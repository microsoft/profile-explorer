using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using ProtoBuf;
using IRExplorerUI;
using System.Threading.Tasks;
using IRExplorerUI.Compilers;

namespace IRExplorerUI.Profile;

public class ProfileData {
    [ProtoContract(SkipConstructor = true)]
    public class ProfileDataState {
        [ProtoMember(1)] public TimeSpan ProfileWeight { get; set; }

        [ProtoMember(2)] public TimeSpan TotalWeight { get; set; }

        [ProtoMember(3)] public Dictionary<IRTextFunctionId, FunctionProfileData> FunctionProfiles { get; set; }

        [ProtoMember(4)] public Dictionary<int, PerformanceCounter> PerformanceCounters { get; set; }

        [ProtoMember(5)] public Dictionary<string, TimeSpan> ModuleWeights { get; set; }

        [ProtoMember(6)] public ProfileDataReport Report { get; set; }

        [ProtoMember(7)] public byte[] CallTreeState; //? TODO: Reimplement, super slow!

        [ProtoMember(8)] public List<(ProfileSample Sample, ResolvedProfileStack Stack)> Samples { get; set; }
        [ProtoMember(9)] public ProfileProcess Process { get; set; }
        [ProtoMember(10)]
        public Dictionary<int, ProfileThread> Threads { get; set; }

        public ProfileDataState(TimeSpan profileWeight, TimeSpan totalWeight) {
            ProfileWeight = profileWeight;
            TotalWeight = totalWeight;
            FunctionProfiles = new Dictionary<IRTextFunctionId, FunctionProfileData>();
        }
    }

    public TimeSpan ProfileWeight { get; set; }
    public TimeSpan TotalWeight { get; set; }
    public ConcurrentDictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; set; }
    public Dictionary<string, TimeSpan> ModuleWeights { get; set; }
    public Dictionary<string, PerformanceCounterValueSet> ModuleCounters { get; set; }
    public Dictionary<int, PerformanceCounter> PerformanceCounters { get; set; }
    public ProfileCallTree CallTree { get; set; }
    public ProfileDataReport Report { get; set; }

    public List<(ProfileSample Sample, ResolvedProfileStack Stack)> Samples { get; set; }
    public List<(PerformanceCounterEvent Sample, ResolvedProfileStack Stack)> Events { get; set; }
    public ProfileProcess Process { get; set; }
    public Dictionary<int, ProfileThread> Threads { get; set; }

    [ProtoContract(SkipConstructor = true)]
    class SampleStore {
        [ProtoMember(1)] public List<(ProfileSample Sample, ResolvedProfileStack Stack)> Samples { get; set; }
    }

    public void SaveSamples(string path) {
        int parts = 2;

        while (true) {
            try {
                var newlist = new List<(ProfileSample Sample, ResolvedProfileStack Stack)>();
                for (int i = 0; i < Samples.Count / parts; i++) {
                    newlist.Add(Samples[i]);
                }

                var state = new SampleStore() { Samples = newlist };
                var data = StateSerializer.Serialize(state);
                File.WriteAllBytes(path, data);
                Trace.WriteLine($"Done with {parts}: {data.Length}");
                return;
            }
            catch (Exception ex) {
                Trace.WriteLine($"Failed with {parts}");
                parts++;
            }
        }
    }

    public static ProfileData LoadSamples(string path) {
        var state = StateSerializer.Deserialize<SampleStore>(path);
        return new ProfileData() { Samples = state.Samples };
    }  
    
    public List<PerformanceCounter> SortedPerformanceCounters {
        get {
            var list = PerformanceCounters.ToValueList();
            list.Sort((a, b) => b.Id.CompareTo(a.Id));
            return list;
        }
    }

    public List<(int ThreadId, TimeSpan Weight)> SortedThreadWeights {
        get {
            var list = new List<(int ThreadId, TimeSpan Weight)>();
            var threadWeights = new Dictionary<int, TimeSpan>();

            foreach (var (sample, stack) in Samples) {
                threadWeights.AccumulateValue(stack.Context.ThreadId, sample.Weight);
            }

            foreach (var (threadId, weight) in threadWeights) {
                list.Add((threadId, weight));
            }

            list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            return list;
        }
    }

    public ProfileData(TimeSpan profileWeight, TimeSpan totalWeight) : this() {
        ProfileWeight = profileWeight;
        TotalWeight = totalWeight;
    }

    public ProfileData() {
        ProfileWeight = TimeSpan.Zero;
        FunctionProfiles = new ConcurrentDictionary<IRTextFunction, FunctionProfileData>();
        ModuleWeights = new Dictionary<string, TimeSpan>();
        PerformanceCounters = new Dictionary<int, PerformanceCounter>();
        ModuleCounters = new Dictionary<string, PerformanceCounterValueSet>();
        Threads = new Dictionary<int, ProfileThread>();
        Samples = new List<(ProfileSample, ResolvedProfileStack)>();
        Events = new List<(PerformanceCounterEvent Sample, ResolvedProfileStack Stack)>();
    }

    public void AddModuleSample(string moduleName, TimeSpan weight) {
        if (ModuleWeights.TryGetValue(moduleName, out var currentWeight)) {
            ModuleWeights[moduleName] = currentWeight + weight;
        }
        else {
            ModuleWeights[moduleName] = weight;
        }
    }

    public void AddModuleCounter(string moduleName, int perfCounterId, long value) {
        if (!ModuleCounters.TryGetValue(moduleName, out var counterSet)) {
            counterSet = new PerformanceCounterValueSet();
            ModuleCounters[moduleName] = counterSet;
        }

        counterSet.AddCounterSample(perfCounterId, value);
    }

    public void RegisterPerformanceCounter(PerformanceCounter perfCounter) {
        perfCounter.Index = PerformanceCounters.Count;
        PerformanceCounters[perfCounter.Id] = perfCounter;
    }

    public PerformanceCounter GetPerformanceCounter(int id) {
        if (PerformanceCounters.TryGetValue(id, out var counter)) {
            return counter;
        }

        return null;
    }

    public PerformanceCounter FindPerformanceCounter(string name) {
        foreach (var pair in PerformanceCounters) {
            if (pair.Value.Name == name) {
                return pair.Value;
            }
        }

        return null;
    }

    public PerformanceMetric RegisterPerformanceMetric(int id, PerformanceMetricConfig config) {
        var baseCounter = FindPerformanceCounter(config.BaseCounterName);
        var relativeCounter = FindPerformanceCounter(config.RelativeCounterName);

        if (baseCounter != null && relativeCounter != null) {
            var metric = new PerformanceMetric(id, config, baseCounter, relativeCounter);
            PerformanceCounters[id] = metric;
            return metric;
        }

        return null;
    }

    public double ScaleFunctionWeight(TimeSpan weight) {
        return ProfileWeight.Ticks == 0 ? 0 : (double)weight.Ticks / (double)ProfileWeight.Ticks;
    }

    public double ScaleModuleWeight(TimeSpan weight) {
        return TotalWeight.Ticks == 0 ? 0 : (double)weight.Ticks / (double)TotalWeight.Ticks;
    }

    public FunctionProfileData GetFunctionProfile(IRTextFunction function) {
        if (FunctionProfiles.TryGetValue(function, out var profile)) {
            return profile;
        }

        return null;
    }

    public bool HasFunctionProfile(IRTextFunction function) {
        return GetFunctionProfile(function) != null;
    }

    public FunctionProfileData GetOrCreateFunctionProfile(IRTextFunction function, 
                                                          FunctionDebugInfo debugInfo) {
        if (!FunctionProfiles.TryGetValue(function, out var profile)) {
            profile = new FunctionProfileData() { FunctionDebugInfo = debugInfo };
            FunctionProfiles.TryAdd(function, profile);
        }

        return profile;
    }

    public byte[] Serialize() {
        var state = new ProfileDataState(ProfileWeight, TotalWeight);
        state.PerformanceCounters = PerformanceCounters;
        state.ModuleWeights = ModuleWeights;
        state.Report = Report;
        state.CallTreeState = CallTree.Serialize();
        state.Samples = Samples;
        state.Process = Process;
        state.Threads = Threads;

        foreach (var pair in FunctionProfiles) {
            var funcId = new IRTextFunctionId(pair.Key);
            state.FunctionProfiles[funcId] = pair.Value;
        }

        return StateSerializer.Serialize(state);
    }

    public static ProfileData Deserialize(byte[] data, List<IRTextSummary> summaries) {
        var state = StateSerializer.Deserialize<ProfileDataState>(data);
        var profileData = new ProfileData(state.ProfileWeight, state.TotalWeight);
        profileData.PerformanceCounters = state.PerformanceCounters;
        profileData.ModuleWeights = state.ModuleWeights;

        var summaryMap = new Dictionary<Guid, IRTextSummary>();

        foreach (var summary in summaries) {
            summaryMap[summary.Id] = summary;
        }

        foreach (var pair in state.FunctionProfiles) {
            var summary = summaryMap[pair.Key.SummaryId];
            var function = summary.GetFunctionWithId(pair.Key.FunctionNumber);

            if (function == null) {
                Trace.TraceWarning($"No func for {pair.Value.SourceFilePath}");
                continue;
            }

            profileData.FunctionProfiles[function] = pair.Value;
        }

        profileData.Process = state.Process;
        profileData.Threads = state.Threads;
        profileData.Report = state.Report;
        profileData.CallTree = ProfileCallTree.Deserialize(state.CallTreeState, summaryMap);
        DeserializeSamples(profileData, state, summaryMap);
        return profileData;
    }

    private static void DeserializeSamples(ProfileData profileData, ProfileDataState state, Dictionary<Guid, IRTextSummary> summaryMap) {
        if (state.Samples == null) {
            return;
        }

        profileData.Samples = state.Samples;

        foreach (var pair in profileData.Samples) {
            foreach (var frame in pair.Stack.StackFrames) {
                if (frame.Info.Function == null) {
                    continue; // Unknown frame.
                }

                if (!summaryMap.ContainsKey(frame.Info.Function.Id.SummaryId)) {
                    continue;
                }

                var summary = summaryMap[frame.Info.Function.Id.SummaryId];
                var function = summary.GetFunctionWithId(frame.Info.Function.Id.FunctionNumber);

                if (function == null) {
                    Debug.Assert(false, "Could not find node for func");
                    continue;
                }

                frame.Info.Function = function;
            }
        }
    }

    public List<(IRTextFunction, FunctionProfileData)> GetSortedFunctions() {
        var list = FunctionProfiles.ToList();
        list.Sort((a, b) => -a.Item2.ExclusiveWeight.CompareTo(b.Item2.ExclusiveWeight));
        return list;
    }

    public void AddThreads(IEnumerable<ProfileThread> threads) {
        foreach (var thread in threads) {
            Threads[thread.ThreadId] = thread;
        }
    }

    public ProfileThread FindThread(int threadId) {
        if (Threads != null) {
            return Threads.GetValueOrNull(threadId);
        }
        return null;
    }

    public void FilterFunctionProfile(ProfileSampleFilter filter) {
        ModuleWeights.Clear();
        ProfileWeight = TimeSpan.Zero;
        TotalWeight = TimeSpan.Zero;
        FunctionProfiles.Clear();
        CallTree = null; //? Recycle nodes

        //? TODO: Split ProfileData into a part that has the samples and other info that doesn't change,
        //? while the rest is more like a processing result similar to FuncProfileData
        var profile = ComputeFunctionProfile(this, filter);
        ModuleWeights = profile.ModuleWeights;
        ProfileWeight = profile.ProfileWeight;
        TotalWeight = profile.TotalWeight;
        FunctionProfiles = profile.FunctionProfiles;
        CallTree = profile.CallTree;
    }

    public ProfileData ComputeFunctionProfile(ProfileData baseProfile, ProfileSampleFilter filter, int maxChunks = int.MaxValue) {
        var profile = new ProfileData();
        ProfileCallTree callTree = new();

        int sampleStartIndex = filter.TimeRange?.StartSampleIndex ?? 0;
        int sampleEndIndex = filter.TimeRange?.EndSampleIndex ?? baseProfile.Samples.Count;

        int sampleCount = sampleEndIndex - sampleStartIndex;
        int chunks = Math.Min(maxChunks, Math.Min(8, (Environment.ProcessorCount * 3) / 4));

        int chunkSize = sampleCount / chunks;
        var tasks = new List<Task>();
        var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, chunks);
        var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);

        for (int k = 0; k < chunks; k++) {
            int start = Math.Min(sampleStartIndex + k * chunkSize, sampleEndIndex);
            int end = Math.Min(sampleStartIndex + (k + 1) * chunkSize, sampleEndIndex);

            tasks.Add(taskFactory.StartNew(() => {
                HashSet<int> stackModules = new();
                HashSet<IRTextFunction> stackFuncts = new();

                for (int i = start; i < end; i++) {
                    var (sample, stack) = baseProfile.Samples[i];

                    if (filter.ThreadIds != null &&
                        !filter.ThreadIds.Contains(stack.Context.ThreadId)) {
                        continue;
                    }

                    //? TODO: Use same Interlocked trick from CallTreeNode to avoid lock
                    lock (profile) {
                        profile.TotalWeight += sample.Weight;
                        profile.ProfileWeight += sample.Weight;
                    }

                    bool isTopFrame = true;
                    stackModules.Clear();
                    stackFuncts.Clear();

                    foreach (var resolvedFrame in stack.StackFrames) {
                        if (resolvedFrame.IsUnknown) {
                            continue;
                        }

                        if (isTopFrame && stackModules.Add(resolvedFrame.Info.Image.Id)) {
                            //? TODO: Avoid lock by summing per thread, accumulate at the end
                            //? TODO: Also, don't use mod name as key, use imageId
                            lock (profile) {
                                profile.AddModuleSample(resolvedFrame.Info.Image.ModuleName, sample.Weight);
                            }
                        }

                        var funcRva = resolvedFrame.Info.DebugInfo.RVA;
                        var frameRva = resolvedFrame.FrameRVA;
                        var textFunction = resolvedFrame.Info.Function;
                        var funcProfile = profile.GetOrCreateFunctionProfile(resolvedFrame.Info.Function, resolvedFrame.Info.DebugInfo);
                        
                        //? TODO: Info.Profile ends up being the func profile in the previous run
                        //? resolvedFrame.Info.Profile = funcProfile;

                        lock (funcProfile) {
                            var offset = frameRva - funcRva;

                            // Don't count the inclusive time for recursive functions multiple times.
                            if (stackFuncts.Add(textFunction)) {
                                funcProfile.AddInstructionSample(offset, sample.Weight);
                                funcProfile.Weight += sample.Weight;
                                funcProfile.SampleStartIndex = Math.Min(funcProfile.SampleStartIndex, i);
                                funcProfile.SampleEndIndex = Math.Max(funcProfile.SampleEndIndex, i);
                            }

                            // Count the exclusive time for the top frame function.
                            if (isTopFrame) {
                                funcProfile.ExclusiveWeight += sample.Weight;
                            }
                        }

                        isTopFrame = false;
                    }

                    callTree.UpdateCallTree(sample, stack);
                }
            }));

        }

        Task.WhenAll(tasks.ToArray()).Wait();
        profile.CallTree = callTree;
        return profile;
    }
}


[ProtoContract(SkipConstructor = true)]
public struct IRTextFunctionId : IEquatable<IRTextFunctionId> {
    [ProtoMember(1)] public Guid SummaryId { get; set; }
    [ProtoMember(2)] public int FunctionNumber { get; set; }

    public IRTextFunctionId(Guid summaryId, int funcNumber) {
        SummaryId = summaryId;
        FunctionNumber = funcNumber;
    }

    public IRTextFunctionId(IRTextFunction func) {
        SummaryId = func.ParentSummary.Id;
        FunctionNumber = func.Number;
    }

    public bool Equals(IRTextFunctionId other) {
        return FunctionNumber == other.FunctionNumber && SummaryId.Equals(other.SummaryId);
    }

    public override bool Equals(object obj) {
        return obj is IRTextFunctionId other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(SummaryId, FunctionNumber);
    }

    public override string ToString() {
        return $"{FunctionNumber}@{SummaryId}";
    }

    public static bool operator ==(IRTextFunctionId left, IRTextFunctionId right) {
        return left.Equals(right);
    }

    public static bool operator !=(IRTextFunctionId left, IRTextFunctionId right) {
        return !left.Equals(right);
    }

    public static implicit operator IRTextFunctionId(IRTextFunction func) {
        return new IRTextFunctionId(func);
    }
}


[ProtoContract(SkipConstructor = true)]
public class IRTextFunctionReference {
    [ProtoMember(1)] public IRTextFunctionId Id;
    public IRTextFunction Value;

    public IRTextFunctionReference() {
    }

    public IRTextFunctionReference(IRTextFunction func) {
        Id = new IRTextFunctionId(func);
        Value = func;
    }

    public IRTextFunctionReference(IRTextFunctionId id, IRTextFunction func = null) {
        Id = id;
        Value = func;
    }

    public static implicit operator IRTextFunctionReference(IRTextFunction func) {
        return new IRTextFunctionReference(func);
    }

    public static implicit operator IRTextFunction(IRTextFunctionReference funcRef) {
        return funcRef.Value;
    }
}
