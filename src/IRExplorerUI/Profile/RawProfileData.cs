using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using IRExplorerUI.Utilities;
using Microsoft.Diagnostics.Runtime;
using PEFile;
using ProtoBuf;
using static IRExplorerUI.Profile.ETWProfileDataProvider;

namespace IRExplorerUI.Profile;

//? TODO Perf
//? - use chunked list for samples and stack
//? - use chunked dict?  
//? - compress stacks?
//? - Per-process stacks and samples, reduces dict pressure
//?     - also removes need to have ProcessId in sample

[ProtoContract(SkipConstructor = true)]
public class RawProfileData {
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
    private CompressedSegmentedList<ProfileSample> samples_;
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
    private HashSet<long[]> stackData_;
    private Dictionary<int, ManagedData> procManagedDataMap_;

    private Dictionary<ProfileStack, int> lastProcStacks_;
    private int lastProcId_;

    public ProfileTraceInfo TraceInfo => traceInfo_;
    public CompressedSegmentedList<ProfileSample> Samples => samples_;
    public List<ProfileProcess> Processes => processes_.ToValueList();
    public List<ProfileThread> Threads => threads_;
    public List<ProfileImage> Images => images_;
    public CompressedSegmentedList<PerformanceCounterEvent> PerformanceCountersEvents => perfCountersEvents_;
    public List<PerformanceCounter> PerformanceCounters => perfCounters_;

    public bool HasManagedMethods(int processId) => procManagedDataMap_ != null && procManagedDataMap_.ContainsKey(processId);

    public int ComputeSampleChunkLength(int chunks) {
        int chunkSize = Math.Max(1, samples_.Count / chunks);
        chunkSize = CompressedSegmentedList<ProfileSample>.RoundUpToSegmentLength(chunkSize);
        return Math.Min(chunkSize, samples_.Count);
    }

    public int ComputePerfCounterChunkLength(int chunks) {
        int chunkSize = Math.Max(1, perfCountersEvents_.Count / chunks);
        chunkSize = CompressedSegmentedList<PerformanceCounterEvent>.RoundUpToSegmentLength(chunkSize);
        return Math.Min(chunkSize, perfCountersEvents_.Count);
    }

    private ManagedData GetOrCreateManagedData(int processId) {
        if (!procManagedDataMap_.TryGetValue(processId, out var data)) {
            data = new ManagedData();
            procManagedDataMap_[processId] = data;
        }

        return data;
    }

    public void AddManagedMethodMapping(long moduleId, long methodId,
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
        var initialName = functionDebugInfo.Name;

        if (data.managedMethodsMap_.TryGetValue(initialName, out var other)) {
            if (other.FunctionDebugInfo.HasOptimizationLevel &&
                !other.FunctionDebugInfo.Name.EndsWith(other.FunctionDebugInfo.OptimizationLevel)) {
                other.FunctionDebugInfo.Name = $"{other.FunctionDebugInfo.Name}_{other.FunctionDebugInfo.OptimizationLevel}";
            }

            if (functionDebugInfo.HasOptimizationLevel) {
                functionDebugInfo.Name = $"{functionDebugInfo.Name}_{functionDebugInfo.OptimizationLevel}";
            }
        }

        moduleDebugInfo.AddFunctionInfo(functionDebugInfo);
        data.managedMethodIdMap_[methodId] = mapping;
        data.managedMethodsMap_[initialName] = mapping;
    }

    public ManagedMethodMapping FindManagedMethodForIP(long ip, int processId) {
        var data = GetOrCreateManagedData(processId);
        return FunctionDebugInfo.BinarySearch(data.managedMethods_, ip);
    }

    public ManagedMethodMapping FindManagedMethod(long id, int processId) {
        var data = GetOrCreateManagedData(processId);
        return data.managedMethodIdMap_.GetValueOrNull(id);
    }

    public IDebugInfoProvider GetDebugInfoForImage(ProfileImage image, int processId) {
        if (!HasManagedMethods(processId)) {
            return null;
        }

        var data = GetOrCreateManagedData(processId);
        return data.imageDebugInfo_?.GetValueOrNull(image);
    }

    public DotNetDebugInfoProvider GetOrAddModuleDebugInfo(int processId, string moduleName,
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

    public RawProfileData(bool handlesDotNetEvents = false) {
        traceInfo_ = new ProfileTraceInfo();
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
        samples_ = new CompressedSegmentedList<ProfileSample>();
        perfCounters_ = new List<PerformanceCounter>();
        perfCountersEvents_ = new CompressedSegmentedList<PerformanceCounterEvent>();

        if (handlesDotNetEvents) {
            procManagedDataMap_ = new Dictionary<int, ManagedData>();
        }        
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
        samples_.Wait();
        perfCountersEvents_.Wait();
    }

    public void ManagedLoadingCompleted(string managedAsmDir) {
        if (procManagedDataMap_ != null) {
            foreach (var pair in procManagedDataMap_) {
                pair.Value.LoadingCompleted(pair.Key, managedAsmDir);
            }
        }
    }

    public ProfileProcess GetOrCreateProcess(int id) {
        return processes_.GetOrAddValue(id);
    }

    public void AddProcess(ProfileProcess process) {
        processes_[process.ProcessId] = process;
    }

    public int AddImage(ProfileImage image) {
        if (!imagesMap_.TryGetValue(image, out var existingImage)) {
            images_.Add(image);
            int id = images_.Count;
            image.Id = id;
            imagesMap_[image] = id;
            existingImage = id;
        }

        return existingImage;
    }

    public ProfileImage FindImage(int id) {
        Debug.Assert(id > 0 && id <= images_.Count);
        return images_[id - 1];
    }

    public int AddThread(ProfileThread thread) {
        if (!threadsMap_.TryGetValue(thread, out var existingThread)) {
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
        var result = AddThread(thread);
        proc.AddThread(result);
        return result;
    }

    public int AddImageToProcess(int processId, ProfileImage image) {
        var proc = GetOrCreateProcess(processId);
        var result = AddImage(image);
        proc.AddImage(result);
        return result;
    }

    public int AddSample(ProfileSample sample) {
        Debug.Assert(sample.ContextId != 0);
        samples_.Add(sample);
        return samples_.Count;
    }

    public bool TrySetSampleStack(int sampleId, int stackId, int contextId) {
        if (sampleId == 0) {
            return false;
        }

        if (samples_[sampleId - 1].ContextId == contextId) {
            SetSampleStack(sampleId, stackId, contextId);
            return true;
        }

        return false;
    }

    public void SetSampleStack(int sampleId, int stackId, int contextId) {
        // Change the stack ID in-place in the array.
        Debug.Assert(samples_[sampleId - 1].ContextId == contextId);
        ref var sampleRef = ref samples_.GetValueRef(sampleId - 1);
        sampleRef.StackId = stackId;
    }

    public int AddPerformanceCounter(PerformanceCounter counter) {
        perfCounters_.Add(counter);
        return perfCounters_.Count;
    }

    public int AddPerformanceCounterEvent(PerformanceCounterEvent counterEvent) {
        Debug.Assert(counterEvent.ContextId != 0);
        perfCountersEvents_.Add(counterEvent);
        return (int)perfCountersEvents_.Count;
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
        if (!contextsMap_.TryGetValue(context, out var existingContext)) {
            contexts_.Add(context);
            existingContext = contexts_.Count;
            contextsMap_[context] = contexts_.Count;
        }

        return existingContext;
    }

    public ProfileContext FindContext(int id) {
        Debug.Assert(id > 0 && id <= contexts_.Count);
        return contexts_[id - 1];
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

    public int AddStack(ProfileStack stack, ProfileContext context) {
        Debug.Assert(stack.ContextId != 0);

        // De-duplicate the stack frame pointer array,
        // since lots of samples have identical stacks.
        if (!stackData_.TryGetValue(stack.FramePointers, out var framePtrData)) {
            // Make a clone since the temporary stack uses a static array.
            framePtrData = stack.CloneFramePointers();
            stackData_.Add(framePtrData);
        }

        stack.SubstituteFramePointers(framePtrData);
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

        if (!procStacks.TryGetValue(stack, out var existingStack)) {
            stacks_.Add(stack);
            procStacks[stack] = (int)stacks_.Count;
            existingStack = (int)stacks_.Count;
        }
        else {
            Debug.Assert(stack == FindStack(existingStack));
        }

        return existingStack;
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
        var processSamples = new Dictionary<ProfileProcess, TimeSpan>();
        var procDuration = new Dictionary<ProfileProcess, (TimeSpan First, TimeSpan Last)>();
        var totalWeight = TimeSpan.Zero;

        foreach (var sample in samples_) {
            var context = sample.GetContext(this);
            var process = GetOrCreateProcess(context.ProcessId);
            processSamples.AccumulateValue(process, sample.Weight);
            totalWeight += sample.Weight;

            // Modify in-place.
            ref var durationRef = ref CollectionsMarshal.GetValueRefOrAddDefault(procDuration, process, out bool found);

            if (!found) {
                durationRef.First = sample.Time;
            }

            durationRef.Last = sample.Time;
        };

        var list = new List<ProcessSummary>(procDuration.Count);

        foreach (var pair in processSamples) {
            var item = new ProcessSummary(pair.Key, pair.Value) {
                WeightPercentage = 100 * (double)pair.Value.Ticks / (double)totalWeight.Ticks
            };

            list.Add(item);

            if (procDuration.TryGetValue(pair.Key, out var duration)) {
                item.Duration = duration.Last - duration.First;
            }
        }

        return list;
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

        foreach(var proc in processes_) {
            Trace.WriteLine($"- {proc}");

            //if (proc.Value.Name.Contains("bench")) {
            //    PrintPerfCounters(proc.Value.ProcessId);
            //    Trace.WriteLine("================================================\n");
            //}
        }
    }

    public void PrintPerfCounters(int processId) {
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

    private class StackComparer : IEqualityComparer<long[]> {
        public unsafe bool Equals(long[] data1, long[] data2) {
            fixed (long* p1 = data1, p2 = data2) {
                return new Span<long>(p1, data1.Length).
                    SequenceEqual(new Span<long>(p2, data2.Length));
            }
        }

        public int GetHashCode(long[] data) {
            int hash = 0;
            int left = data.Length;
            int i = 0;

            while (left >= 4) {
                hash = HashCode.Combine(hash, data[i], data[i + 1], data[i + 2], data[i + 3]);
                left -= 4;
                i += 4;
            }

            while (left > 0) {
                hash = HashCode.Combine(hash, data[i]);
                left--;
                i++;
            }

            return hash;
        }
    }
}

[ProtoContract(SkipConstructor = true)]
public class ManagedMethodMapping : IComparable<ManagedMethodMapping>, IComparable<long>, IEquatable<ManagedMethodMapping> {
    public ManagedMethodMapping(FunctionDebugInfo functionDebugInfo, ProfileImage image, 
                                long moduleId, long ip, int size) {
        FunctionDebugInfo = functionDebugInfo;
        Image = image;
        ModuleId = moduleId;
        IP = ip;
        Size = size;
    }

    [ProtoMember(1)]
    public FunctionDebugInfo FunctionDebugInfo { get; }
    [ProtoMember(2)]
    public ProfileImage Image { get; set; }
    [ProtoMember(3)]
    public long ModuleId { get; }
    [ProtoMember(4)]
    public long IP { get; }
    [ProtoMember(5)]
    public int Size { get; }

    public int CompareTo(long value) {
        if (value < IP) {
            return 1;
        }
        if (value > IP + Size) {
            return -1;
        }

        return 0;
    }

    public bool Equals(ManagedMethodMapping other) {
        if (other == null) return false;
        return IP == other.IP;
    }

    public int CompareTo(ManagedMethodMapping other) {
        return CompareTo(other.IP);
    }

    public override bool Equals(object obj) {
        return obj is ManagedMethodMapping other && Equals(other);
    }

    public override int GetHashCode() {
        return IP.GetHashCode();
    }
}

public class ManagedData {
    [ProtoContract(SkipConstructor = true)]
    public class ManagedDataState {
        // list of DotNetDebugInfoProvider {id, file_name, arch}

        public Dictionary<ProfileImage, int /* providerId */> ImageDebugInfo;
        public Dictionary<long /* moduleId */, int /* providerId */> moduleDebugInfoMap_;
        public Dictionary<long /* methodId */, int /* mappingId */> managedMethodIdMap_;
        public Dictionary<string, int /* mappingId */> managedMethodsMap_;
        public List<ManagedMethodMapping> managedMethods_;


    }

    public Dictionary<ProfileImage, DotNetDebugInfoProvider> imageDebugInfo_;
    public Dictionary<long /* moduleId */, DotNetDebugInfoProvider> moduleDebugInfoMap_;
    public Dictionary<long /* moduleId */, ProfileImage> moduleImageMap_;
    public Dictionary<long /* methodId */, ManagedMethodMapping> managedMethodIdMap_;
    public Dictionary<string, ManagedMethodMapping> managedMethodsMap_;
    public List<ManagedMethodMapping> managedMethods_;
    public List<(long ModuleId, ManagedMethodMapping Mapping)> patchedMappings_;

    public ManagedData() {
        imageDebugInfo_ = new Dictionary<ProfileImage, DotNetDebugInfoProvider>();
        moduleDebugInfoMap_ = new Dictionary<long, DotNetDebugInfoProvider>();
        moduleImageMap_ = new Dictionary<long, ProfileImage>();
        managedMethods_ = new List<ManagedMethodMapping>();
        managedMethodsMap_ = new Dictionary<string, ManagedMethodMapping>();
        managedMethodIdMap_ = new Dictionary<long, ManagedMethodMapping>();
        patchedMappings_ = new List<(long ModuleId, ManagedMethodMapping Mapping)>();
    }

    public void LoadingCompleted(int processId, string managedAsmDir) {
        managedMethods_.Sort();

        if (!string.IsNullOrEmpty(managedAsmDir)) {
            foreach (var debugInfo in moduleDebugInfoMap_.Values) {
                var asmFilePath = Path.Combine(managedAsmDir, $"{processId}.asm");
                debugInfo.ManagedAsmFilePath = asmFilePath;
            }
        }

        // A placeholder is created for cases where the method load event
        // is triggered before the module load one, try to assign the image now.
        foreach (var pair in patchedMappings_) {
            pair.Mapping.Image = moduleImageMap_.GetValueOrNull(pair.ModuleId);
        }

        patchedMappings_ = null;
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
        long lowestAddr = Int64.MaxValue;

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

    ProfileImage BinarySearch(List<ProfileImage> ranges, long value) {
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
