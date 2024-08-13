// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ProfileExplorer.Core.Collections;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.UI.Compilers;
using ProtoBuf;

namespace ProfileExplorer.UI.Profile;

//? TODO Perf improvements ideas
//? - Per-process stacks and samples, reduces dict pressure
//?     - also removes need to have ProcessId in sample
//? - Check .NET 8 frozen collections

[ProtoContract(SkipConstructor = true)]
public class RawProfileData : IDisposable {
  private static ProfileContext tempContext_ = new();
  private static ProfileStack tempStack_ = new();

  // Per-thread caches to speed up lookups.
  [ThreadStatic]
  private static List<(int ProcessId, IpToImageCache Cache)> ipImageCache_;
  [ThreadStatic]
  private static ProfileImage lastIpImage_;
  [ThreadStatic]
  private static IpToImageCache globalIpImageCache_;
  [ProtoMember(1)]
  private List<ProfileSample> samples_;
  [ProtoMember(2)]
  private Dictionary<int, ProfileProcess> processes_;
  [ProtoMember(3)]
  private List<ProfileThread> threads_;
  [ProtoMember(4)]
  private List<ProfileContext> contexts_;
  [ProtoMember(5)]
  private List<ProfileImage> images_;
  [ProtoMember(6)]
  private List<ProfileStack> stacks_;
  [ProtoMember(7)]
  private List<PerformanceCounter> perfCounters_;
  [ProtoMember(8)]
  private CompressedSegmentedList<PerformanceCounterEvent> perfCountersEvents_;
  [ProtoMember(9)]
  private ProfileTraceInfo traceInfo_;

  // Objects used only while building the profile.
  private Dictionary<ProfileThread, int> threadsMap_;
  private Dictionary<ProfileContext, int> contextsMap_;
  private Dictionary<ProfileImage, int> imagesMap_;
  private Dictionary<int, Dictionary<ProfileStack, int>> stacksMap_;
  private Dictionary<int, Dictionary<long, SymbolFileDescriptor>> imageSymbols_;
  private HashSet<long[]> stackData_;
  private Dictionary<int, ManagedRawProfileData> procManagedDataMap_;
  private Dictionary<ProfileStack, int> lastProcStacks_;
  private int lastProcId_;

  public RawProfileData(string tracePath, bool handlesDotNetEvents = false) {
    traceInfo_ = new ProfileTraceInfo(tracePath);
    contexts_ = new List<ProfileContext>();
    contextsMap_ = new Dictionary<ProfileContext, int>();
    images_ = new List<ProfileImage>();
    imagesMap_ = new Dictionary<ProfileImage, int>();
    processes_ = new Dictionary<int, ProfileProcess>();
    threads_ = new List<ProfileThread>();
    threadsMap_ = new Dictionary<ProfileThread, int>();
    stacks_ = new List<ProfileStack>();
    stacksMap_ = new Dictionary<int, Dictionary<ProfileStack, int>>();
    stackData_ = new HashSet<long[]>(new StackComparer());
    samples_ = new List<ProfileSample>();
    perfCounters_ = new List<PerformanceCounter>();
    imageSymbols_ = new Dictionary<int, Dictionary<long, SymbolFileDescriptor>>();

    if (handlesDotNetEvents) {
      procManagedDataMap_ = new Dictionary<int, ManagedRawProfileData>();
    }
  }

  public ProfileTraceInfo TraceInfo => traceInfo_;
  public List<ProfileSample> Samples => samples_;
  public List<ProfileProcess> Processes => processes_.ToValueList();
  public List<ProfileThread> Threads => threads_;
  public List<ProfileImage> Images => images_;
  public CompressedSegmentedList<PerformanceCounterEvent> PerformanceCountersEvents => perfCountersEvents_;
  public List<PerformanceCounter> PerformanceCounters => perfCounters_;
  public bool HasPerformanceCountersEvents => PerformanceCountersEvents is {Count: > 0};

  public bool HasManagedMethods(int processId) {
    return procManagedDataMap_ != null && procManagedDataMap_.ContainsKey(processId);
  }

  public int ComputeSampleChunkLength(int chunks) {
    int chunkSize = Math.Max(1, samples_.Count / chunks);
    chunkSize = CompressedSegmentedList<ProfileSample>.RoundUpToSegmentLength(chunkSize);
    return Math.Min(chunkSize, samples_.Count);
  }

  public int ComputePerfCounterChunkLength(int chunks) {
    if (perfCountersEvents_ == null) {
      return 0;
    }

    int chunkSize = Math.Max(1, perfCountersEvents_.Count / chunks);
    chunkSize = CompressedSegmentedList<PerformanceCounterEvent>.RoundUpToSegmentLength(chunkSize);
    return Math.Min(chunkSize, perfCountersEvents_.Count);
  }

  public void AddManagedMethodMapping(long moduleId, long methodId, long rejitId,
                                      FunctionDebugInfo functionDebugInfo,
                                      long ip, int size, int processId) {
    var (moduleDebugInfo, moduleImage) = GetModuleDebugInfo(processId, moduleId);

    var mapping = new ManagedMethodMapping(functionDebugInfo, moduleImage, moduleId, ip, size);
    var data = GetOrCreateManagedData(processId);
    data.managedMethods_.Add(mapping);

    if (moduleImage == null) {
      // A placeholder is created for cases where the method load event
      // is triggered before the module load one, add mapping to patch list
      // to have the image filled in later.
      data.patchedMappings_.Add((moduleId, mapping));
    }

    //? TODO: MethodID is identical between opt levels, either split by level or use time like TraceEvent
    string initialName = functionDebugInfo.Name;

    //if (data.managedMethodsMap_.TryGetValue(initialName, out var other)) {
    //    if (other.FunctionDebugInfo.HasOptimizationLevel &&
    //        !other.FunctionDebugInfo.Name.EndsWith(other.FunctionDebugInfo.OptimizationLevel)) {
    //        other.FunctionDebugInfo.UpdateName($"{other.FunctionDebugInfo.Name}_{other.FunctionDebugInfo.OptimizationLevel}");
    //    }

    //    if (functionDebugInfo.HasOptimizationLevel) {
    //        functionDebugInfo.UpdateName($"{functionDebugInfo.Name}_{functionDebugInfo.OptimizationLevel}");
    //    }
    //}

    moduleDebugInfo.AddFunctionInfo(functionDebugInfo);
    data.managedMethodIdMap_[new ManagedMethodId(methodId, rejitId)] = mapping;
    data.managedMethodsMap_[initialName] = mapping;
  }

  public void AddManagedMethodCode(long functionId, int rejitId, int processId, long address, int codeSize,
                                   byte[] codeBytes) {
    var info = new DotNetDebugInfoProvider.MethodCode(address, codeSize, codeBytes);
    var data = GetOrCreateManagedData(processId);
    data.managedMethodCodeMap_[new ManagedMethodId(functionId, rejitId)] = info;
  }

  public void AddManagedMethodCallTarget(long functionId, int rejitId, int processId, long address, string name) {
    var data = GetOrCreateManagedData(processId);

    if (data.managedMethodCodeMap_.TryGetValue(new ManagedMethodId(functionId, rejitId), out var code)) {
      code.CallTargets.Add(new DotNetDebugInfoProvider.AddressNamePair(address, name));
    }
  }

  public ManagedMethodMapping FindManagedMethodForIP(long ip, int processId) {
    var data = GetOrCreateManagedData(processId);
    return ManagedMethodMapping.BinarySearch(data.managedMethods_, ip);
  }

  public ManagedMethodMapping FindManagedMethod(long id, long rejitId, int processId) {
    var data = GetOrCreateManagedData(processId);
    return data.managedMethodIdMap_.GetValueOrNull(new ManagedMethodId(id, rejitId));
  }

  public IDebugInfoProvider GetDebugInfoForManagedImage(ProfileImage image, int processId) {
    if (!HasManagedMethods(processId)) {
      return null;
    }

    var data = GetOrCreateManagedData(processId);
    return data.imageDebugInfo_?.GetValueOrNull(image);
  }

  public void AddDebugFileForImage(SymbolFileDescriptor symbolFile, long imageBase, int processId) {
    var procImageSymbols = imageSymbols_.GetOrAddValue(processId);
    procImageSymbols[imageBase] = symbolFile;
  }

  public SymbolFileDescriptor GetDebugFileForImage(ProfileImage image, int processId) {
    // If the module is loaded in kernel address space,
    // look for the debug info entry in the kernel process.
    if (ETWEventProcessor.IsKernelAddress((ulong)image.BaseAddress, TraceInfo.PointerSize)) {
      processId = ETWEventProcessor.KernelProcessId;
    }

    var procImageSymbols = imageSymbols_.GetOrAddValue(processId);

    if (procImageSymbols == null) {
      return null;
    }

    return procImageSymbols.GetValueOrNull(image.BaseAddress);
  }

  public DotNetDebugInfoProvider GetOrAddManagedModuleDebugInfo(int processId, string moduleName,
                                                                long moduleId, Machine architecture) {
    var data = GetOrCreateManagedData(processId);
    var proc = GetOrCreateProcess(processId);

    foreach (var image in proc.Images(this)) {
      //? TODO: Avoid linear search

      if (image.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase)) {
        if (!data.imageDebugInfo_.TryGetValue(image, out var debugInfo)) {
          // A placeholder is created for cases where the method load event
          // is triggered before the module load one, use that provider.
          if (!data.moduleDebugInfoMap_.TryGetValue(moduleId, out debugInfo)) {
            debugInfo = new DotNetDebugInfoProvider(architecture);
          }
          else {
            debugInfo.UpdateArchitecture(architecture);

            foreach (var pair in data.patchedMappings_) {
              //? TODO: List shouldn't grow too much
              if (pair.ModuleId == moduleId) {
                pair.Mapping.Image = image;
              }
            }
          }

          data.imageDebugInfo_[image] = debugInfo;
          data.moduleDebugInfoMap_[moduleId] = debugInfo;
          data.moduleImageMap_[moduleId] = image;
        }

        return debugInfo;
      }
    }

    return null;
  }

  public (DotNetDebugInfoProvider, ProfileImage) GetModuleDebugInfo(int processId, long moduleId) {
    var data = GetOrCreateManagedData(processId);

    var debugInfo = data.moduleDebugInfoMap_.GetValueOrNull(moduleId);
    var image = data.moduleImageMap_.GetValueOrNull(moduleId);

    if (debugInfo == null) {
      // Module loaded event not triggered yet, create a placeholder for now.
      debugInfo = new DotNetDebugInfoProvider(Machine.Unknown);
      data.moduleDebugInfoMap_[moduleId] = debugInfo;
    }

    return (debugInfo, image);
  }

  public void LoadingCompleted() {
    // Free objects used while reading the profile.
    stacksMap_ = null;
    contextsMap_ = null;
    imagesMap_ = null;
    threadsMap_ = null;
    stackData_ = null;
    lastProcStacks_ = null;

    // Wait for any compression tasks.
    perfCountersEvents_?.Wait();
  }

  public void ManagedLoadingCompleted() {
    if (procManagedDataMap_ != null) {
      foreach (var pair in procManagedDataMap_) {
        pair.Value.LoadingCompleted(pair.Key);
      }
    }
  }

  public ProfileProcess GetOrCreateProcess(int id) {
    if (processes_.TryGetValue(id, out var process)) {
      return process;
    }

    return processes_.GetOrAddValue(id, () => new ProfileProcess(id));
  }

  public void AddProcess(ProfileProcess process) {
    processes_[process.ProcessId] = process;
  }

  public int AddImage(ProfileImage image) {
    if (!imagesMap_.TryGetValue(image, out int existingImageId)) {
      images_.Add(image);
      int id = images_.Count;
      image.Id = id;
      imagesMap_[image] = id;
      existingImageId = id;
    }

    return existingImageId;
  }

  public ProfileImage FindImage(int id) {
    Debug.Assert(id > 0 && id <= images_.Count);
    return images_[id - 1];
  }

  public int AddThread(ProfileThread thread) {
    if (!threadsMap_.TryGetValue(thread, out int existingThread)) {
      threads_.Add(thread);
      existingThread = threads_.Count;
      threadsMap_[thread] = threads_.Count;
    }

    return existingThread;
  }

  public ProfileThread FindThread(int id) {
    Debug.Assert(id > 0 && id <= threads_.Count);
    return threads_[id - 1];
  }

  public int AddThreadToProcess(int processId, ProfileThread thread) {
    var proc = GetOrCreateProcess(processId);
    int result = AddThread(thread);
    proc.AddThread(result);
    return result;
  }

  public int AddImageToProcess(int processId, ProfileImage image) {
    var proc = GetOrCreateProcess(processId);
    int result = AddImage(image);
    proc.AddImage(result);
    return result;
  }

  public int AddSample(ProfileSample sample) {
    Debug.Assert(sample.ContextId != 0);
    samples_.Add(sample);
    return samples_.Count;
  }

  public bool TrySetSampleStack(int sampleId, int stackId, long frameIp, int contextId) {
    if (sampleId == 0) {
      return false;
    }

    if (samples_[sampleId - 1].ContextId == contextId &&
        samples_[sampleId - 1].IP == frameIp) {
      SetSampleStack(sampleId, stackId, contextId);
      return true;
    }

    return false;
  }

  public void SetSampleStack(int sampleId, int stackId, int contextId) {
    // Change the stack ID in-place in the array.
    Debug.Assert(samples_[sampleId - 1].ContextId == contextId);
    CollectionsMarshal.AsSpan(samples_)[sampleId - 1].StackId = stackId;
  }

  public int AddPerformanceCounter(PerformanceCounter counter) {
    perfCounters_.Add(counter);
    return perfCounters_.Count;
  }

  public int AddPerformanceCounterEvent(PerformanceCounterEvent counterEvent) {
    Debug.Assert(counterEvent.ContextId != 0);
    perfCountersEvents_ ??= new CompressedSegmentedList<PerformanceCounterEvent>();
    perfCountersEvents_.Add(counterEvent);
    return perfCountersEvents_.Count;
  }

  public ProfileContext FindContext(int id) {
    Debug.Assert(id > 0 && id <= contexts_.Count);
    return contexts_[id - 1];
  }

  public int AddStack(ProfileStack stack, ProfileContext context) {
    Debug.Assert(stack.ContextId != 0);
    Dictionary<ProfileStack, int> procStacks = null;

    if (lastProcId_ == context.ProcessId && lastProcStacks_ != null) {
      procStacks = lastProcStacks_;
    }
    else {
      if (!stacksMap_.TryGetValue(context.ProcessId, out procStacks)) {
        procStacks = new Dictionary<ProfileStack, int>();
        stacksMap_[context.ProcessId] = procStacks;
      }

      lastProcId_ = context.ProcessId;
      lastProcStacks_ = procStacks;
    }

    //? TODO:  Stack hash computed 3 times,
    //? do it once and inject it into the ProfileCallStack and StackComparer used by set

    if (!procStacks.TryGetValue(stack, out int existingStackId)) {
      // De-duplicate the stack frame pointer array,
      // since lots of samples have identical stacks.
      if (!stackData_.TryGetValue(stack.FramePointers, out long[] framePtrData)) {
        // Make a clone since the temporary stack uses a static array.
        framePtrData = stack.CloneFramePointers();
        stackData_.Add(framePtrData);
      }

      var newStack = new ProfileStack(stack.ContextId, framePtrData);
      stacks_.Add(newStack);
      procStacks[newStack] = stacks_.Count;
      existingStackId = stacks_.Count;
    }
    else {
      Debug.Assert(stack == FindStack(existingStackId));
    }

    stack.Discard();
    return existingStackId;
  }

  public void ReplaceStackFramePointers(ProfileStack stack, long[] newFramePtrs, ProfileContext context) {
    if (stacksMap_.TryGetValue(context.ProcessId, out var procStacks)) {
      procStacks.Remove(stack);
    }

    stack.FramePointers = newFramePtrs;
    AddStack(stack, context);
  }

  public ProfileStack FindStack(int id) {
    Debug.Assert(id > 0 && id <= stacks_.Count);
    return stacks_[id - 1];
  }

  public ProfileProcess FindProcess(string name, bool allowSubstring = false) {
    foreach (var process in processes_.Values) {
      if (process.Name == name) {
        return process;
      }
    }

    if (allowSubstring) {
      foreach (var process in processes_.Values) {
        if (process.Name.Contains(name)) {
          return process;
        }
      }
    }

    return null;
  }

  public ProfileProcess FindProcess(int processId) {
    return processes_.GetValueOrNull(processId);
  }

  public ProfileImage FindImage(ProfileProcess process, BinaryFileDescriptor info) {
    foreach (var image in process.Images(this)) {
      if (image.FilePath == info.ImageName &&
          image.TimeStamp == info.TimeStamp &&
          image.Checksum == info.Checksum) {
        return image;
      }
    }

    return null;
  }

  public ProfileImage FindImageForIP(long ip, ProfileContext context) {
    return FindImageForIP(ip, context.ProcessId);
  }

  public ProfileImage FindImageForIP(long ip, int processId) {
    // lastIpImage_ and ipImageCache_ are thread-local,
    // making this function thread-safe.
    if (lastIpImage_ != null && lastIpImage_.HasAddress(ip)) {
      return lastIpImage_;
    }

    ipImageCache_ ??= new List<(int ProcessId, IpToImageCache Cache)>();
    IpToImageCache cache = null;

    foreach (var entry in ipImageCache_) {
      if (entry.ProcessId == processId) {
        cache = entry.Cache;
        break;
      }
    }

    if (cache == null) {
      var process = GetOrCreateProcess(processId);
      cache = IpToImageCache.Create(process.Images(this));
      ipImageCache_.Add((processId, cache));
    }

    if (!cache.IsValidAddres(ip)) {
      return null;
    }

    var result = cache.Find(ip);

    if (result != null) {
      lastIpImage_ = result;
      return result;
    }

    // Trace.WriteLine($"No image for ip {ip:X}");
    return null;
  }

  public ProfileImage FindImageForIP(long ip) {
    if (globalIpImageCache_ == null) {
      // Per-thread, no locks needed.
      globalIpImageCache_ = IpToImageCache.Create(images_);
    }

    return globalIpImageCache_.Find(ip);
  }

  public List<ProcessSummary> BuildProcessSummary() {
    var builder = new ProcessSummaryBuilder(this);

    foreach (var sample in samples_) {
      builder.AddSample(sample);
    }

    return builder.MakeSummaries();
  }

  public void PrintProcess(int processId) {
    var proc = GetOrCreateProcess(processId);

    if (proc != null) {
      Trace.WriteLine($"Process {proc}");

      foreach (var image in proc.Images(this)) {
        Trace.WriteLine($"Image: {image}");

        if (HasManagedMethods(proc.ProcessId)) {
          var data = GetOrCreateManagedData(proc.ProcessId);
          Trace.WriteLine($"   o methods: {data.managedMethods_.Count}");
          Trace.WriteLine($"   o hasDebug: {data.imageDebugInfo_ != null && data.imageDebugInfo_.ContainsKey(image)}");
        }
      }
    }
  }

  public void PrintAllProcesses() {
    Trace.WriteLine($"Profile processes: {processes_.Count}");

    foreach (var proc in processes_) {
      Trace.WriteLine($"- {proc}");
    }
  }

  public void PrintPerfCounters(int processId) {
    if (perfCountersEvents_ == null) {
      return;
    }

    foreach (var sample in perfCountersEvents_) {
      var context = sample.GetContext(this);

      if (context.ProcessId == processId) {
        Trace.WriteLine($"{sample}");
      }
    }
  }

  public void PrintSamples(int processId) {
    foreach (var sample in samples_) {
      var context = sample.GetContext(this);

      if (context.ProcessId == processId) {
        Trace.WriteLine($"{sample}");
      }
    }
  }

  public void Dispose() {
    perfCountersEvents_?.Dispose();
  }

  internal ProfileContext RentTempContext(int processId, int threadId, int processorNumber) {
    tempContext_.ProcessId = processId;
    tempContext_.ThreadId = threadId;
    tempContext_.ProcessorNumber = processorNumber;
    return tempContext_;
  }

  internal void ReturnContext(int contextId) {
    var context = FindContext(contextId);

    if (ReferenceEquals(context, tempContext_)) {
      tempContext_ = new ProfileContext();
    }
  }

  internal int AddContext(ProfileContext context) {
    ref var existingContextId = ref CollectionsMarshal.GetValueRefOrAddDefault(contextsMap_, context, out bool exists);

    if (!exists) {
      contexts_.Add(context);
      existingContextId = contexts_.Count;
    }

    return existingContextId;
  }

  internal ProfileStack RentTemporaryStack(int frameCount, int contextId) {
    tempStack_.SetTempFramePointers(frameCount);
    tempStack_.ContextId = contextId;
    return tempStack_;
  }

  internal void ReturnStack(int stackId) {
    var stack = FindStack(stackId);

    if (ReferenceEquals(stack, tempStack_)) {
      tempStack_ = new ProfileStack();
    }
  }

  private ManagedRawProfileData GetOrCreateManagedData(int processId) {
    if (!procManagedDataMap_.TryGetValue(processId, out var data)) {
      data = new ManagedRawProfileData();
      procManagedDataMap_[processId] = data;
    }

    return data;
  }
}

public class StackComparer : IEqualityComparer<long[]> {
  public bool Equals(long[] data1, long[] data2) {
    return AreEqual(data1, data2);
  }

  public int GetHashCode(long[] data) {
    return ComputeHashCode(data);
  }

  public static bool AreEqual(long[] data1, long[] data2) {
    if (data1.Length != data2.Length) {
      return false;
    }

    return data1.AsSpan().SequenceEqual(data2.AsSpan());
  }

  public static int ComputeHashCode(long[] data) {
    HashCode hash = new();
    hash.AddBytes(MemoryMarshal.AsBytes(data.AsSpan()));
    return hash.ToHashCode();
  }
}

public class IpToImageCache {
  private List<ProfileImage> images_;
  private long lowestBaseAddress_;

  public IpToImageCache(IEnumerable<ProfileImage> images, long lowestBaseAddress) {
    lowestBaseAddress_ = lowestBaseAddress;
    images_ = new List<ProfileImage>(images);
    images_.Sort();
  }

  public static IpToImageCache Create(IEnumerable<ProfileImage> images) {
    long lowestAddr = long.MaxValue;

    foreach (var image in images) {
      lowestAddr = Math.Min(lowestAddr, image.BaseAddress);
    }

    return new IpToImageCache(images, lowestAddr);
  }

  public bool IsValidAddres(long ip) {
    return ip >= lowestBaseAddress_;
  }

  public ProfileImage Find(long ip) {
    Debug.Assert(IsValidAddres(ip));
    return BinarySearch(images_, ip);
  }

  private ProfileImage BinarySearch(List<ProfileImage> ranges, long value) {
    int min = 0;
    int max = ranges.Count - 1;

    while (min <= max) {
      int mid = (min + max) / 2;
      var range = ranges[mid];
      int comparison = range.CompareTo(value);

      if (comparison == 0) {
        return range;
      }

      if (comparison < 0) {
        min = mid + 1;
      }
      else {
        max = mid - 1;
      }
    }

    return null;
  }
}

public class ProcessSummaryBuilder {
  private RawProfileData profile_;
  private Dictionary<ProfileProcess, TimeSpan> processSamples_ = new();
  private Dictionary<ProfileProcess, (TimeSpan First, TimeSpan Last)> procDuration_ = new();
  private TimeSpan totalWeight_;

  public ProcessSummaryBuilder(RawProfileData profile) {
    profile_ = profile;
  }

  public void AddSample(ProfileSample sample) {
    var context = sample.GetContext(profile_);
    var process = profile_.GetOrCreateProcess(context.ProcessId);
    processSamples_.AccumulateValue(process, sample.Weight);
    totalWeight_ += sample.Weight;

    // Modify in-place.
    ref var durationRef = ref CollectionsMarshal.GetValueRefOrAddDefault(procDuration_, process, out bool found);

    if (!found) {
      durationRef.First = sample.Time;
    }

    durationRef.Last = sample.Time;
  }

  public void AddSample(TimeSpan sampleWeight, TimeSpan sampleTime, int processId) {
    var process = profile_.GetOrCreateProcess(processId);
    processSamples_.AccumulateValue(process, sampleWeight);
    totalWeight_ += sampleWeight;

    // Modify in-place.
    ref var durationRef = ref CollectionsMarshal.GetValueRefOrAddDefault(procDuration_, process, out bool found);

    if (!found) {
      durationRef.First = sampleTime;
    }

    durationRef.Last = sampleTime;
  }

  public List<ProcessSummary> MakeSummaries() {
    var list = new List<ProcessSummary>(procDuration_.Count);

    foreach (var pair in processSamples_) {
      var item = new ProcessSummary(pair.Key, pair.Value) {
        WeightPercentage = 100 * (double)pair.Value.Ticks / totalWeight_.Ticks
      };

      list.Add(item);

      if (procDuration_.TryGetValue(pair.Key, out var duration)) {
        item.Duration = duration.Last - duration.First;
      }
    }

    return list;
  }
}