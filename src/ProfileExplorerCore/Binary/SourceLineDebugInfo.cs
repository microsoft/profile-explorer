// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProfileExplorerCore.IR.Tags;
using ProtoBuf;

namespace ProfileExplorerCore.Binary;

[ProtoContract(SkipConstructor = true)]
public struct SourceLineDebugInfo : IEquatable<SourceLineDebugInfo> {
  [ProtoMember(1)]
  public int OffsetStart { get; set; } // Offset in bytes relative to function start.
  [ProtoMember(2)]
  public int OffsetEnd { get; set; } // Offset in bytes relative to function start.
  [ProtoMember(3)]
  public int Line { get; set; }
  [ProtoMember(4)]
  public int Column { get; set; }
  [ProtoMember(5)]
  public string FilePath { get; private set; } //? Move to FunctionDebugInfo, add OriginalFilePath for SourceLink
  public List<SourceStackFrame> Inlinees { get; set; }
  public static readonly SourceLineDebugInfo Unknown = new(-1, -1);
  public bool IsUnknown => Line == -1;

  public SourceLineDebugInfo(int offsetStart, int line, int column = 0, string filePath = null) {
    OffsetStart = offsetStart;
    OffsetEnd = offsetStart;
    Line = line;
    Column = column;
    FilePath = filePath != null ? string.Intern(filePath) : null;
  }

  public void AddInlinee(SourceStackFrame inlinee) {
    Inlinees ??= new List<SourceStackFrame>();
    Inlinees.Add(inlinee);
  }

  public bool HasInlinee(SourceStackFrame inlinee) {
    return Inlinees != null && Inlinees.Contains(inlinee);
  }

  public SourceStackFrame FindSameFunctionInlinee(SourceStackFrame inlinee) {
    return Inlinees?.Find(item => item.HasSameFunction(inlinee));
  }

  public bool Equals(SourceLineDebugInfo other) {
    return OffsetStart == other.OffsetStart && Line == other.Line &&
           Column == other.Column &&
           FilePath.Equals(other.FilePath, StringComparison.Ordinal);
  }

  public override bool Equals(object obj) {
    return obj is SourceLineDebugInfo other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine(FilePath, Line, Column);
  }
}