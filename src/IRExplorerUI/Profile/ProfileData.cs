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

[ProtoContract(SkipConstructor = true)]
public class FunctionProfileData {
    [ProtoMember(1)]
    public string SourceFilePath { get; set; }
    [ProtoMember(2)]
    public TimeSpan Weight { get; set; }
    [ProtoMember(3)]
    public TimeSpan ExclusiveWeight { get; set; }
    [ProtoMember(4)]
    public Dictionary<int, TimeSpan> SourceLineWeight { get; set; } // Line number mapping
    [ProtoMember(5)]
    public Dictionary<long, TimeSpan> InstructionWeight { get; set; } // Instr. offset mapping
    [ProtoMember(6)]
    public Dictionary<long, TimeSpan> BlockWeight { get; set; } //? TODO: Unused
    [ProtoMember(7)]
    public Dictionary<(Guid, int), TimeSpan> CalleesWeights { get; set; } // {Summary,Function ID} mapping
    [ProtoMember(8)]
    public Dictionary<(Guid, int), TimeSpan> CallerWeights { get; set; } // {Summary,Function ID} mapping
    [ProtoMember(9)]
    public Dictionary<long, PerformanceCounterSet> InstructionCounters { get; set; }

    public DebugFunctionInfo DebugInfo { get; set; }

    public bool HasSourceLines => SourceLineWeight != null && SourceLineWeight.Count > 0;
    public bool HasPerformanceCounters => InstructionCounters.Count > 0;
    public bool HasCallers => CallerWeights != null && CallerWeights.Count > 0;
    public bool HasCallees => CalleesWeights != null && CalleesWeights.Count > 0;
    public List<(int LineNumber, TimeSpan Weight)> SourceLineWeightList => SourceLineWeight.ToKeyValueList();

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
            CounterElements = new List<Tuple<IRElement, PerformanceCounterSet>>(capacity);
            FunctionCounters = new PerformanceCounterSet();
        }

        public double ScaleCounterValue(long value, PerformanceCounterInfo counter) {
            var total = FunctionCounters.FindCounterValue(counter);
            return total > 0 ? (double)value / (double)total : 0;
        }
    }

    //? TODO: Module ID referencing ProfileData

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
        SourceLineWeight ??= new Dictionary<int, TimeSpan>();
        InstructionWeight ??= new Dictionary<long, TimeSpan>();
        BlockWeight ??= new Dictionary<long, TimeSpan>();
        CalleesWeights ??= new Dictionary<(Guid, int), TimeSpan>();
        CallerWeights ??= new Dictionary<(Guid, int), TimeSpan>();
        InstructionCounters ??= new Dictionary<long, PerformanceCounterSet>();
    }

    public void AddLineSample(int sourceLine, TimeSpan weight) {
        if (SourceLineWeight.TryGetValue(sourceLine, out var currentWeight)) {
            SourceLineWeight[sourceLine] = currentWeight + weight;
        }
        else {
            SourceLineWeight[sourceLine] = weight;
        }
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
        result.BlockSampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        result.SampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return result;
    }

    public void ProcessSourceLines(IDebugInfoProvider debugInfo) {
        if (HasSourceLines) {
            return;
        }

        SourceLineWeight ??= new Dictionary<int, TimeSpan>();
        var funcInfo = debugInfo.FindFunctionByRVA(DebugInfo.RVA);

        foreach (var pair in InstructionWeight) {
            long rva = pair.Key + funcInfo.RVA;
            var lineInfo = debugInfo.FindSourceLineByRVA(rva);

            if (!lineInfo.IsUnknown) {
                SourceLineWeight.AccumulateValue(lineInfo.Line, pair.Value);
            }
        }
    }

    public PerformanceCounterSet ComputeFunctionCounters() {
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
        perfCounter.Number = PerformanceCounters.Count;
        PerformanceCounters[perfCounter.Id] = perfCounter;
    }

    public PerformanceCounterInfo GetPerformanceCounter(int id) {
        if (PerformanceCounters.TryGetValue(id, out var counter)) {
            return counter;
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

        return profileData;
    }

    public List<Tuple<IRTextFunction, FunctionProfileData>> GetSortedFunctions() {
        var list = FunctionProfiles.ToList();
        list.Sort((a, b) => -a.Item2.ExclusiveWeight.CompareTo(b.Item2.ExclusiveWeight));
        return list;
    }
}