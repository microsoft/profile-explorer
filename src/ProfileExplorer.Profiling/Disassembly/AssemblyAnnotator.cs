// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection;
using System.Text;
using ProfileExplorer.Profiling.Profiling;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling.Disassembly;

/// <summary>
/// Combines disassembled instructions with profiling weights and source line information
/// to produce annotated assembly output.
/// </summary>
internal class AssemblyAnnotator {
  /// <summary>
  /// Annotate a list of disassembled instructions with per-instruction timing data.
  /// </summary>
  public static AnnotatedAssembly Annotate(
    IReadOnlyList<DisassembledInstruction> instructions,
    IReadOnlyDictionary<long, TimeSpan> instructionWeights,
    long functionRva,
    IDebugInfoProvider? debugInfo,
    FunctionDebugInfo? funcDebugInfo,
    ProcessorArchitecture architecture,
    double minHotLinePercent,
    int maxHotLines) {
    // Build a set of known instruction offsets for skid correction.
    var knownOffsets = new HashSet<long>();
    foreach (var instr in instructions) {
      knownOffsets.Add(instr.Rva - functionRva);
    }

    // Map instruction weights with skid correction.
    var correctedWeights = new Dictionary<long, TimeSpan>();
    TimeSpan totalFunctionWeight = TimeSpan.Zero;

    foreach (var (offset, weight) in instructionWeights) {
      long corrected = InstructionOffsetConfig.CorrectSkid(offset, knownOffsets, architecture);
      if (correctedWeights.TryGetValue(corrected, out var existing)) {
        correctedWeights[corrected] = existing + weight;
      }
      else {
        correctedWeights[corrected] = weight;
      }

      totalFunctionWeight += weight;
    }

    // Populate source lines if available.
    if (debugInfo != null && funcDebugInfo != null && !funcDebugInfo.HasSourceLines) {
      debugInfo.PopulateSourceLines(funcDebugInfo);
    }

    // Build annotated lines.
    var lines = new List<AssemblyLine>(instructions.Count);
    var hotLines = new List<HotLine>();
    var sb = new StringBuilder();

    foreach (var instr in instructions) {
      long offset = instr.Rva - functionRva;
      correctedWeights.TryGetValue(offset, out var weight);

      double percent = totalFunctionWeight > TimeSpan.Zero
        ? weight.TotalMilliseconds / totalFunctionWeight.TotalMilliseconds * 100
        : 0;

      // Find source line info.
      string? sourceFile = null;
      int? sourceLine = null;

      if (funcDebugInfo?.HasSourceLines == true) {
        var srcLine = FindSourceLineForOffset(funcDebugInfo.SourceLines!, (int)offset);
        if (!srcLine.IsUnknown) {
          sourceFile = srcLine.FilePath ?? funcDebugInfo.SourceFileName;
          sourceLine = srcLine.Line;
        }
      }

      var assemblyLine = new AssemblyLine(
        address: instr.Address,
        rva: instr.Rva,
        instructionText: instr.Text,
        weight: weight,
        percent: percent,
        sourceFile: sourceFile,
        sourceLine: sourceLine);

      lines.Add(assemblyLine);

      // Build text line.
      sb.Append($"{instr.Address:X}:    {instr.Text}");
      if (percent >= minHotLinePercent) {
        sb.Append($"    [Time(%): {percent:F2}%, Time: {weight.TotalMilliseconds:F2} ms]");
      }

      sb.AppendLine();

      // Track hot lines.
      if (percent >= minHotLinePercent) {
        hotLines.Add(new HotLine(
          instructionOffset: offset,
          percent: percent,
          time: weight,
          instructionText: instr.Text,
          sourceFile: sourceFile,
          sourceLine: sourceLine));
      }
    }

    // Sort hot lines descending by percent and limit.
    hotLines.Sort((a, b) => b.Percent.CompareTo(a.Percent));
    if (hotLines.Count > maxHotLines) {
      hotLines.RemoveRange(maxHotLines, hotLines.Count - maxHotLines);
    }

    return new AnnotatedAssembly(sb.ToString(), lines, hotLines);
  }

  private static SourceLineDebugInfo FindSourceLineForOffset(List<SourceLineDebugInfo> sourceLines, int offset) {
    // Source lines are sorted by OffsetStart. Find the line that contains this offset.
    int low = 0;
    int high = sourceLines.Count - 1;
    SourceLineDebugInfo best = SourceLineDebugInfo.Unknown;

    while (low <= high) {
      int mid = low + (high - low) / 2;
      var line = sourceLines[mid];

      if (line.OffsetStart <= offset) {
        best = line;
        low = mid + 1;
      }
      else {
        high = mid - 1;
      }
    }

    return best;
  }
}

/// <summary>
/// A raw disassembled instruction (before annotation).
/// </summary>
internal record DisassembledInstruction(long Address, long Rva, string Text, int Size);
