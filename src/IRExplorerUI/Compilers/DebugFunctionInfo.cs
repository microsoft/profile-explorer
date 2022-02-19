using System;
using System.Collections.Generic;
using System.Linq;

namespace IRExplorerUI.Compilers;

public class DebugFunctionInfo : IEquatable<DebugFunctionInfo> {
    public string Name { get; set; }
    public long RVA { get; set; }
    public long Size { get; set; }
    public SourceLineInfo StartSourceLine { get; set; }
    public List<SourceLineInfo> SourceLines { get; set; }
    public int Id { get; set; }
    public string ModuleName { get; set; }

    public long StartRVA => RVA;
    public long EndRVA => RVA + Size - 1;

    public static DebugFunctionInfo Unknown = new DebugFunctionInfo(null, 0, 0);
    public bool IsUnknown => Name == null;
    public bool HasSourceLines => SourceLines != null && SourceLines.Count > 0;
    public bool HasModuleName => !string.IsNullOrEmpty(ModuleName);

    public DebugFunctionInfo(string name, long rva, long size, int id = -1) {
        Name = name;
        RVA = rva;
        Size = size;
        StartSourceLine = SourceLineInfo.Unknown;
        SourceLines = null;
        Id = id;
    }
    
    public SourceLineInfo FindNearestLine(long offset) {
        if (!HasSourceLines) {
            return SourceLineInfo.Unknown;
        }

        if (offset < SourceLines[0].Offset) {
            return SourceLineInfo.Unknown;
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



    public override bool Equals(object obj) {
        return obj is DebugFunctionInfo info && Equals(info);
    }

    public bool Equals(DebugFunctionInfo other) {
        return RVA == other.RVA &&
               Size == other.Size;
    }

    public override int GetHashCode() {
        return HashCode.Combine(Name, RVA, Size);
    }
}

public struct SourceLineInfo : IEquatable<SourceLineInfo> {
    public long Offset { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string FilePath { get; set; }

    public static SourceLineInfo Unknown = new SourceLineInfo(-1, -1);
    public bool IsUnknown => Line == -1;

    public SourceLineInfo(long offset, int line, int column = 0, string filePath = null) {
        Offset = offset;
        Line = line;
        Column = column;
        FilePath = filePath;
    }

    public bool Equals(SourceLineInfo other) {
        return Offset == other.Offset && Line == other.Line && 
               Column == other.Column && FilePath == other.FilePath;
    }

    public override bool Equals(object obj) {
        return obj is SourceLineInfo other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(FilePath, Line, Column);
    }
}