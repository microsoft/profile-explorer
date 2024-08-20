// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Dia2Lib;

namespace PDBViewer {
  public partial class MainForm : Form {
    private IDiaDataSource diaSource_;
    private IDiaSession session_;
    private IDiaSymbol globalSymbol_;
    private List<FunctionDebugInfo> sortedFuncList_;
    private List<FunctionDebugInfo> filteredFuncList_;
    private string debugFilePath_;

    public MainForm() {
      InitializeComponent();
      FunctionListView.VirtualMode = true;
      FunctionListView.RetrieveVirtualItem += FunctionListViewOnRetrieveVirtualItem;
      FunctionListView.KeyDown += FunctionListViewOnKeyDown;
    }

    private void FunctionListViewOnKeyDown(object? sender, KeyEventArgs e) {
      if (e.Control && e.KeyCode == Keys.C &&
          FunctionListView.SelectedIndices.Count == 1) {
        var item = filteredFuncList_[FunctionListView.SelectedIndices[0]];
        var sb = new StringBuilder();
        sb.AppendLine($"Name: {item.Name}");
        sb.AppendLine($"Start RVA: {item.RVA:X} ({item.RVA})");
        sb.AppendLine($"End RVA: {item.EndRVA:X} ({item.EndRVA})");
        sb.AppendLine($"Length: {item.Size}");
        sb.AppendLine($"Kind: {(item.IsPublic ? "Public" : "Function")}");
        Clipboard.SetText(sb.ToString());
        e.Handled = true;
      }
    }

    private void FunctionListViewOnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e) {
      if (filteredFuncList_ != null && e.ItemIndex < filteredFuncList_.Count) {
        var item = filteredFuncList_[e.ItemIndex];
        var lvi = new ListViewItem();
        lvi.Text = item.RVA.ToString("X");
        lvi.SubItems.Add(new ListViewItem.ListViewSubItem(lvi, GetFunctionName(item, DemangleCheckbox.Checked)));
        lvi.SubItems.Add(new ListViewItem.ListViewSubItem(lvi, item.Size.ToString()));
        lvi.SubItems.Add(new ListViewItem.ListViewSubItem(lvi, item.EndRVA.ToString("X")));
        lvi.SubItems.Add(new ListViewItem.ListViewSubItem(lvi, item.IsPublic ? "Public" : "Function"));

        if (item.IsSelected) {
          lvi.BackColor = Color.LightBlue;
        }

        e.Item = lvi;
      }
    }

    private async void OpenButton_Click(object sender, EventArgs e) {
      if (OpenFileDialog.ShowDialog(this) == DialogResult.OK) {
        var sw = Stopwatch.StartNew();
        StatusLabel.Text = "Opening PDB";
        FunctionListView.Enabled = false;
        ProgressBar.Visible = true;

        if (!(await OpenPDB(OpenFileDialog.FileName))) {
          MessageBox.Show("Failed to open PDB", "PDB Viewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
          ProgressBar.Visible = true;
          return;
        }

        StatusLabel.Text = "Enumerating functions";

        if (!(await LoadFunctionList())) {
          MessageBox.Show("Failed to load function list from PDB", "PDB Viewer", MessageBoxButtons.OK,
                          MessageBoxIcon.Error);
          ProgressBar.Visible = false;
          return;
        }

        StatusLabel.Text = $"Done in {sw.Elapsed.Seconds} sec";
        UpdateFunctionListView();
        ProgressBar.Visible = false;
        FunctionListView.Enabled = true;
      }
    }

    private void UpdateFunctionListView() {
      if (sortedFuncList_ == null) {
        return;
      }

      filteredFuncList_ = new List<FunctionDebugInfo>();

      bool showFuncts = ShowFunctionsCheckbox.Checked;
      bool showPublics = ShowPublicsCheckbox.Checked;
      bool filterName = SearchTextbox.Text.Length > 0;
      string searchedText = SearchTextbox.Text.Trim();
      bool demangle = DemangleCheckbox.Checked;

      foreach (var item in sortedFuncList_) {
        if (!showFuncts && !item.IsPublic) {
          continue;
        }

        if (!showPublics && item.IsPublic) {
          continue;
        }

        if (filterName) {
          if (!item.Name.Contains(searchedText, StringComparison.OrdinalIgnoreCase)) {
            continue;
          }
        }

        filteredFuncList_.Add(item);
      }

      FunctionListView.VirtualListSize = filteredFuncList_.Count;
      SymbolCountLabel.Text = filteredFuncList_.Count.ToString();
      TotalSymbolCountLabel.Text = sortedFuncList_.Count.ToString();
    }

    private void UpdateSelectedRVAItem() {
      if (filteredFuncList_ == null) {
        return;
      }

      var (hasRVA, searchedRVA) = GetRVAValue();
      int selectedIndex = -1;
      int index = 0;

      foreach (var item in filteredFuncList_) {
        if (hasRVA && FunctionDebugInfo.BinarySearch(sortedFuncList_, searchedRVA) == item) {
          item.IsSelected = true;
          selectedIndex = index;
        }
        else {
          item.IsSelected = false;
        }

        index++;
      }

      if (selectedIndex != -1) {
        FunctionListView.RedrawItems(0, filteredFuncList_.Count - 1, false);
        FunctionListView.EnsureVisible(selectedIndex);
      }
    }

    private (bool, long) GetRVAValue() {
      bool hasRVA = false;
      long searchedRVA = 0;

      if (RVATextbox.Text.Length > 0) {
        if (HexCheckbox.Checked) {
          hasRVA = long.TryParse(RVATextbox.Text.Trim(), NumberStyles.HexNumber, null, out searchedRVA);
        }
        else {
          hasRVA = long.TryParse(RVATextbox.Text.Trim(), NumberStyles.Integer, null, out searchedRVA);
        }
      }

      return (hasRVA, searchedRVA);
    }

    private void SetRVAValue(long value) {
      if (HexCheckbox.Checked) {
        RVATextbox.Text = value.ToString("X");
      }
      else {
        RVATextbox.Text = value.ToString();
      }
    }

    private async Task<bool> OpenPDB(string debugFilePath) {
      try {
        await Task.Run(() => {
          debugFilePath_ = debugFilePath;
          diaSource_ = new DiaSourceClass();
          diaSource_.loadDataFromPdb(debugFilePath);
          diaSource_.openSession(out session_);

          try {
            session_.findChildren(null, SymTagEnum.SymTagExe, null, 0, out var exeSymEnum);
            globalSymbol_ = exeSymEnum.Item(0);
          }
          catch (Exception ex) {
            Trace.TraceError($"Failed to locate global sym for file {debugFilePath}: {ex.Message}");
          }
        });
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed to load debug file {debugFilePath}: {ex.Message}");
        return false;
      }

      return true;
    }

    private async Task<bool> LoadFunctionList() {
      sortedFuncList_ = await Task.Run(CollectFunctionDebugInfo);

      if (sortedFuncList_ != null) {
        sortedFuncList_.Sort();
        return true;
      }

      return false;
    }

    private List<FunctionDebugInfo> CollectFunctionDebugInfo() {
      IDiaEnumSymbols symbolEnum = null;
      IDiaEnumSymbols publicSymbolEnum = null;

      try {
        globalSymbol_.findChildren(SymTagEnum.SymTagFunction, null, 0, out symbolEnum);
        globalSymbol_.findChildren(SymTagEnum.SymTagPublicSymbol, null, 0, out publicSymbolEnum);
        var funcSymbolsSet = new HashSet<FunctionDebugInfo>(symbolEnum.count);

        foreach (IDiaSymbol sym in symbolEnum) {
          //Trace.WriteLine($" FuncSym {sym.name}: RVA {sym.relativeVirtualAddress:X}, size {sym.length}");
          var funcInfo = new FunctionDebugInfo(sym.name, sym.relativeVirtualAddress, (uint)sym.length);
          funcSymbolsSet.Add(funcInfo);
        }

        foreach (IDiaSymbol sym in publicSymbolEnum) {
          //Trace.WriteLine($" PublicSym {sym.name}: RVA {sym.relativeVirtualAddress:X} size {sym.length}");
          var funcInfo = new FunctionDebugInfo(sym.name, sym.relativeVirtualAddress, (uint)sym.length);
          funcInfo.IsPublic = true;

          // Public symbols are preferred over function symbols if they have the same RVA and size.
          // This ensures that the mangled name is saved, set only of public symbols.
          if (funcSymbolsSet.Contains(funcInfo)) {
            funcSymbolsSet.Remove(funcInfo);
          }

          funcSymbolsSet.Add(funcInfo);
        }

        return funcSymbolsSet.ToList();
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed to enumerate functions: {ex.Message}");
      }
      finally {
        if (symbolEnum != null) {
          Marshal.ReleaseComObject(symbolEnum);
        }

        if (publicSymbolEnum != null) {
          Marshal.ReleaseComObject(publicSymbolEnum);
        }
      }

      return null;
    }

    public string GetFunctionName(FunctionDebugInfo item, bool demangle) {
      if (!demangle) {
        return item.Name;
      }

      var flags = NativeMethods.UnDecorateFlags.UNDNAME_COMPLETE;
      flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_ACCESS_SPECIFIERS |
               NativeMethods.UnDecorateFlags.UNDNAME_NO_ALLOCATION_MODEL |
               NativeMethods.UnDecorateFlags.UNDNAME_NO_MEMBER_TYPE;
      flags |= NativeMethods.UnDecorateFlags.UNDNAME_NAME_ONLY;
      flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_MS_KEYWORDS;
      flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_MS_THISTYPE;
      flags |= NativeMethods.UnDecorateFlags.UNDNAME_NO_FUNCTION_RETURNS;
      const int MaxDemangledFunctionNameLength = 8192;
      var sb = new StringBuilder(MaxDemangledFunctionNameLength);
      NativeMethods.UnDecorateSymbolName(item.Name, sb, MaxDemangledFunctionNameLength, flags);
      return sb.ToString();
    }

    private void RVATextbox_TextChanged(object? sender, EventArgs e) {
      UpdateSelectedRVAItem();
    }

    private void SearchTextbox_TextChanged(object? sender, EventArgs e) {
      UpdateFunctionListView();
    }

    private void HexCheckbox_CheckedChanged(object? sender, EventArgs e) {
      UpdateSelectedRVAItem();
    }

    private void ShowPublicsCheckbox_CheckedChanged(object sender, EventArgs e) {
      UpdateFunctionListView();
    }

    private void ShowFunctionsCheckbox_CheckStateChanged(object sender, EventArgs e) {
      UpdateFunctionListView();
    }

    private async void DemangleCheckbox_CheckedChanged(object sender, EventArgs e) {
      UpdateFunctionListView();
    }

    private void AddRVAButton_Click(object sender, EventArgs e) {
      var (hasRVA, value) = GetRVAValue();

      if (hasRVA) {
        SetRVAValue(value + 4);
      }
    }

    private void SubtractRVAButton_Click(object sender, EventArgs e) {
      var (hasRVA, value) = GetRVAValue();

      if (hasRVA) {
        SetRVAValue(Math.Max(0, value - 4));
      }
    }
  }

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

    public long Id { get; set; } // Used for MethodToken in managed code.
    public string Name { get; private set; }
    public List<SourceLineDebugInfo> SourceLines { get; set; }
    public long AuxiliaryId { get; set; } // Used for RejitID in managed code.
    public long RVA { get; set; }
    public uint Size { get; set; }
    public short OptimizationLevel { get; set; } // Used for OptimizationTier in managed code.
    public bool HasSourceLines => SourceLines is { Count: > 0 };
    public SourceLineDebugInfo FirstSourceLine => HasSourceLines ? SourceLines[0] : SourceLineDebugInfo.Unknown;
    public SourceLineDebugInfo LastSourceLine => HasSourceLines ? SourceLines[^1] : SourceLineDebugInfo.Unknown;

    //? TODO: Remove SourceFileName from SourceLineDebugInfo
    public string SourceFileName { get; set; }
    public string OriginalSourceFileName { get; set; }
    public long StartRVA => RVA;
    public long EndRVA => RVA + Size - 1;
    public bool IsUnknown => RVA == 0 && Size == 0;
    public bool IsPublic { get; set; }
    public bool IsSelected { get; set; }

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
             Size == other.Size &&
             Id == other.Id &&
             AuxiliaryId == other.AuxiliaryId;
    }
  }

  public struct SourceLineDebugInfo : IEquatable<SourceLineDebugInfo> {
    public int OffsetStart { get; set; } // Offset in bytes relative to function start.
    public int OffsetEnd { get; set; } // Offset in bytes relative to function start.
    public int Line { get; set; }
    public int Column { get; set; }
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
}