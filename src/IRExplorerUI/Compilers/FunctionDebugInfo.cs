// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI.Compilers;

[ProtoContract(SkipConstructor = true)]
public struct SourceFileDebugInfo : IEquatable<SourceFileDebugInfo> {
  public SourceFileDebugInfo(string filePath, string originalFilePath, int startLine = 0,
                             bool hasChecksumMismatch = false) {
    FilePath = filePath != null ? string.Intern(filePath) : null;
    OriginalFilePath = originalFilePath != null ? string.Intern(originalFilePath) : null;
    StartLine = startLine;
    HasChecksumMismatch = hasChecksumMismatch;
  }

  [ProtoMember(1)]
  public string FilePath { get; set; }
  [ProtoMember(2)]
  public string OriginalFilePath { get; set; }
  [ProtoMember(3)]
  public int StartLine { get; set; }
  [ProtoMember(4)]
  public bool HasChecksumMismatch { get; set; }
  public static readonly SourceFileDebugInfo Unknown = new SourceFileDebugInfo(null, null, -1);
  public bool IsUnknown => FilePath == null;
  public bool HasFilePath => !string.IsNullOrEmpty(FilePath);
  public bool HasOriginalFilePath => !string.IsNullOrEmpty(OriginalFilePath);

  public bool Equals(SourceFileDebugInfo other) {
    return FilePath.Equals(other.FilePath, StringComparison.Ordinal) &&
           OriginalFilePath.Equals(other.OriginalFilePath, StringComparison.Ordinal) &&
           StartLine == other.StartLine &&
           HasChecksumMismatch == other.HasChecksumMismatch;
  }

  public override bool Equals(object obj) {
    return obj is SourceFileDebugInfo other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine(FilePath, OriginalFilePath, StartLine, HasChecksumMismatch);
  }
}

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

  public static readonly SourceLineDebugInfo Unknown = new SourceLineDebugInfo(-1, -1);
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

[ProtoContract(SkipConstructor = true)]
public class FunctionDebugInfo : IEquatable<FunctionDebugInfo>, IComparable<FunctionDebugInfo>, IComparable<long> {
  public static readonly FunctionDebugInfo Unknown = new FunctionDebugInfo(null, 0, 0);
  private int cachedHashCode_;

  public FunctionDebugInfo(string name, long rva, long size, short optLevel = 0, int id = -1, short auxId = -1) {
    // Note that string interning is not done here on purpose because
    // it is often the slowest part in processing a trace, while the memory
    // saving are quite small (under 15%, a few dozen MBs even for big traces).
    Name = name;
    RVA = (uint)rva;
    Size = (uint)size;
    OptimizationLevel = optLevel;
    SourceLines = null;
    Id = id;
    AuxiliaryId = auxId;
  }

  [ProtoMember(1)]
  public long Id { get; set; } // Used for MethodToken in managed code.
  [ProtoMember(2)]
  public string Name { get; private set; }
  [ProtoMember(3)]
  public List<SourceLineDebugInfo> SourceLines { get; set; }
  [ProtoMember(4)]
  public long AuxiliaryId { get; set; } // Used for RejitID in managed code.
  [ProtoMember(5)]
  public uint RVA { get; set; }
  [ProtoMember(6)]
  public uint Size { get; set; }
  [ProtoMember(7)]
  public short OptimizationLevel { get; set; } // Used for OptimizationTier in managed code.

  public bool HasSourceLines => SourceLines is {Count: > 0};
  public SourceLineDebugInfo FirstSourceLine => HasSourceLines ?
    SourceLines[0] : SourceLineDebugInfo.Unknown;

  public SourceLineDebugInfo LastSourceLine => HasSourceLines ?
    SourceLines[^1] : SourceLineDebugInfo.Unknown;

  //? TODO: Remove SourceFileName from SourceLineDebugInfo
  public string SourceFileName { get; set; }
  public string OriginalSourceFileName { get; set; }
  public uint StartRVA => RVA;
  public uint EndRVA => RVA + Size - 1;
  public bool IsUnknown => RVA == 0 && Size == 0;

  public static T BinarySearch<T>(List<T> ranges, long value) where T : IComparable<long> {
    int min = 0;
    int max = ranges.Count - 1;

    while (min <= max) {
      int mid = (min + max) / 2;
      var range = ranges[mid];
      int comparison = range.CompareTo(value);

      if (comparison == 0) {
        return range;
      }

      if (comparison < 0) {
        min = mid + 1;
      }
      else {
        max = mid - 1;
      }
    }

    return default(T);
  }

  public void UpdateName(string newName) {
    Name = newName != null ? string.Intern(newName) : null;
  }

  public void AddSourceLine(SourceLineDebugInfo sourceLine) {
    SourceLines ??= new List<SourceLineDebugInfo>(1);
    SourceLines.Add(sourceLine);
  }

  public SourceLineDebugInfo FindNearestLine(long offset) {
    if (!HasSourceLines) {
      return SourceLineDebugInfo.Unknown;
    }

    if (offset < SourceLines[0].OffsetStart) {
      return SourceLineDebugInfo.Unknown;
    }

    // Find line mapped to same offset or nearest smaller offset.
    int low = 0;
    int high = SourceLines.Count - 1;

    while (low <= high) {
      int middle = low + (high - low) / 2;

      if (SourceLines[middle].OffsetStart == offset) {
        return SourceLines[middle];
      }

      if (SourceLines[middle].OffsetStart > offset) {
        high = middle - 1;
      }
      else {
        low = middle + 1;
      }
    }

    return SourceLines[high];
  }

  public override bool Equals(object obj) {
    return obj is FunctionDebugInfo info && Equals(info);
  }

  public override int GetHashCode() {
    if (cachedHashCode_ == 0) {
      cachedHashCode_ = HashCode.Combine(Name, RVA, Id, AuxiliaryId);
    }

    return cachedHashCode_;
  }

  public override string ToString() {
    return $"{Name}, RVA: {RVA:X}, Size: {Size}, Id: {Id}, AuxId: {AuxiliaryId}";
  }

  public int CompareTo(FunctionDebugInfo other) {
    if (other == null) return 0;

    if (StartRVA < other.StartRVA && EndRVA < other.EndRVA) {
      return -1;
    }

    if (StartRVA > other.StartRVA && EndRVA > other.EndRVA) {
      return 1;
    }

    return 0;
  }

  public int CompareTo(long value) {
    if (value < StartRVA) {
      return 1;
    }

    if (value > EndRVA) {
      return -1;
    }

    return 0;
  }

  public bool Equals(FunctionDebugInfo other) {
    if (ReferenceEquals(null, other)) {
      return false;
    }

    if (ReferenceEquals(this, other)) {
      return true;
    }

    return RVA == other.RVA &&
           Size == other.Size &&
           Name.Equals(other.Name, StringComparison.Ordinal) &&
           Id == other.Id &&
           AuxiliaryId == other.AuxiliaryId;
  }
}