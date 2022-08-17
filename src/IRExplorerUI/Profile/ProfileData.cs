using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
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
        public Dictionary<(Guid summaryId, int funcNumber), FunctionProfileData> FunctionProfiles { get; set; }

        [ProtoMember(4)]
        public Dictionary<int, PerformanceCounterInfo> PerformanceCounters { get; set; }
            
        [ProtoMember(5)]
        public Dictionary<string, TimeSpan> ModuleWeights { get; set; }

        [ProtoMember(6)]
        public ProfileDataReport Report { get; set; }

        public ProfileDataState(TimeSpan profileWeight, TimeSpan totalWeight) {
            ProfileWeight = profileWeight;
            TotalWeight = totalWeight;
            FunctionProfiles = new Dictionary<(Guid summaryId, int funcNumber), FunctionProfileData>();
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
        return (double)weight.Ticks / (double)ProfileWeight.Ticks;
    }

    public double ScaleModuleWeight(TimeSpan weight) {
        return (double)weight.Ticks / (double)TotalWeight.Ticks;
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
        var profileState = new ProfileDataState(ProfileWeight, TotalWeight);
        profileState.PerformanceCounters = PerformanceCounters;
        profileState.ModuleWeights = ModuleWeights;
        profileState.Report = Report;

        foreach (var pair in FunctionProfiles) {
            var func = pair.Key;
            profileState.FunctionProfiles[(func.ParentSummary.Id, func.Number)] = pair.Value;
        }

        return StateSerializer.Serialize(profileState);
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
            var summary = summaryMap[pair.Key.summaryId];
            var function = summary.GetFunctionWithId(pair.Key.funcNumber);

            if (function == null) {
                Trace.TraceWarning($"No func for {pair.Value.SourceFilePath}");
                continue;
            }

            profileData.FunctionProfiles[function] = pair.Value;
        }

        profileData.CallTree ??= new ProfileCallTree();
        return profileData;
    }

    public List<Tuple<IRTextFunction, FunctionProfileData>> GetSortedFunctions() {
        var list = FunctionProfiles.ToList();
        list.Sort((a, b) => -a.Item2.ExclusiveWeight.CompareTo(b.Item2.ExclusiveWeight));
        return list;
    }
}

[ProtoContract(SkipConstructor = true)]
public class FunctionProfileData {
    [ProtoMember(1)]
    public string SourceFilePath { get; set; }
    [ProtoMember(2)]
    public TimeSpan Weight { get; set; }
    [ProtoMember(3)]
    public TimeSpan ExclusiveWeight { get; set; }
    [ProtoMember(4)]
    public Dictionary<long, TimeSpan> InstructionWeight { get; set; } // Instr. offset mapping
    [ProtoMember(5)]
    public Dictionary<(Guid, int), TimeSpan> CalleesWeights { get; set; } // {Summary,Function ID} mapping
    [ProtoMember(6)]
    public Dictionary<(Guid, int), TimeSpan> CallerWeights { get; set; } // {Summary,Function ID} mapping
    [ProtoMember(7)]
    public Dictionary<long, PerformanceCounterSet> InstructionCounters { get; set; }

    public DebugFunctionInfo DebugInfo { get; set; }

    public bool HasPerformanceCounters => InstructionCounters.Count > 0;
    public bool HasCallers => CallerWeights != null && CallerWeights.Count > 0;
    public bool HasCallees => CalleesWeights != null && CalleesWeights.Count > 0;

    public class ProcessingResult {
        public List<Tuple<IRElement, TimeSpan>> SampledElements { get; set; }
        public Dictionary<BlockIR, TimeSpan> BlockSampledElementsMap { get; set; }
        public List<Tuple<BlockIR, TimeSpan>> BlockSampledElements { get; set; }
        public List<Tuple<IRElement, PerformanceCounterSet>> CounterElements { get; set; }
        public List<Tuple<BlockIR, PerformanceCounterSet>> BlockCounterElements { get; set; }
        public PerformanceCounterSet FunctionCounters { get; set; }

        public ProcessingResult(int capacity = 0) {
            SampledElements = new List<Tuple<IRElement, TimeSpan>>(capacity);
            BlockSampledElementsMap = new Dictionary<BlockIR, TimeSpan>(capacity);
            BlockSampledElements = new List<Tuple<BlockIR, TimeSpan>>();
            CounterElements = new List<Tuple<IRElement, PerformanceCounterSet>>(capacity);
            FunctionCounters = new PerformanceCounterSet(); 
        }

        public double ScaleCounterValue(long value, PerformanceCounterInfo counter) {
            var total = FunctionCounters.FindCounterValue(counter);
            return total > 0 ? (double)value / (double)total : 0;
        }

        public void SortSampledElements() {
            BlockSampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            SampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        }
    }

    public class SourceLineProcessingResult {
        public Dictionary<int, TimeSpan> SourceLineWeight { get; set; } // Line number mapping
        public Dictionary<int, PerformanceCounterSet> SourceLineCounters { get; set; } // Line number mapping
        public PerformanceCounterSet FunctionCounters { get; set; }
        public List<(int LineNumber, TimeSpan Weight)> SourceLineWeightList => SourceLineWeight.ToKeyValueList();
        public int FirstLineIndex { get; set; }
        public int LastLineIndex { get; set; }

        public SourceLineProcessingResult() {
            SourceLineWeight = new Dictionary<int, TimeSpan>();
            SourceLineCounters = new Dictionary<int, PerformanceCounterSet>();
            FunctionCounters = new PerformanceCounterSet();
        }
    }

    //? TODO
    //? - save unique stacks with inclusive samples for each frame

    public FunctionProfileData(string filePath) {
        SourceFilePath = filePath;
        Weight = TimeSpan.Zero;
        InitializeReferenceMembers();
    }

    public void AddCounterSample(long instrOffset, int perfCounterId, long value) {
        var counterSet = InstructionCounters.GetOrAddValue(instrOffset);
        counterSet.AddCounterSample(perfCounterId, value);
    }

    [ProtoAfterDeserialization]
    private void InitializeReferenceMembers() {
        InstructionWeight ??= new Dictionary<long, TimeSpan>();
        CalleesWeights ??= new Dictionary<(Guid, int), TimeSpan>();
        CallerWeights ??= new Dictionary<(Guid, int), TimeSpan>();
        InstructionCounters ??= new Dictionary<long, PerformanceCounterSet>();
    }

    public void AddInstructionSample(long instrOffset, TimeSpan weight) {
        if (InstructionWeight.TryGetValue(instrOffset, out var currentWeight)) {
            InstructionWeight[instrOffset] = currentWeight + weight;
        }
        else {
            InstructionWeight[instrOffset] = weight;
        }
    }

    public void AddChildSample(IRTextFunction childFunc, TimeSpan weight) {
        lock (CalleesWeights) {
            var key = (childFunc.ParentSummary.Id, childFunc.Number);

            if (CalleesWeights.TryGetValue(key, out var currentWeight)) {
                CalleesWeights[key] = currentWeight + weight;
            }
            else {
                CalleesWeights[key] = weight;
            }
        }
    }

    public void AddCallerSample(IRTextFunction callerFunc, TimeSpan weight) {
        lock (CallerWeights) {
            var key = (callerFunc.ParentSummary.Id, callerFunc.Number);

            if (CallerWeights.TryGetValue(key, out var currentWeight)) {
                CallerWeights[key] = currentWeight + weight;
            }
            else {
                CallerWeights[key] = weight;
            }
        }
    }

    public double ScaleWeight(TimeSpan weight) {
        return (double)weight.Ticks / (double)Weight.Ticks;
    }

    public ProcessingResult Process(FunctionIR function, ICompilerIRInfo ir) {
        var metadataTag = function.GetTag<AssemblyMetadataTag>();
        bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

        if (!hasInstrOffsetMetadata) {
            return null;
        }

        var result = new ProcessingResult(metadataTag.OffsetToElementMap.Count);

        foreach (var pair in InstructionWeight) {
            if (TryFindElementForOffset(metadataTag, pair.Key, ir, out var element)) {
                result.SampledElements.Add(new Tuple<IRElement, TimeSpan>(element, pair.Value));
                result.BlockSampledElementsMap.AccumulateValue(element.ParentBlock, pair.Value);
            }
        }

        foreach (var pair in InstructionCounters) {
            if (TryFindElementForOffset(metadataTag, pair.Key, ir, out var element)) {
                result.CounterElements.Add(new Tuple<IRElement, PerformanceCounterSet>(element, pair.Value));
            }

            result.FunctionCounters.Add(pair.Value);
        }

        result.BlockSampledElements = result.BlockSampledElementsMap.ToList();
        result.SortSampledElements();
        return result;
    }

    public SourceLineProcessingResult ProcessSourceLines(IDebugInfoProvider debugInfo) {
        var result = new SourceLineProcessingResult();
        int firstLine = int.MaxValue;
        int lastLine = int.MinValue;

        foreach (var pair in InstructionWeight) {
            //? The profile offset is set on the instr. following the one intended,
            //? TryFindElementForOffset account for this but here the FunctionIR
            //? is not being used, subtract 1 to end in that range of the right instr.
            long rva = pair.Key + DebugInfo.RVA - 1;
            var lineInfo = debugInfo.FindSourceLineByRVA(rva);

            if (!lineInfo.IsUnknown) {
                result.SourceLineWeight.AccumulateValue(lineInfo.Line, pair.Value);
                firstLine = Math.Min(lineInfo.Line, firstLine);
                lastLine = Math.Max(lineInfo.Line, lastLine);
            }
        }

        foreach (var pair in InstructionCounters) {
            long rva = pair.Key + DebugInfo.RVA;
            var lineInfo = debugInfo.FindSourceLineByRVA(rva);

            if (!lineInfo.IsUnknown) {
                result.SourceLineCounters.AccumulateValue(lineInfo.Line, pair.Value);
            }

            result.FunctionCounters.Add(pair.Value);
        }

        result.FirstLineIndex = firstLine;
        result.LastLineIndex = lastLine;
        return result;
    }

    public PerformanceCounterSet ComputeFunctionTotalCounters() {
        var result = new PerformanceCounterSet();

        foreach (var pair in InstructionCounters) {
            result.Add(pair.Value);
        }

        return result;
    }

    private bool TryFindElementForOffset(AssemblyMetadataTag metadataTag, long offset,
        ICompilerIRInfo ir, out IRElement element) {
        int multiplier = 1;
        var offsetData = ir.InstructionOffsetData;

        do {
            if (metadataTag.OffsetToElementMap.TryGetValue(offset - multiplier * offsetData.OffsetAdjustIncrement, out element)) {
                return true;
            }
            ++multiplier;
        } while (multiplier * offsetData.OffsetAdjustIncrement < offsetData.MaxOffsetAdjust);

        return false;
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
