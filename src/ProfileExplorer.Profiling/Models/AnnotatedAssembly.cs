// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling;

/// <summary>
/// Annotated disassembly output for a function, including per-instruction timing.
/// </summary>
public class AnnotatedAssembly {
  public AnnotatedAssembly(
    string fullText,
    IReadOnlyList<AssemblyLine> lines,
    IReadOnlyList<HotLine> hotLines) {
    FullText = fullText;
    Lines = lines;
    HotLines = hotLines;
  }

  /// <summary>Complete annotated disassembly text.</summary>
  public string FullText { get; }

  /// <summary>All disassembled instructions with their profiling data.</summary>
  public IReadOnlyList<AssemblyLine> Lines { get; }

  /// <summary>Only instructions above the minimum percent threshold, sorted descending.</summary>
  public IReadOnlyList<HotLine> HotLines { get; }
}

/// <summary>
/// A single disassembled instruction with profiling attribution.
/// </summary>
public class AssemblyLine {
  public AssemblyLine(
    long address,
    long rva,
    string instructionText,
    TimeSpan weight,
    double percent,
    string? sourceFile,
    int? sourceLine) {
    Address = address;
    Rva = rva;
    InstructionText = instructionText;
    Weight = weight;
    Percent = percent;
    SourceFile = sourceFile;
    SourceLine = sourceLine;
  }

  /// <summary>Absolute virtual address of the instruction.</summary>
  public long Address { get; }

  /// <summary>Relative Virtual Address within the module.</summary>
  public long Rva { get; }

  /// <summary>Disassembled instruction text (e.g., "call CIconCache::GetIcon").</summary>
  public string InstructionText { get; }

  /// <summary>Accumulated CPU time on this instruction.</summary>
  public TimeSpan Weight { get; }

  /// <summary>Percentage of function's total CPU time.</summary>
  public double Percent { get; }

  /// <summary>Source file path (if available from PDB).</summary>
  public string? SourceFile { get; }

  /// <summary>Source line number (if available from PDB).</summary>
  public int? SourceLine { get; }
}

/// <summary>
/// A hot instruction — an instruction that consumed a significant portion of CPU time.
/// </summary>
public class HotLine {
  public HotLine(
    long instructionOffset,
    double percent,
    TimeSpan time,
    string instructionText,
    string? sourceFile,
    int? sourceLine) {
    InstructionOffset = instructionOffset;
    Percent = percent;
    Time = time;
    InstructionText = instructionText;
    SourceFile = sourceFile;
    SourceLine = sourceLine;
  }

  /// <summary>RVA offset from function start.</summary>
  public long InstructionOffset { get; }

  /// <summary>Percentage of function's total CPU time.</summary>
  public double Percent { get; }

  /// <summary>Absolute CPU time on this instruction.</summary>
  public TimeSpan Time { get; }

  /// <summary>Disassembled instruction text.</summary>
  public string InstructionText { get; }

  /// <summary>Source file path (if available).</summary>
  public string? SourceFile { get; }

  /// <summary>Source line number (if available).</summary>
  public int? SourceLine { get; }
}
