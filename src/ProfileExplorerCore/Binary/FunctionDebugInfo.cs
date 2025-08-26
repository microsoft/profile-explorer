// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProtoBuf;

namespace ProfileExplorerCore.Binary;

[ProtoContract(SkipConstructor = true)]
public class FunctionDebugInfo : IEquatable<FunctionDebugInfo>, IComparable<FunctionDebugInfo>, IComparable<long> {
  public static readonly FunctionDebugInfo Unknown = new(null, 0, 0);

  public FunctionDebugInfo(string name, long rva, uint size, short optLevel = 0, int id = -1, short auxId = -1) {
    // Note that string interning is not done here on purpose because
    // it is often the slowest part in processing a trace, while the memory
    // saving are quite small (under 15%, a few dozen MBs even for big traces).
    Name = name;
    RVA = rva;
    Size = size;
    OptimizationLevel = optLevel;
    SourceLines = null;
    Id = id;
    AuxiliaryId = auxId;
  }

  [ProtoMember(1)]
  public long Id { get; set; } // Used for MethodToken in managed code.
  [ProtoMember(2)]
  public string Name { get; set; }
  [ProtoMember(3)]
  public List<SourceLineDebugInfo> SourceLines { get; set; }
  [ProtoMember(4)]
  public long AuxiliaryId { get; set; } // Used for RejitID in managed code.
  [ProtoMember(5)]
  public long RVA { get; set; }
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
  public long StartRVA => RVA;
  public long EndRVA => RVA + Size - 1;
  public bool IsUnknown => RVA == 0 && Size == 0;

  public int CompareTo(FunctionDebugInfo other) {
    // Userd by sorting.
    if (other == null) return 0;

    if (other.StartRVA < StartRVA) {
      return 1;
    }

    if (other.StartRVA > StartRVA) {
      return -1;
    }

    return 0;
  }

  public int CompareTo(long value) {
    // Used by binary search.
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
           Id == other.Id &&
           AuxiliaryId == other.AuxiliaryId;
  }

  public static FunctionDebugInfo BinarySearch(List<FunctionDebugInfo> ranges, long value,
                                               bool hasOverlappingFuncts = false) {
    int low = 0;
    int high = ranges.Count - 1;

    while (low <= high) {
      int mid = low + (high - low) / 2;
      var range = ranges[mid];
      int result = range.CompareTo(value);

      if (result == 0) {
        // With code written in assembly, it's possible to have overlapping functions
        // (or rather, one function with multiple entry points). In such a case,
        // pick the outer function that contains the given RVA.
        // |F1------------------|--
        // -----|F2----|-----------
        // ----------------|F3|----
        // If the RVA is inside F2 or F3, pick F1 instead since it covers the whole range.
        if (hasOverlappingFuncts) {
          int count = 0;

          while (--mid >= 0 && count++ < 10) {
            var otherRange = ranges[mid];

            if (otherRange.CompareTo(value) == 0 &&
                (otherRange.StartRVA != range.StartRVA ||
                 otherRange.Size > range.Size)) {
              return otherRange;
            }
          }
        }

        return range;
      }

      if (result < 0) {
        low = mid + 1;
      }
      else {
        high = mid - 1;
      }
    }

    return null;
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
    return HashCode.Combine(RVA, Size, Id);
  }

  public override string ToString() {
    return $"{Name}, RVA: {RVA:X}, Size: {Size}, Id: {Id}, AuxId: {AuxiliaryId}";
  }
}