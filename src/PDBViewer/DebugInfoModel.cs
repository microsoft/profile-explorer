// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Runtime.InteropServices;
using System.Text;

namespace PDBViewer;

//? TODO: Move to ProfileExplorerCore and share with ProfileExplorerUI.
public class FunctionDebugInfo : IEquatable<FunctionDebugInfo>, IComparable<FunctionDebugInfo>, IComparable<long> {
  public static readonly FunctionDebugInfo Unknown = new(null, 0, 0);

  public FunctionDebugInfo(string name, long rva, uint size) {
    Name = name;
    RVA = rva;
    Size = size;
    SourceLines = null;
  }

  public string Name { get; private set; }
  public List<SourceLineDebugInfo> SourceLines { get; set; }
  public long RVA { get; set; }
  public uint Size { get; set; }
  public bool HasSourceLines => SourceLines is { Count: > 0 };
  public SourceLineDebugInfo FirstSourceLine => HasSourceLines ? SourceLines[0] : SourceLineDebugInfo.Unknown;
  public SourceLineDebugInfo LastSourceLine => HasSourceLines ? SourceLines[^1] : SourceLineDebugInfo.Unknown;

  public string SourceFileName => HasSourceLines ? SourceLines[0].FilePath : null;
  public string OriginalSourceFileName { get; set; }
  public long StartRVA => RVA;
  public long EndRVA => RVA + Size - 1;
  public bool IsUnknown => RVA == 0 && Size == 0;
  public bool IsPublic { get; set; }
  public bool IsSelected { get; set; }
  public bool HasOverlap { get; set; }
  public bool HasSelectionOverlap { get; set; }

  public static FunctionDebugInfo BinarySearch(List<FunctionDebugInfo> ranges, long value,
                                               bool hasOverlappingFuncts = false) {
    int low = 0;
    int high = ranges.Count - 1;

    while (low <= high) {
      int mid = low + (high - low) / 2;
      var range = ranges[mid];
      int result = range.CompareTo(value);

      if (result == 0) {
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

  public override bool Equals(object obj) {
    return obj is FunctionDebugInfo info && Equals(info);
  }

  public override int GetHashCode() {
    return HashCode.Combine(RVA, Size);
  }

  public override string ToString() {
    return $"{Name}, RVA: {RVA:X}, Size: {Size}";
  }

  public int CompareTo(FunctionDebugInfo other) {
    // Used by sorting.
    if (other == null)
      return 0;

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
           Size == other.Size;
  }
}


public class SourceLineDebugInfo : IEquatable<SourceLineDebugInfo> {
  public int RVA { get; set; }
  public int Line { get; set; }
  public int Column { get; set; }
  public string FilePath { get; private set; } //? Move to FunctionDebugInfo, add OriginalFilePath for SourceLink
  public List<SourceStackFrame> Inlinees { get; set; }
  public int InlineeCount => Inlinees != null ? Inlinees.Count : 0;
  public static readonly SourceLineDebugInfo Unknown = new(-1, -1);
  public bool IsUnknown => Line == -1;

  public SourceLineDebugInfo(int rva, int line, int column = 0, string filePath = null) {
    RVA = rva;
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
    return RVA == other.RVA && Line == other.Line &&
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


public sealed class SourceStackFrame : IEquatable<SourceStackFrame> {
  public SourceStackFrame(string function, string filePath, int line, int column) {
    Function = function;
    FilePath = filePath;
    Line = line;
    Column = column;
  }

  public string Function { get; set; }
  public string FilePath { get; set; }
  public int Line { get; set; }
  public int Column { get; set; }

  public static bool operator ==(SourceStackFrame left, SourceStackFrame right) {
    return Equals(left, right);
  }

  public static bool operator !=(SourceStackFrame left, SourceStackFrame right) {
    return !Equals(left, right);
  }

  public override bool Equals(object obj) {
    return ReferenceEquals(this, obj) || obj is SourceStackFrame other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine(Function, FilePath, Line, Column);
  }

  public bool Equals(SourceStackFrame other) {
    if (ReferenceEquals(null, other))
      return false;
    if (ReferenceEquals(this, other))
      return true;
    return Line == other.Line && Column == other.Column &&
           Function.Equals(other.Function, StringComparison.OrdinalIgnoreCase) &&
           FilePath.Equals(other.FilePath, StringComparison.OrdinalIgnoreCase);
  }

  public bool HasSameFunction(SourceStackFrame inlinee) {
    return Function.Equals(inlinee.Function, StringComparison.OrdinalIgnoreCase) &&
           FilePath.Equals(inlinee.FilePath, StringComparison.OrdinalIgnoreCase);
  }
}


static class NativeMethods {
  [DllImport("dbghelp.dll", SetLastError = true, PreserveSig = true)]
  public static extern int UnDecorateSymbolName(
    [In][MarshalAs(UnmanagedType.LPStr)] string DecoratedName,
    [Out] StringBuilder UnDecoratedName,
    [In][MarshalAs(UnmanagedType.U4)] int UndecoratedLength,
    [In][MarshalAs(UnmanagedType.U4)] UnDecorateFlags Flags);

  // C++ function name demangling
  [Flags]
  public enum UnDecorateFlags {
    UNDNAME_COMPLETE = 0x0000, // Enable full undecoration
    UNDNAME_NO_LEADING_UNDERSCORES = 0x0001, // Remove leading underscores from MS extended keywords
    UNDNAME_NO_MS_KEYWORDS = 0x0002, // Disable expansion of MS extended keywords
    UNDNAME_NO_FUNCTION_RETURNS = 0x0004, // Disable expansion of return type for primary declaration
    UNDNAME_NO_ALLOCATION_MODEL = 0x0008, // Disable expansion of the declaration model
    UNDNAME_NO_ALLOCATION_LANGUAGE = 0x0010, // Disable expansion of the declaration language specifier
    UNDNAME_NO_MS_THISTYPE = 0x0020, // NYI Disable expansion of MS keywords on the 'this' type for primary declaration
    UNDNAME_NO_CV_THISTYPE = 0x0040, // NYI Disable expansion of CV modifiers on the 'this' type for primary declaration
    UNDNAME_NO_THISTYPE = 0x0060, // Disable all modifiers on the 'this' type
    UNDNAME_NO_ACCESS_SPECIFIERS = 0x0080, // Disable expansion of access specifiers for members
    UNDNAME_NO_THROW_SIGNATURES =
      0x0100, // Disable expansion of 'throw-signatures' for functions and pointers to functions
    UNDNAME_NO_MEMBER_TYPE = 0x0200, // Disable expansion of 'static' or 'virtual'ness of members
    UNDNAME_NO_RETURN_UDT_MODEL = 0x0400, // Disable expansion of MS model for UDT returns
    UNDNAME_32_BIT_DECODE = 0x0800, // Undecorate 32-bit decorated names
    UNDNAME_NAME_ONLY = 0x1000, // Crack only the name for primary declaration;
                                // return just [scope::]name.  Does expand template params
    UNDNAME_NO_ARGUMENTS = 0x2000, // Don't undecorate arguments to function
    UNDNAME_NO_SPECIAL_SYMS = 0x4000 // Don't undecorate special names (v-table, vcall, vector xxx, metatype, etc)
  }
}