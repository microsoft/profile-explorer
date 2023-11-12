// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public class FunctionProfileData {
  //? TODO
  //? - save unique stacks with inclusive samples for each frame

  public FunctionProfileData() {
    InitializeReferenceMembers();
  }

  [ProtoMember(1)]
  public string SourceFilePath { get; set; } //? TODO: Remove, unused
  [ProtoMember(2)]
  public TimeSpan Weight { get; set; }
  [ProtoMember(3)]
  public TimeSpan ExclusiveWeight { get; set; }
  [ProtoMember(4)]
  public Dictionary<long, TimeSpan> InstructionWeight { get; set; } // Instr. offset mapping
  [ProtoMember(5)]
  public Dictionary<long, PerformanceCounterValueSet> InstructionCounters { get; set; }
  [ProtoMember(8)]
  public FunctionDebugInfo FunctionDebugInfo { get; set; }
  public int SampleStartIndex { get; set; }
  public int SampleEndIndex { get; set; }
  public bool HasPerformanceCounters => InstructionCounters.Count > 0;

  public static bool TryFindElementForOffset(AssemblyMetadataTag metadataTag, long offset,
                                             ICompilerIRInfo ir, out IRElement element) {
    int multiplier = 0;
    var offsetData = ir.InstructionOffsetData;

    do {
      if (metadataTag.OffsetToElementMap.TryGetValue(offset - multiplier * offsetData.OffsetAdjustIncrement,
                                                     out element)) {
        return true;
      }

      ++multiplier;
    } while (multiplier * offsetData.OffsetAdjustIncrement < offsetData.MaxOffsetAdjust);

    return false;
  }

  public void AddCounterSample(long instrOffset, int perfCounterId, long value) {
    var counterSet = InstructionCounters.GetOrAddValue(instrOffset);
    counterSet.AddCounterSample(perfCounterId, value);
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

  public double ScaleWeight(TimeSpan weight) {
    return weight.Ticks / (double)Weight.Ticks;
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
        result.SampledElements.Add((element, pair.Value));
        result.BlockSampledElementsMap.AccumulateValue(element.ParentBlock, pair.Value);
      }
    }

    foreach (var pair in InstructionCounters) {
      if (TryFindElementForOffset(metadataTag, pair.Key, ir, out var element)) {
        result.CounterElements.Add((element, pair.Value));
      }

      result.FunctionCountersValue.Add(pair.Value);
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
      long rva = pair.Key + FunctionDebugInfo.RVA;
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

      result.FunctionCountersValue.Add(pair.Value);
    }

    result.FirstLineIndex = firstLine;
    result.LastLineIndex = lastLine;
    return result;
  }

  public PerformanceCounterValueSet ComputeFunctionTotalCounters() {
    var result = new PerformanceCounterValueSet();

    foreach (var pair in InstructionCounters) {
      result.Add(pair.Value);
    }

    return result;
  }

  public void Reset() {
    Weight = TimeSpan.Zero;
    ExclusiveWeight = TimeSpan.Zero;
    SampleStartIndex = int.MaxValue;
    SampleEndIndex = int.MinValue;
    InstructionWeight?.Clear();
    InstructionCounters?.Clear();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InstructionWeight ??= new Dictionary<long, TimeSpan>();
    InstructionCounters ??= new Dictionary<long, PerformanceCounterValueSet>();

    SampleStartIndex = int.MaxValue;
    SampleEndIndex = int.MinValue;
  }

  public class ProcessingResult {
    public ProcessingResult(int capacity = 0) {
      SampledElements = new List<(IRElement, TimeSpan)>(capacity);
      BlockSampledElementsMap = new Dictionary<BlockIR, TimeSpan>(capacity);
      BlockSampledElements = new List<(BlockIR, TimeSpan)>();
      CounterElements = new List<(IRElement, PerformanceCounterValueSet)>(capacity);
      FunctionCountersValue = new PerformanceCounterValueSet();
    }

    public List<(IRElement, TimeSpan)> SampledElements { get; set; }
    public Dictionary<BlockIR, TimeSpan> BlockSampledElementsMap { get; set; }
    public List<(BlockIR, TimeSpan)> BlockSampledElements { get; set; }
    public List<(IRElement, PerformanceCounterValueSet)> CounterElements { get; set; }
    public List<(BlockIR, PerformanceCounterValueSet)> BlockCounterElements { get; set; }
    public PerformanceCounterValueSet FunctionCountersValue { get; set; }

    public double ScaleCounterValue(long value, PerformanceCounter counter) {
      long total = FunctionCountersValue.FindCounterValue(counter);
      return total > 0 ? value / (double)total : 0;
    }

    public void SortSampledElements() {
      BlockSampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
      SampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
    }
  }

  public class SourceLineProcessingResult {
    public SourceLineProcessingResult() {
      SourceLineWeight = new Dictionary<int, TimeSpan>();
      SourceLineCounters = new Dictionary<int, PerformanceCounterValueSet>();
      FunctionCountersValue = new PerformanceCounterValueSet();
    }

    public Dictionary<int, TimeSpan> SourceLineWeight { get; set; } // Line number mapping
    public Dictionary<int, PerformanceCounterValueSet> SourceLineCounters { get; set; } // Line number mapping
    public PerformanceCounterValueSet FunctionCountersValue { get; set; }
    public List<(int LineNumber, TimeSpan Weight)> SourceLineWeightList => SourceLineWeight.ToList();
    public int FirstLineIndex { get; set; }
    public int LastLineIndex { get; set; }
  }
}
