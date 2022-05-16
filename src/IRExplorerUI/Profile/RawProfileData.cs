using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.Diagnostics.Runtime;
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
    private static ProfileContext tempContext_ = new ProfileContext();
    private static ProfileStack tempStack_ = new ProfileStack();

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

    // Objects used only while building the profile.
    private Dictionary<ProfileThread, int> threadsMap_;
    private Dictionary<ProfileContext, int> contextsMap_;
    private Dictionary<ProfileImage, int> imagesMap_;
    private Dictionary<int, Dictionary<ProfileStack, int>> stacksMap_;
    private HashSet<long[]> stackData_;
    private Dictionary<int, ManagedData> procManagedDataMap_;

    private Dictionary<ProfileStack, int> lastProcStacks_;
    private int lastProcId_;

    public List<ProfileSample> Samples => samples_;
    public List<ProfileProcess> Processes => processes_.ToValueList();
    public List<ProfileThread> Threads => threads_;
    public List<ProfileImage> Images => images_;
    public List<PerformanceCounterEvent> PerfCounters { get; set; }



    public bool HasManagedMethods(int processId) => procManagedDataMap_ != null && procManagedDataMap_.ContainsKey(processId);

    private ManagedData GetOrCreateManagedData(int processId) {
        if (!procManagedDataMap_.TryGetValue(processId, out var data)) {
            data = new ManagedData();
            procManagedDataMap_[processId] = data;
        }

        return data;
    }

    public void AddManagedMethodMapping(long methodId, DebugFunctionInfo debugInfo, ProfileImage image, 
                                        long ip, int size, int processId) {
        var mapping = new ManagedMethodMapping(debugInfo, image, ip, size);
        var data = GetOrCreateManagedData(processId);
        data.managedMethods_.Add(mapping);
        data.managedMethodIdMap_[methodId] = mapping;
    }

    public ManagedMethodMapping FindManagedMethodForIP(long ip, int processId) {
        var data = GetOrCreateManagedData(processId);
        return DebugFunctionInfo.BinarySearch(data.managedMethods_, ip);
    }

    public ManagedMethodMapping FindManagedMethod(long id, int processId) {
        var data = GetOrCreateManagedData(processId);
        return data.managedMethodIdMap_.GetValueOr(id, ManagedMethodMapping.Unknown);
    }

    public IDebugInfoProvider GetDebugInfoForImage(ProfileImage image, int processId) {
        if (!HasManagedMethods(processId)) {
            return null;
        }

        Trace.WriteLine($"=> Query IDebugInfoProvider for {image.FilePath}");
        var data = GetOrCreateManagedData(processId);
        return data.imageDebugInfo_?.GetValueOrNull(image);
    }

    public (DotNetDebugInfoProvider, ProfileImage) 
        GetOrAddModuleDebugInfo(int processId, string moduleName, long moduleBase, Machine architecture) {
        var data = GetOrCreateManagedData(processId);
        var proc = GetOrCreateProcess(processId);

        Trace.WriteLine($"GetOrAddModuleDebugInfo for {moduleBase}: {moduleName} in proc {processId}");

        //? Use baseAddr -> image map

        foreach (var image in proc.Images(this)) {
            //? TODO: Avoid linear search
            
            if (image.ModuleName.Equals(moduleName, StringComparison.Ordinal)) {
                //? TODO: Maybe patch image? What about R2R, that likely have both native and JIT associated

                if (image.BaseAddress != moduleBase) {
                    Trace.WriteLine($"=> MISMATCH {image.BaseAddress}, {moduleBase}");
                }

                if (!data.imageDebugInfo_.TryGetValue(image, out var debugInfo)) {
                    debugInfo = new DotNetDebugInfoProvider(architecture);
                    data.imageDebugInfo_[image] = debugInfo;
                }

                return (debugInfo, image);
            }
        }

        return (null, null);
    }

    public RawProfileData(bool handlesDotNetEvents = false) {
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
        PerfCounters = new List<PerformanceCounterEvent>();

        if (handlesDotNetEvents) {
            procManagedDataMap_ = new Dictionary<int, ManagedData>();
        }        
    }
    
    public void LoadingCompleted() {
        if (procManagedDataMap_ != null) {
            foreach (var pair in procManagedDataMap_) {
                pair.Value.managedMethods_.Sort();
            }
        }

        // Free objects used while reading the profile.
        stacksMap_ = null;
        contextsMap_ = null;
        imagesMap_ = null;
        threadsMap_ = null;
        stackData_ = null;
        lastProcStacks_ = null;
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

    //? TODO: Per-process samples, reduces dict pressure

    public int AddSample(ProfileSample sample) {
        Debug.Assert(sample.ContextId != 0);
        samples_.Add(sample);

        return (int)samples_.Count;
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
        var span = CollectionsMarshal.AsSpan(samples_);
        ref var sampleRef = ref span[sampleId - 1];
        sampleRef.StackId = stackId;
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

    //? TODO: Per-process stack, reduces dict pressure
    public int AddStack(ProfileStack stack, ProfileContext context) {
        Debug.Assert(stack.ContextId != 0);

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

    public ProfileImage FindImage(ProfileProcess process, BinaryFileDescription info) {
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
        return FindImageForIP(ip, context.ProcessId, context.ThreadId);
    }

    private ProfileImage FindImageForIP(long ip, int processId, int threadId) {
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
            globalIpImageCache_ = IpToImageCache.Create(images_);
        }

        return globalIpImageCache_.Find(ip);
    }

    public List<TraceProcessSummary> BuildProcessSummary() {
        var list = new List<TraceProcessSummary>();
        var processSamples = new Dictionary<ProfileProcess, int>();
        //var procImageW = new Dictionary<ProfileProcess, Dictionary<ProfileImage, TimeSpan>>();
        var procDuration = new Dictionary<ProfileProcess, (TimeSpan First, TimeSpan Last)>();

        foreach (var sample in samples_) {
            var context = sample.GetContext(this);
            var process = GetOrCreateProcess(context.ProcessId);
            processSamples.AccumulateValue(process, 1);

            if (!procDuration.TryGetValue(process, out var duration)) {
                duration = (sample.Time, sample.Time);
                procDuration[process] = duration;
            }
            else {
                //? TODO: Inefficient, use ref to value to change in place
                duration.Last = sample.Time;
                procDuration[process] = duration;
            }

            //var image = FindImageForIP(sample.IP, context);

            //if (image == null && HasManagedMethods(context.ProcessId)) {
            //    var managedFunc = FindManagedMethodForIP(sample.IP, context.ProcessId);

            //    if (!managedFunc.IsUnknown) {
            //        image = managedFunc.Image;
            //    }
            //}
            
            //if (image != null) {
            //    if (!procImageW.TryGetValue(process, out var set)) {
            //        set = new Dictionary<ProfileImage, TimeSpan>();
            //        procImageW[process] = set;
            //    }

            //    set.AccumulateValue(image, sample.Weight);
            //}
        };
        
        foreach (var pair in processSamples) {
            var item = new TraceProcessSummary(pair.Key, pair.Value);
            
            item.WeightPercentage = 100 * (double)pair.Value / (double)samples_.Count;
            list.Add(item);

            if (procDuration.TryGetValue(pair.Key, out var duration)) {
                item.Duration = duration.Last - duration.First;
                Trace.WriteLine($"Proc {pair.Key.Name}, duration {item.Duration}");
            }

            //if (procImageW.TryGetValue(pair.Key, out var images)) {
            //    foreach (var imagePair in images) {
            //        item.ImageWeights.Add((imagePair.Key, imagePair.Value));
            //    }
                
            //    Trace.WriteLine($"Proc {pair.Key.Name}, {pair.Key.ProcessId}");

            //    foreach (var img in images) {
            //        Trace.WriteLine($"  {Utils.TryGetFileName(img.Key.FilePath)}, {img.Value.TotalMilliseconds} ms");
            //    }
            //}
        }

        return list;
    }

    //private HashSet<ProfileSample2> sampleSet_ = new HashSet<ProfileSample2>();
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
        }
    }

    public void PrintSamples(int processId) {
        foreach (var sample in samples_) {
            var context = sample.GetContext(this);
            if (context.ProcessId == processId) {
                Trace.WriteLine($"{sample}");
            }
        }


        //SampleHolder h = new SampleHolder() { samples = samples_ };

        //var d = StateSerializer.Serialize(h);
        //Trace.WriteLine($"Serialized to {d.Length}b");
        //Trace.Flush();
        //File.WriteAllBytes(@"C:\test\samples.dat", d);

        // foreach (var s in samples_) {
        //     var s2 = new ProfileSample2() {
        //         IP = s.IP,
        //         StackId = s.StackId,
        //         ContextId = s.ContextId,
        //         IsKernelCode = s.IsKernelCode
        //     };
        // 
        //     if (!sampleSet_.TryGetValue(s2, out var es2)) {
        //         sampleSet_.Add(s2);
        //         es2 = s2;
        //     }
        // 
        //     es2.Weight += s.Weight;
        //     es2.Time.Add(s.Time);
        // }
        // 
        // List<int> counts = sampleSet_.Select(s => s.Time.Count).ToList();
        // int max = sampleSet_.Max(s => s.Time.Count);
        // counts.Sort();
        // int med = counts[counts.Count / 2];
        // 
        // int memUsage = samples_.Count * Unsafe.SizeOf<ProfileSample>();
        // int memUsage2 = sampleSet_.Count * Unsafe.SizeOf<ProfileSample>() +
        //                 sampleSet_.Select(s => s.Time.Count * 8).Sum();
        // int memUsage3 = sampleSet_.Count * Unsafe.SizeOf<ProfileSample>() +
        //                 sampleSet_.Select(s => s.Time.Capacity * 8).Sum();
        // 
        // Trace.WriteLine($"Total samples: {samples_.Count}");
        // Trace.WriteLine($"    same IP: {sampleSet_.Count}, {100*(double)sampleSet_.Count / samples_.Count}%, median {med}, max {max}");
        // Trace.WriteLine($"    memUsage:           {memUsage}, {(double)memUsage / (1024 * 1024)} MB");
        // Trace.WriteLine($"    memUsage2 size:     {memUsage2}, {(double)memUsage2 / (1024 * 1024)} MB");
        // Trace.WriteLine($"    memUsage2 capacity: {memUsage3}, {(double)memUsage3 / (1024 * 1024)} MB");

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
public struct ManagedMethodMapping : IComparable<ManagedMethodMapping>, IComparable<long>, IEquatable<ManagedMethodMapping> {
    public ManagedMethodMapping(DebugFunctionInfo debugInfo, ProfileImage image, long ip, int size) {
        DebugInfo = debugInfo;
        Image = image;
        IP = ip;
        Size = size;
    }

    [ProtoMember(1)]
    public DebugFunctionInfo DebugInfo { get; }
    [ProtoMember(2)]
    public ProfileImage Image { get; }
    [ProtoMember(3)]
    public long IP { get; }
    [ProtoMember(4)]
    public int Size { get; }

    public bool IsUnknown => DebugInfo == null;
    public static ManagedMethodMapping Unknown => new ManagedMethodMapping(null, null, 0, 0);

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

    public static bool operator ==(ManagedMethodMapping left, ManagedMethodMapping right) {
        return left.Equals(right);
    }

    public static bool operator !=(ManagedMethodMapping left, ManagedMethodMapping right) {
        return !left.Equals(right);
    }
}

public class ManagedData {
    public Dictionary<ProfileImage, DotNetDebugInfoProvider> imageDebugInfo_;
    public List<ManagedMethodMapping> managedMethods_;
    public Dictionary<long, ManagedMethodMapping> managedMethodIdMap_;

    public ManagedData() {
        imageDebugInfo_ = new Dictionary<ProfileImage, DotNetDebugInfoProvider>();
        managedMethods_ = new List<ManagedMethodMapping>();
        managedMethodIdMap_ = new Dictionary<long, ManagedMethodMapping>();
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
