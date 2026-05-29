// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProtoBuf;

namespace ProfileExplorer.Profiling.Symbols;

/// <summary>
/// Source line debug information for a single instruction offset range within a function.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public struct SourceLineDebugInfo : IEquatable<SourceLineDebugInfo> {
  public static readonly SourceLineDebugInfo Unknown = new(-1, -1);

  public SourceLineDebugInfo(int offsetStart, int line, int column = 0, string? filePath = null) {
    OffsetStart = offsetStart;
    OffsetEnd = offsetStart;
    Line = line;
    Column = column;
    FilePath = filePath;
  }

  /// <summary>Offset in bytes from the function start (start of range).</summary>
  [ProtoMember(1)] public int OffsetStart { get; set; }

  /// <summary>Offset in bytes from the function start (end of range).</summary>
  [ProtoMember(2)] public int OffsetEnd { get; set; }

  /// <summary>Source line number.</summary>
  [ProtoMember(3)] public int Line { get; set; }

  /// <summary>Source column number.</summary>
  [ProtoMember(4)] public int Column { get; set; }

  /// <summary>Source file path.</summary>
  [ProtoMember(5)] public string? FilePath { get; set; }

  /// <summary>Inlined function frames at this offset.</summary>
  public List<SourceStackFrame>? Inlinees { get; set; }

  public bool IsUnknown => Line == -1;

  public void AddInlinee(SourceStackFrame inlinee) {
    Inlinees ??= [];
    Inlinees.Add(inlinee);
  }

  public bool Equals(SourceLineDebugInfo other) {
    return OffsetStart == other.OffsetStart && Line == other.Line &&
           Column == other.Column &&
           string.Equals(FilePath, other.FilePath, StringComparison.Ordinal);
  }

  public override bool Equals(object? obj) => obj is SourceLineDebugInfo other && Equals(other);
  public override int GetHashCode() => HashCode.Combine(FilePath, Line, Column);
}

/// <summary>
/// Represents a frame in an inlinee call stack at a particular source location.
/// </summary>
public struct SourceStackFrame : IEquatable<SourceStackFrame> {
  public string FunctionName { get; set; }
  public string? FilePath { get; set; }
  public int Line { get; set; }
  public int Column { get; set; }

  public SourceStackFrame(string functionName, string? filePath, int line, int column = 0) {
    FunctionName = functionName;
    FilePath = filePath;
    Line = line;
    Column = column;
  }

  public bool HasSameFunction(SourceStackFrame other) {
    return string.Equals(FunctionName, other.FunctionName, StringComparison.Ordinal);
  }

  public bool Equals(SourceStackFrame other) {
    return string.Equals(FunctionName, other.FunctionName, StringComparison.Ordinal) &&
           Line == other.Line;
  }

  public override bool Equals(object? obj) => obj is SourceStackFrame other && Equals(other);
  public override int GetHashCode() => HashCode.Combine(FunctionName, Line);
}

/// <summary>
/// Source file debug information.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public struct SourceFileDebugInfo : IEquatable<SourceFileDebugInfo> {
  public static readonly SourceFileDebugInfo Unknown = new(null, null, -1);

  public SourceFileDebugInfo(string? filePath, string? originalFilePath, int startLine = 0,
                             bool hasChecksumMismatch = false) {
    FilePath = filePath;
    OriginalFilePath = originalFilePath;
    StartLine = startLine;
    HasChecksumMismatch = hasChecksumMismatch;
  }

  [ProtoMember(1)] public string? FilePath { get; set; }
  [ProtoMember(2)] public string? OriginalFilePath { get; set; }
  [ProtoMember(3)] public int StartLine { get; set; }
  [ProtoMember(4)] public bool HasChecksumMismatch { get; set; }

  public bool IsUnknown => FilePath == null;
  public bool HasFilePath => !string.IsNullOrEmpty(FilePath);
  public bool HasOriginalFilePath => !string.IsNullOrEmpty(OriginalFilePath);

  public bool Equals(SourceFileDebugInfo other) {
    return string.Equals(FilePath, other.FilePath, StringComparison.Ordinal) &&
           string.Equals(OriginalFilePath, other.OriginalFilePath, StringComparison.Ordinal) &&
           StartLine == other.StartLine;
  }

  public override bool Equals(object? obj) => obj is SourceFileDebugInfo other && Equals(other);
  public override int GetHashCode() => HashCode.Combine(FilePath, OriginalFilePath, StartLine);
}
