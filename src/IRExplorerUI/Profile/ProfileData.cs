using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using IRExplorerCore;
using ProtoBuf;

namespace IRExplorerUI.Profile;

public class ProfileData {
    [ProtoContract(SkipConstructor = true)]
    public class ProfileDataState {
        [ProtoMember(1)]
        public TimeSpan ProfileWeight { get; set; }
            
        [ProtoMember(2)]
        public TimeSpan TotalWeight { get; set; }

        [ProtoMember(3)]
        public Dictionary<IRTextFunctionId, FunctionProfileData> FunctionProfiles { get; set; }

        [ProtoMember(4)]
        public Dictionary<int, PerformanceCounterInfo> PerformanceCounters { get; set; }
            
        [ProtoMember(5)]
        public Dictionary<string, TimeSpan> ModuleWeights { get; set; }

        [ProtoMember(6)]
        public ProfileDataReport Report { get; set; }

        [ProtoMember(7)]
        public byte[] CallTreeState;

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
    public Dictionary<string, PerformanceCounterSet> ModuleCounters { get; set; }
    public Dictionary<int, PerformanceCounterInfo> PerformanceCounters { get; set; }
    public ProfileCallTree CallTree { get; set; }
    public ProfileDataReport Report { get; set; }

    public List<PerformanceCounterInfo> SortedPerformanceCounters {
        get {
            var list = PerformanceCounters.ToValueList();
            list.Sort((a, b) => b.Id.CompareTo(a.Id));
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
        PerformanceCounters = new Dictionary<int, PerformanceCounterInfo>();
        ModuleCounters = new Dictionary<string, PerformanceCounterSet>();
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
            counterSet = new PerformanceCounterSet();
            ModuleCounters[moduleName] = counterSet;
        }
            
        counterSet.AddCounterSample(perfCounterId, value);
    }

    public void RegisterPerformanceCounter(PerformanceCounterInfo perfCounter) {
        perfCounter.Index = PerformanceCounters.Count;
        PerformanceCounters[perfCounter.Id] = perfCounter;
    }

    public PerformanceCounterInfo GetPerformanceCounter(int id) {
        if (PerformanceCounters.TryGetValue(id, out var counter)) {
            return counter;
        }

        return null;
    }

    public PerformanceCounterInfo FindPerformanceCounter(string name) {
        foreach (var pair in PerformanceCounters) {
            if (pair.Value.Name == name) {
                return pair.Value;
            }
        }

        return null;
    }

    public PerformanceMetricInfo RegisterPerformanceMetric(int id, PerformanceMetricConfig config) {
        var baseCounter = FindPerformanceCounter(config.BaseCounterName);
        var relativeCounter = FindPerformanceCounter(config.RelativeCounterName);

        if (baseCounter != null && relativeCounter != null) {
            var metric = new PerformanceMetricInfo(id, config, baseCounter, relativeCounter);
            PerformanceCounters[id] = metric;
            return metric;
        }

        return null;
    }
        
    public double ScaleFunctionWeight(TimeSpan weight) {
        return ProfileWeight.Ticks == 0 ? 0 :
            (double)weight.Ticks / (double)ProfileWeight.Ticks;
    }

    public double ScaleModuleWeight(TimeSpan weight) {
        return TotalWeight.Ticks == 0 ? 0 :
            (double)weight.Ticks / (double)TotalWeight.Ticks;
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

    public FunctionProfileData GetOrCreateFunctionProfile(IRTextFunction function, string sourceFile) {
        if (!FunctionProfiles.TryGetValue(function, out var profile)) {
            profile = new FunctionProfileData(sourceFile);
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

        foreach(var pair in state.FunctionProfiles) {
            var summary = summaryMap[pair.Key.SummaryId];
            var function = summary.GetFunctionWithId(pair.Key.FunctionNumber);

            if (function == null) {
                Trace.TraceWarning($"No func for {pair.Value.SourceFilePath}");
                continue;
            }

            profileData.FunctionProfiles[function] = pair.Value;
        }

        profileData.Report = state.Report;
        profileData.CallTree = ProfileCallTree.Deserialize(state.CallTreeState, summaryMap);
        return profileData;
    }

    public List<Tuple<IRTextFunction, FunctionProfileData>> GetSortedFunctions() {
        var list = FunctionProfiles.ToList();
        list.Sort((a, b) => -a.Item2.ExclusiveWeight.CompareTo(b.Item2.ExclusiveWeight));
        return list;
    }
}

[ProtoContract(SkipConstructor = true)]
public struct IRTextFunctionId : IEquatable<IRTextFunctionId> {
    [ProtoMember(1)]
    public Guid SummaryId { get; set; }
    [ProtoMember(2)]
    public int FunctionNumber { get; set; }

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
}

[ProtoContract(SkipConstructor = true)]
public class IRTextFunctionReference {
    [ProtoMember(1)]
    public IRTextFunctionId Id;
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

[ProtoContract(SkipConstructor = true)]
[ProtoInclude(100, typeof(PerformanceMetricInfo))]
public class PerformanceCounterInfo {
    [ProtoMember(1)]
    public PerformanceCounterConfig Config { get;set; }
    [ProtoMember(2)]
    public int Index { get; set; }
    [ProtoMember(3)]
    public int Id { get; set; }
    [ProtoMember(4)]
    public string Name { get; set; }
    [ProtoMember(5)]
    public int Frequency { get; set; }

    public virtual bool IsMetric => false;

    public PerformanceCounterInfo() {

    }

    public PerformanceCounterInfo(int id, string name, int frequency = 0) {
        Id = id;
        Name = name;
        Frequency = frequency;
    }
}

[ProtoContract(SkipConstructor = true)]
public class PerformanceMetricInfo : PerformanceCounterInfo {
    [ProtoMember(1)]
    public PerformanceMetricConfig Config { get; set; }
    [ProtoMember(2)]
    public PerformanceCounterInfo BaseCounter { get; set; }
    [ProtoMember(3)]
    public PerformanceCounterInfo RelativeCounter { get; set; }

    public override bool IsMetric => true;

    public PerformanceMetricInfo(int id, PerformanceMetricConfig config,
                                 PerformanceCounterInfo baseCounter, 
                                 PerformanceCounterInfo relativeCounter) : base(id, config.Name) {
        Config = config;
        BaseCounter = baseCounter;
        RelativeCounter = relativeCounter;
    }

    public double ComputeMetric(PerformanceCounterSet counterSet, out long baseValue, out long relativeValue) {
        baseValue = counterSet.FindCounterValue(BaseCounter);
        relativeValue = counterSet.FindCounterValue(RelativeCounter);

        if (baseValue == 0) {
            return 0;
        }

        // Counters may not be accurate and the percentage can end up more than 100%.
        double result = (double)relativeValue / (double)baseValue;
        return Config.IsPercentage ? Math.Min (result, 1) : result;
    }
}

// https://devblogs.microsoft.com/premier-developer/performance-traps-of-ref-locals-and-ref-returns-in-c/
[ProtoContract(SkipConstructor = true)]
public class PerformanceCounterValue : IEquatable<PerformanceCounterValue> {
    [ProtoMember(1)]
    public int CounterId { get; set; }
    [ProtoMember(2)]
    public long Value { get; set; }

    public PerformanceCounterValue(int counterId, long value = 0) {
        CounterId = counterId;
        Value = value;
    }

    public bool Equals(PerformanceCounterValue other) {
        return CounterId == other.CounterId && Value == other.Value;
    }

    public override bool Equals(object obj) {
        return obj is PerformanceCounterValue other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(CounterId, Value);
    }
}

// Groups a set of counters associated with a single instruction.
// There is one PerformanceCounterValue for each counter type
// that accumulates all instances of the raw events.
[ProtoContract(SkipConstructor = true)]
public class PerformanceCounterSet {
    //? Use smth like https://github.com/faustodavid/ListPool/blob/main/src/ListPool/ValueListPool.cs
    //? and make PerformanceCounterSet as struct.
    [ProtoMember(1)]
    public List<PerformanceCounterValue> Counters { get; set; }

    public int Count => Counters.Count;

    public PerformanceCounterSet() {
        InitializeReferenceMembers();
    }

    [ProtoAfterDeserialization]
    private void InitializeReferenceMembers() {
        Counters ??= new List<PerformanceCounterValue>();
    }

    public void AddCounterSample(int perfCounterId, long value) {
        PerformanceCounterValue counter;
        var index = Counters.FindIndex((item) => item.CounterId == perfCounterId);

        if (index != -1) {
            counter = Counters[index];
        }
        else {
            // Keep the list sorted so that it is in sync
            // with the sorted counter definition list.
            counter = new PerformanceCounterValue(perfCounterId);
            int insertionIndex = 0;

            for (int i = 0; i < Counters.Count; i++, insertionIndex++) {
                if (Counters[i].CounterId >= perfCounterId) {
                    break;
                }
            }

            Counters.Insert(insertionIndex, counter);
        }

        counter.Value += value;
    }

    public long FindCounterValue(int perfCounterId) {
        var index = Counters.FindIndex((item) => item.CounterId == perfCounterId);
        return index != -1 ? Counters[index].Value : 0;
    }

    public long FindCounterValue(PerformanceCounterInfo counter) {
        return FindCounterValue(counter.Id);
    }

    public void Add(PerformanceCounterSet other) {
        //? TODO: This assumes there are not many counters being collected,
        //? switch to dict if dozens get to be collected one day.
        foreach (var counter in other.Counters) {
            var index = Counters.FindIndex((item) => item.CounterId == counter.CounterId);

            if (index != -1) {
                var countersSpan = CollectionsMarshal.AsSpan(Counters);
                ref var counterRef = ref countersSpan[index];
                counterRef.Value += counter.Value;
            }
            else {
                Counters.Add(new PerformanceCounterValue(counter.CounterId, counter.Value));
            }
        }
    }

    public long this[int perfCounterId] => FindCounterValue(perfCounterId);
}

public static class PerformanceCounterExtensions {
    public static PerformanceCounterSet AccumulateValue<K>(this Dictionary<K, PerformanceCounterSet> dict, K key, PerformanceCounterSet value) {
        if (!dict.TryGetValue(key, out var currentValue)) {
            currentValue = new PerformanceCounterSet();
            dict[key] = currentValue;
        }

        currentValue.Add(value);
        return currentValue;
    }
}
