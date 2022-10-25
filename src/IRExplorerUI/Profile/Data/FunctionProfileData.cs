using System;
using System.Collections.Generic;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public class FunctionProfileData {
    [ProtoMember(1)]
    public string SourceFilePath { get; set; } //? TODO: Remove, unused
    [ProtoMember(2)]
    public TimeSpan Weight { get; set; }
    [ProtoMember(3)]
    public TimeSpan ExclusiveWeight { get; set; }
    [ProtoMember(4)]
    public Dictionary<long, TimeSpan> InstructionWeight { get; set; } // Instr. offset mapping
    [ProtoMember(5)]
    public Dictionary<IRTextFunctionId, TimeSpan> CalleesWeights { get; set; } // {Summary,Function ID} mapping
    [ProtoMember(6)]
    public Dictionary<IRTextFunctionId, TimeSpan> CallerWeights { get; set; } // {Summary,Function ID} mapping
    [ProtoMember(7)]
    public Dictionary<long, PerformanceCounterSet> InstructionCounters { get; set; }
    [ProtoMember(8)]
    public FunctionDebugInfo FunctionDebugInfo { get; set; }

    public bool HasPerformanceCounters => InstructionCounters.Count > 0;
    public bool HasCallers => CallerWeights != null && CallerWeights.Count > 0;
    public bool HasCallees => CalleesWeights != null && CalleesWeights.Count > 0;


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
        CalleesWeights ??= new Dictionary<IRTextFunctionId, TimeSpan>();
        CallerWeights ??= new Dictionary<IRTextFunctionId, TimeSpan>();
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

    //? TODO: Dead
    public void AddChildSample(IRTextFunction childFunc, TimeSpan weight) {
        lock (CalleesWeights) {
            var key = new IRTextFunctionId(childFunc);

            if (CalleesWeights.TryGetValue(key, out var currentWeight)) {
                CalleesWeights[key] = currentWeight + weight;
            }
            else {
                CalleesWeights[key] = weight;
            }
        }
    }

    //? TODO: Dead
    public void AddCallerSample(IRTextFunction callerFunc, TimeSpan weight) {
        lock (CallerWeights) {
            var key = new IRTextFunctionId(callerFunc);

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
            long rva = pair.Key + FunctionDebugInfo.RVA - 1;
            var lineInfo = debugInfo.FindSourceLineByRVA(rva);

            if (!lineInfo.IsUnknown) {
                result.SourceLineWeight.AccumulateValue(lineInfo.Line, pair.Value);
                firstLine = Math.Min(lineInfo.Line, firstLine);
                lastLine = Math.Max(lineInfo.Line, lastLine);
            }
        }

        foreach (var pair in InstructionCounters) {
            long rva = pair.Key + FunctionDebugInfo.RVA;
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

    public static bool TryFindElementForOffset(AssemblyMetadataTag metadataTag, long offset,
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

    public void Reset() {
        Weight = TimeSpan.Zero;
        ExclusiveWeight = TimeSpan.Zero;
        InstructionWeight?.Clear();
        CalleesWeights?.Clear();
        CallerWeights?.Clear();
        InstructionCounters?.Clear();
    }

    
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
}