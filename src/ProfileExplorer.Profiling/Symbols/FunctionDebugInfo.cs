// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProtoBuf;

namespace ProfileExplorer.Profiling.Symbols;

/// <summary>
/// Debug information for a single function: name, RVA, size, and source line mappings.
/// Supports binary search by RVA for efficient lookup.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public class FunctionDebugInfo : IEquatable<FunctionDebugInfo>, IComparable<FunctionDebugInfo>, IComparable<long> {
  public static readonly FunctionDebugInfo Unknown = new(null!, 0, 0);

  public FunctionDebugInfo(string name, long rva, uint size, short optLevel = 0, int id = -1, short auxId = -1) {
    Name = name;
    RVA = rva;
    Size = size;
    OptimizationLevel = optLevel;
    Id = id;
    AuxiliaryId = auxId;
  }

  [ProtoMember(1)] public long Id { get; set; }
  [ProtoMember(2)] public string Name { get; set; }
  [ProtoMember(3)] public List<SourceLineDebugInfo>? SourceLines { get; set; }
  [ProtoMember(4)] public long AuxiliaryId { get; set; }
  [ProtoMember(5)] public long RVA { get; set; }
  [ProtoMember(6)] public uint Size { get; set; }
  [ProtoMember(7)] public short OptimizationLevel { get; set; }

  public bool HasSourceLines => SourceLines is { Count: > 0 };
  public SourceLineDebugInfo FirstSourceLine => HasSourceLines ? SourceLines![0] : SourceLineDebugInfo.Unknown;
  public SourceLineDebugInfo LastSourceLine => HasSourceLines ? SourceLines![^1] : SourceLineDebugInfo.Unknown;
  public string? SourceFileName { get; set; }
  public string? OriginalSourceFileName { get; set; }
  public long StartRVA => RVA;
  public long EndRVA => RVA + Size - 1;
  public bool IsUnknown => RVA == 0 && Size == 0;

  public int CompareTo(FunctionDebugInfo? other) {
    if (other == null) return 0;
    return StartRVA.CompareTo(other.StartRVA);
  }

  public int CompareTo(long value) {
    if (value < StartRVA) return 1;
    if (value > EndRVA) return -1;
    return 0;
  }

  public bool Equals(FunctionDebugInfo? other) {
    if (other is null) return false;
    if (ReferenceEquals(this, other)) return true;
    return RVA == other.RVA && Size == other.Size && Id == other.Id && AuxiliaryId == other.AuxiliaryId;
  }

  public override bool Equals(object? obj) => obj is FunctionDebugInfo other && Equals(other);
  public override int GetHashCode() => HashCode.Combine(RVA, Size, Id, AuxiliaryId);

  /// <summary>
  /// Binary search a sorted list of FunctionDebugInfo for the function containing the given RVA.
  /// </summary>
  public static FunctionDebugInfo? BinarySearch(List<FunctionDebugInfo> ranges, long value,
                                                bool hasOverlappingFuncts = false) {
    int low = 0;
    int high = ranges.Count - 1;

    while (low <= high) {
      int mid = low + (high - low) / 2;
      var range = ranges[mid];
      int result = range.CompareTo(value);

      if (result == 0) {
        if (hasOverlappingFuncts) {
          // With overlapping functions (assembly code with multiple entry points),
          // pick the outermost function that contains the RVA.
          int count = 0;
          while (--mid >= 0 && count++ < 10) {
            var otherRange = ranges[mid];
            if (otherRange.CompareTo(value) == 0 && otherRange.Size >= range.Size) {
              range = otherRange;
            }
          }
        }

        return range;
      }

      if (result > 0) {
        high = mid - 1;
      }
      else {
        low = mid + 1;
      }
    }

    return null;
  }

  public override string ToString() => $"{Name} RVA={RVA:X} Size={Size}";
}
