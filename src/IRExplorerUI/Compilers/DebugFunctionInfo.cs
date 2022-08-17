using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace IRExplorerUI.Compilers;

[ProtoContract(SkipConstructor = true)]
public class DebugFunctionInfo : IEquatable<DebugFunctionInfo>, IComparable<DebugFunctionInfo>, IComparable<long> {
    [ProtoMember(1)]
    public long Id { get; set; }
    [ProtoMember(2)]
    public string Name { get; set; }
    [ProtoMember(3)]
    public long RVA { get; set; }
    [ProtoMember(4)]
    public long Size { get; set; }
    [ProtoMember(5)]
    public DebugSourceLineInfo StartDebugSourceLine { get; set; }
    [ProtoMember(6)]
    public List<DebugSourceLineInfo> SourceLines { get; set; }
    [ProtoMember(7)]
    public string OptimizationLevel { get; set; }

    public long StartRVA => RVA;
    public long EndRVA => RVA + Size - 1;
    public bool HasSourceLines => SourceLines != null && SourceLines.Count > 0;
    public bool HasOptimizationLevel => !string.IsNullOrEmpty(OptimizationLevel);

    public DebugFunctionInfo(string name, long rva, long size, string optLevel = null, int id = -1) {
        Name = name;
        RVA = rva;
        Size = size;
        OptimizationLevel = optLevel;
        StartDebugSourceLine = DebugSourceLineInfo.Unknown;
        SourceLines = null;
        Id = id;
    }

    public void AddSourceLine(DebugSourceLineInfo sourceLine) {
        SourceLines ??= new List<DebugSourceLineInfo>();
        SourceLines.Add(sourceLine);

        if (StartDebugSourceLine.IsUnknown) {
            StartDebugSourceLine = sourceLine;
        }
    }

    public DebugSourceLineInfo FindNearestLine(long offset) {
        if (!HasSourceLines) {
            return DebugSourceLineInfo.Unknown;
        }

        if (offset < SourceLines[0].OffsetStart) {
            return DebugSourceLineInfo.Unknown;
        }

        // Find line mapped to same offset or nearest smaller offset.
        int low = 0;
        int high = SourceLines.Count - 1;

        while (low <= high) {
            var middle = low + (high - low) / 2;

            if (SourceLines[middle].OffsetStart == offset) {
                return SourceLines[middle];
            }
            else if (SourceLines[middle].OffsetStart > offset) {
                high = middle - 1;
            }
            else {
                low = middle + 1;
            }
        }

        return SourceLines[high];
    }

    public static T BinarySearch<T>(List<T> ranges, long value) where T: IComparable<long> {
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

    public override bool Equals(object obj) {
        return obj is DebugFunctionInfo info && Equals(info);
    }

    public bool Equals(DebugFunctionInfo other) {
        return RVA == other.RVA &&
               Size == other.Size &&
               Id == other.Id &&
               Name.Equals(other.Name, StringComparison.Ordinal);
    }

    public override int GetHashCode() {
        return HashCode.Combine(Id, RVA, Size);
    }

    public int CompareTo(DebugFunctionInfo other) {
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

    public override string ToString() {
        return $"{Name}, RVA: {RVA:X}, Size: {Size}, Id: {Id}";
    }
}

[ProtoContract(SkipConstructor = true)]
public struct DebugFunctionSourceFileInfo : IEquatable<DebugFunctionSourceFileInfo> {
    public DebugFunctionSourceFileInfo(string filePath, string originalFilePath, int startLine = 0, bool hasChecksumMismatch = false) {
        FilePath = filePath;
        OriginalFilePath = originalFilePath;
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
    public static DebugFunctionSourceFileInfo Unknown => new(null, null, -1);

    public bool IsUnknown => FilePath == null;
    public bool HasFilePath => !string.IsNullOrEmpty(FilePath);
    public bool HasOriginalFilePath => !string.IsNullOrEmpty(OriginalFilePath);

    public bool Equals(DebugFunctionSourceFileInfo other) {
        return FilePath == other.FilePath &&
               OriginalFilePath == other.OriginalFilePath &&
               StartLine == other.StartLine &&
               HasChecksumMismatch == other.HasChecksumMismatch;
    }

    public override bool Equals(object obj) {
        return obj is DebugFunctionSourceFileInfo other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(FilePath, OriginalFilePath, StartLine, HasChecksumMismatch);
    }
}

[ProtoContract(SkipConstructor = true)]
public struct DebugSourceLineInfo : IEquatable<DebugSourceLineInfo> {
    [ProtoMember(1)]
    public int OffsetStart { get; set; } // Offset in bytes relative to function start.
    [ProtoMember(2)]
    public int OffsetEnd { get; set; } // Offset in bytes relative to function start.
    [ProtoMember(3)]
    public int Line { get; set; }
    [ProtoMember(4)]
    public int Column { get; set; }
    [ProtoMember(5)]
    public string FilePath { get; set; }

    public static DebugSourceLineInfo Unknown = new DebugSourceLineInfo(-1, -1);
    public bool IsUnknown => Line == -1;

    public DebugSourceLineInfo(int offsetStart, int line, int column = 0, string filePath = null) {
        OffsetStart = offsetStart;
        OffsetEnd = offsetStart;
        Line = line;
        Column = column;
        FilePath = filePath;
    }

    public bool Equals(DebugSourceLineInfo other) {
        return OffsetStart == other.OffsetStart && Line == other.Line && 
               Column == other.Column && FilePath == other.FilePath;
    }

    public override bool Equals(object obj) {
        return obj is DebugSourceLineInfo other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(FilePath, Line, Column);
    }
}