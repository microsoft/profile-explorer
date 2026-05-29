// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling;

/// <summary>
/// Aggregated profiling data for a single function.
/// </summary>
public class FunctionProfile {
  public FunctionProfile(
    string moduleName,
    string functionName,
    long functionRva,
    int functionSize,
    TimeSpan inclusiveWeight,
    TimeSpan exclusiveWeight,
    double inclusivePercent,
    double exclusivePercent,
    string? sourceFile,
    int? sourceLine,
    bool isManaged,
    Dictionary<long, TimeSpan> instructionWeights) {
    ModuleName = moduleName;
    FunctionName = functionName;
    FunctionRva = functionRva;
    FunctionSize = functionSize;
    InclusiveWeight = inclusiveWeight;
    ExclusiveWeight = exclusiveWeight;
    InclusivePercent = inclusivePercent;
    ExclusivePercent = exclusivePercent;
    SourceFile = sourceFile;
    SourceLine = sourceLine;
    IsManaged = isManaged;
    InstructionWeights = instructionWeights;
  }

  /// <summary>Module/image name (e.g., "ntdll.dll").</summary>
  public string ModuleName { get; }

  /// <summary>Function name (e.g., "RtlAllocateHeap").</summary>
  public string FunctionName { get; }

  /// <summary>Relative Virtual Address of the function start.</summary>
  public long FunctionRva { get; }

  /// <summary>Function size in bytes.</summary>
  public int FunctionSize { get; }

  /// <summary>Total time including callees (inclusive/total weight).</summary>
  public TimeSpan InclusiveWeight { get; internal set; }

  /// <summary>Self time only — samples where IP was inside this function (exclusive weight).</summary>
  public TimeSpan ExclusiveWeight { get; }

  /// <summary>Inclusive weight as percentage of total trace time.</summary>
  public double InclusivePercent { get; internal set; }

  /// <summary>Exclusive weight as percentage of total trace time.</summary>
  public double ExclusivePercent { get; }

  /// <summary>Source file path for the function entry point (if available from PDB).</summary>
  public string? SourceFile { get; }

  /// <summary>Source line number for the function entry point.</summary>
  public int? SourceLine { get; }

  /// <summary>Whether the binary is available for disassembly.</summary>
  public bool HasAssembly { get; internal set; }

  /// <summary>
  /// Per-instruction-offset weights. Key = RVA offset from function start, Value = accumulated CPU time.
  /// </summary>
  public IReadOnlyDictionary<long, TimeSpan> InstructionWeights { get; }

  /// <summary>
  /// Per-instruction PMU counter values. Only populated when IncludePerformanceCounters is enabled.
  /// Key = RVA offset from function start.
  /// </summary>
  public IReadOnlyDictionary<long, InstructionCounterValues>? InstructionCounters { get; internal set; }

  /// <summary>Whether this is a managed (.NET) function.</summary>
  public bool IsManaged { get; }

  /// <summary>Qualified name in "module!function" format.</summary>
  public string QualifiedName => $"{ModuleName}!{FunctionName}";

  public override string ToString() =>
    $"{QualifiedName} (Self: {ExclusiveWeight.TotalMilliseconds:F1}ms / {ExclusivePercent:F2}%)";
}
