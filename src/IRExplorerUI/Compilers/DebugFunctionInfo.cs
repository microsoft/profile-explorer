using System;
using System.Collections.Generic;
using System.Linq;

namespace IRExplorerUI.Compilers;

public class DebugFunctionInfo : IEquatable<DebugFunctionInfo>, IComparable<DebugFunctionInfo>, IComparable<long> {
    public long Id { get; set; }
    public string Name { get; set; }
    public long RVA { get; set; }
    public long Size { get; set; }
    public DebugSourceLineInfo StartDebugSourceLine { get; set; }
    public List<DebugSourceLineInfo> SourceLines { get; set; }
    public object Data { get; set; }

    public long StartRVA => RVA;
    public long EndRVA => RVA + Size - 1;
    public bool HasSourceLines => SourceLines != null && SourceLines.Count > 0;

    public DebugFunctionInfo(string name, long rva, long size, int id = -1) {
        Name = name;
        RVA = rva;
        Size = size;
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

        if (offset < SourceLines[0].Offset) {
            return DebugSourceLineInfo.Unknown;
        }

        // Find line mapped to same offset or nearest smaller offset.
        int low = 0;
        int high = SourceLines.Count - 1;

        while (low <= high) {
            var middle = low + (high - low) / 2;

            if (SourceLines[middle].Offset == offset) {
                return SourceLines[middle];
            }
            else if (SourceLines[middle].Offset > offset) {
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

public struct DebugFunctionSourceFileInfo : IEquatable<DebugFunctionSourceFileInfo> {
    public DebugFunctionSourceFileInfo(string filePath, string originalFilePath, int startLine = 0, bool hasChecksumMismatch = false) {
        FilePath = filePath;
        OriginalFilePath = originalFilePath;
        StartLine = startLine;
        HasChecksumMismatch = hasChecksumMismatch;
    }

    public string FilePath { get; set; }
    public string OriginalFilePath { get; set; }
    public int StartLine { get; set; }
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

public struct DebugSourceLineInfo : IEquatable<DebugSourceLineInfo> {
    public long Offset { get; set; } // Offset in bytes relative to function start.
    public int Line { get; set; }
    public int Column { get; set; }
    public string FilePath { get; set; }

    public static DebugSourceLineInfo Unknown = new DebugSourceLineInfo(-1, -1);
    public bool IsUnknown => Line == -1;

    public DebugSourceLineInfo(long offset, int line, int column = 0, string filePath = null) {
        Offset = offset;
        Line = line;
        Column = column;
        FilePath = filePath;
    }

    public bool Equals(DebugSourceLineInfo other) {
        return Offset == other.Offset && Line == other.Line && 
               Column == other.Column && FilePath == other.FilePath;
    }

    public override bool Equals(object obj) {
        return obj is DebugSourceLineInfo other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(FilePath, Line, Column);
    }
}