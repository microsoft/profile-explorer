// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Dia2Lib;
using ProfileExplorer.Core;

namespace PDBViewer;

public partial class MainForm : Form {
  private IDiaDataSource diaSource_;
  private IDiaSession session_;
  private IDiaSymbol globalSymbol_;
  private List<FunctionDebugInfo> sortedFuncList_;
  private List<FunctionDebugInfo> filteredFuncList_;
  private string debugFilePath_;
  private SourceFileMapper sourceMapper_;
  private bool ignoredNextSourceCaretEvent_;
  private bool ignoreSourceLineSelectedEvent_;

  public MainForm() {
    InitializeComponent();
    FunctionListView.VirtualMode = true;
    FunctionListView.RetrieveVirtualItem += FunctionListViewOnRetrieveVirtualItem;
    FunctionListView.KeyDown += FunctionListViewOnKeyDown;
    sourceMapper_ = new SourceFileMapper();
  }

  private void FunctionListViewOnKeyDown(object? sender, KeyEventArgs e) {
    if (e.Control && e.KeyCode == Keys.C &&
        FunctionListView.SelectedIndices.Count == 1) {
      var item = filteredFuncList_[FunctionListView.SelectedIndices[0]];
      var sb = new StringBuilder();
      PrintFunctionDetails(item, sb);
      Clipboard.SetText(sb.ToString());
      e.Handled = true;
    }
  }

  private async void OpenButton_Click(object sender, EventArgs e) {
    if (OpenFileDialog.ShowDialog(this) == DialogResult.OK) {
      string? filePath = OpenFileDialog.FileName;
      await LoadDebugFile(filePath);
    }
  }

  private async Task LoadDebugFile(string filePath) {
    var sw = Stopwatch.StartNew();
    StatusLabel.Text = "Opening PDB";
    FunctionListView.Enabled = false;
    ProgressBar.Visible = true;
    Application.UseWaitCursor = true;

    if (!await OpenPDB(filePath)) {
      MessageBox.Show("Failed to open PDB", "PDB Viewer", MessageBoxButtons.OK, MessageBoxIcon.Error);
      ProgressBar.Visible = true;
      return;
    }

    StatusLabel.Text = "Enumerating functions";

    if (!await LoadFunctionList()) {
      MessageBox.Show("Failed to load function list from PDB", "PDB Viewer", MessageBoxButtons.OK,
                      MessageBoxIcon.Error);
      ProgressBar.Visible = false;
      return;
    }

    Text = $"PDB Viewer - {filePath}";
    StatusLabel.Text = $"PDB loaded";
    UpdateFunctionListView();
    ProgressBar.Visible = false;
    FunctionListView.Enabled = true;
    Application.UseWaitCursor = false;
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

    Regex nameRegex = null;

    if (filterName && RegexCheckbox.Checked) {
      try {
        nameRegex = new Regex(searchedText, RegexOptions.Compiled);
        StatusLabel.Text = "";
      }
      catch (Exception ex) {
        StatusLabel.Text = $"Invalid regex: {ex.Message}";
        return;
      }
    }

    foreach (var item in sortedFuncList_) {
      if (!showFuncts && !item.IsPublic) {
        continue;
      }

      if (!showPublics && item.IsPublic) {
        continue;
      }

      if (filterName) {
        string? funcName = GetFunctionName(item, demangle);

        if (nameRegex != null) {
          if (!nameRegex.IsMatch(funcName)) {
            continue;
          }
        }
        else if (!funcName.Contains(searchedText, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }
      }

      filteredFuncList_.Add(item);
    }

    for (int i = 1; i < filteredFuncList_.Count; i++) {
      if (filteredFuncList_[i].StartRVA == 0) {
        continue;
      }

      for (int k = i - 1; k >= 0 && i - k < 50; k--) {
        if (filteredFuncList_[k].StartRVA == 0) {
          continue;
        }

        if (filteredFuncList_[k].StartRVA <= filteredFuncList_[i].StartRVA &&
            filteredFuncList_[k].EndRVA > filteredFuncList_[i].EndRVA) {
          filteredFuncList_[i].HasOverlap = true;
          filteredFuncList_[k].HasOverlap = true;
        }
        else if (filteredFuncList_[k].EndRVA < filteredFuncList_[i].StartRVA) {
          break;
        }
      }
    }

    FunctionListView.VirtualListSize = filteredFuncList_.Count;

    if (filteredFuncList_.Count > 0) {
      FunctionListView.RedrawItems(0, filteredFuncList_.Count - 1, false);
    }

    SymbolCountLabel.Text = filteredFuncList_.Count.ToString();
    TotalSymbolCountLabel.Text = sortedFuncList_.Count.ToString();
  }

  private void UpdateSelectedRVAItem() {
    if (filteredFuncList_ == null) {
      return;
    }

    (bool hasRVA, long searchedRVA) = GetRVAValue();
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

    if (selectedIndex != -1 && filteredFuncList_.Count > 0) {
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

  private async Task<(SourceLineDebugInfo, IDiaSourceFile)> FindSourceLineByRVA(long rva) {
    return await Task.Run(() => {
      try {
        session_.findLinesByRVA((uint)rva, 0, out var lineEnum);

        while (true) {
          lineEnum.Next(1, out var lineNumber, out uint retrieved);

          if (retrieved == 0) {
            break;
          }

          var sourceFile = lineNumber.sourceFile;
          var sourceLine = new SourceLineDebugInfo((int)lineNumber.relativeVirtualAddress,
                                                   (int)lineNumber.lineNumber,
                                                   (int)lineNumber.columnNumber,
                                                   sourceFile.fileName);
          return (sourceLine, sourceFile);
        }
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed to get line for RVA {rva}: {ex.Message}");
      }

      return (SourceLineDebugInfo.Unknown, null);
    });
  }

  private async Task AnnotateSourceLines(FunctionDebugInfo func) {
    if (func.HasSourceLines) {
      return;
    }

    await Task.Run(() => {
      try {
        var funcSymbol = FindFunctionSymbol(func);
        session_.findLinesByRVA((uint)func.StartRVA, func.Size, out var lineEnum);

        while (true) {
          lineEnum.Next(1, out var lineNumber, out uint retrieved);

          if (retrieved == 0) {
            break;
          }

          uint lineRVA = lineNumber.relativeVirtualAddress;
          var sourceFile = lineNumber.sourceFile;
          var sourceLine = new SourceLineDebugInfo((int)lineRVA,
                                                   (int)lineNumber.lineNumber,
                                                   (int)lineNumber.columnNumber,
                                                   sourceFile.fileName);

          if (funcSymbol != null) {
            try {
              string? name = funcSymbol.name;

              // Enumerate the functions that got inlined at this call site.
              foreach (var inlinee in EnumerateInlinees(funcSymbol, lineRVA)) {
                if (string.IsNullOrEmpty(inlinee.FilePath)) {
                  // If the file name is not set, it means it's the same file
                  // as the function into which the inlining happened.
                  inlinee.FilePath = sourceFile.fileName;
                }

                sourceLine.AddInlinee(inlinee);
              }
            }
            catch (Exception ex) {
              Trace.TraceError($"Failed to get inlinees for RVA {func.StartRVA}: {ex.Message}");
            }
          }

          func.AddSourceLine(sourceLine);
        }
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed to get line for RVA {func.StartRVA}: {ex.Message}");
      }
    });
  }

  private IEnumerable<SourceStackFrame>
    EnumerateInlinees(IDiaSymbol funcSymbol, uint instrRVA) {
    funcSymbol.findInlineFramesByRVA(instrRVA, out var inlineeFrameEnum);

    foreach (IDiaSymbol inlineFrame in inlineeFrameEnum) {
      inlineFrame.findInlineeLinesByRVA(instrRVA, 0, out var inlineeLineEnum);

      while (true) {
        inlineeLineEnum.Next(1, out var inlineeLineNumber, out uint inlineeRetrieved);

        if (inlineeRetrieved == 0) {
          break;
        }

        // Getting the source file of the inlinee often fails, ignore it.
        string inlineeFileName = null;

        try {
          inlineeFileName = inlineeLineNumber.sourceFile.fileName;
        }
        catch {
          //? TODO: Any way to detect this and avoid throwing?
        }

        var inlinee = new SourceStackFrame(
          inlineFrame.name, inlineeFileName,
          (int)inlineeLineNumber.lineNumber,
          (int)inlineeLineNumber.columnNumber);
        yield return inlinee;
      }

      Marshal.ReleaseComObject(inlineeLineEnum);
    }

    Marshal.ReleaseComObject(inlineeFrameEnum);
  }

  private IDiaSymbol FindFunctionSymbol(FunctionDebugInfo func) {
    try {
      if (func.IsPublic) {
        session_.findSymbolByRVA((uint)func.StartRVA, SymTagEnum.SymTagPublicSymbol, out var pubSym);
        return pubSym;
      }
      else {
        session_.findSymbolByRVA((uint)func.StartRVA, SymTagEnum.SymTagFunction, out var funcSym);
        return funcSym;
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to find function symbol for RVA {func.StartRVA}: {ex.Message}");
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
    (bool hasRVA, long value) = GetRVAValue();

    if (hasRVA) {
      SetRVAValue(value + 4);
    }
  }

  private void SubtractRVAButton_Click(object sender, EventArgs e) {
    (bool hasRVA, long value) = GetRVAValue();

    if (hasRVA) {
      SetRVAValue(Math.Max(0, value - 4));
    }
  }

  private async void FunctionListView_SelectedIndexChanged(object sender, EventArgs e) {
    var currentFunc = GetSelectedFunction();

    if (currentFunc == null) {
      return;
    }

    UpdateOverlappingFunctions(currentFunc);
    await UpdateSelectedFunction(currentFunc);
  }

  private FunctionDebugInfo GetSelectedFunction() {
    if (FunctionListView.SelectedIndices.Count == 0 || filteredFuncList_ == null) {
      return null;
    }

    return filteredFuncList_[FunctionListView.SelectedIndices[0]];
  }

  private void UpdateOverlappingFunctions(FunctionDebugInfo currentItem) {
    int count = 0;

    foreach (var item in filteredFuncList_) {
      if (item != currentItem &&
          !(item.EndRVA < currentItem.StartRVA || item.StartRVA > currentItem.EndRVA)) {
        item.HasSelectionOverlap = true;
        count++;
      }
      else {
        item.HasSelectionOverlap = false;
      }
    }

    if (count > 0) {
      StatusLabel.Text = $"Overlapping: {count}";
    }
    else {
      StatusLabel.Text = "";
    }

    if (filteredFuncList_.Count > 0) {
      FunctionListView.RedrawItems(0, filteredFuncList_.Count - 1, false);
    }
  }

  private async Task UpdateSelectedFunction(FunctionDebugInfo func) {
    var sb = new StringBuilder();
    PrintFunctionDetails(func, sb, false);
    sb.AppendLine();
    await PrintFunctionFile(func, sb);

    sb.AppendLine();
    sb.AppendLine($"Name: {GetFunctionName(func, true)}");
    sb.AppendLine($"Mangled name: {func.Name}");
    FunctionDeteailsTextBox.Text = sb.ToString();

    await AnnotateSourceLines(func);

    if (func.HasSourceLines) {
      SourceLineListView.BeginUpdate();
      SourceLineListView.Items.Clear();

      foreach (var line in func.SourceLines) {
        var lvi = new ListViewItem();
        lvi.Text = $"{line.RVA:X}";
        lvi.SubItems.Add($"{line.Line}");
        lvi.SubItems.Add($"{line.Column}");
        lvi.SubItems.Add($"{line.InlineeCount}");
        lvi.SubItems.Add($"{line.FilePath}");
        lvi.Tag = line;
        SourceLineListView.Items.Add(lvi);
      }

      SourceLineListView.EndUpdate();
      await LoadSourceFile(func);
    }
  }

  private async Task LoadSourceFile(FunctionDebugInfo func, string sourceFile = null) {
    if (sourceFile == null) {
      sourceFile = sourceMapper_.Map(func.SourceFileName);
    }

    if (!File.Exists(sourceFile)) {
      SourceTextBox.Text = "Source file not found";
      SourceFileLabel.Text = "";
      return;
    }

    try {
      if (!string.IsNullOrEmpty(func.SourceFileName) && func.SourceFileName != sourceFile) {
        sourceMapper_.UpdateMap(func.SourceFileName, sourceFile);
      }

      SourceTextBox.Text = await File.ReadAllTextAsync(sourceFile);
      SourceFileLabel.Text = sourceFile;

      if (func.HasSourceLines) {
        foreach (var line in func.SourceLines) {
          HighlightSourceLine(SourceTextBox, line.Line - 1, Color.Bisque);
        }
      }
    }
    catch (Exception ex) {
      SourceTextBox.Text = $"Failed to load source file: {ex.Message}";
      SourceFileLabel.Text = "";
    }
  }

  private async Task PrintFunctionFile(FunctionDebugInfo item, StringBuilder sb) {
    var (sourceLine, sourceFile) = await FindSourceLineByRVA(item.StartRVA);

    if (sourceLine.IsUnknown) {
      sb.AppendLine("Missing source line info");
    }

    sb.AppendLine($"Source File: {sourceLine.FilePath}");
    sb.AppendLine($"Source Line: {sourceLine.Line} (column {sourceLine.Column})");
  }

  private void SearchResetButton_Click(object sender, EventArgs e) {
    SearchTextbox.Text = "";
    StatusLabel.Text = "";
  }

  private void SourceLineListView_SelectedIndexChanged(object sender, EventArgs e) {
    if (ignoreSourceLineSelectedEvent_ ||
        SourceLineListView.SelectedItems.Count == 0) {
      return;
    }

    var item = SourceLineListView.SelectedItems[0];
    var line = item.Tag as SourceLineDebugInfo;

    if (line == null) {
      return;
    }

    InlineeListView.BeginUpdate();
    InlineeListView.Items.Clear();

    if (line.InlineeCount > 0) {
      foreach (var inlinee in line.Inlinees) {
        var lvi = new ListViewItem();
        lvi.Text = $"{inlinee.Function}";
        lvi.SubItems.Add($"{inlinee.Line}");
        lvi.SubItems.Add($"{inlinee.Column}");
        lvi.SubItems.Add(inlinee.FilePath);
        lvi.Tag = inlinee;
        InlineeListView.Items.Add(lvi);
      }
    }

    InlineeListView.EndUpdate();
    SelectSourceLine(SourceTextBox, line.Line - 1);
  }

  private void HighlightSourceLine(RichTextBox richTextBox, int lineNumber, Color color) {
    if (lineNumber < 0 || lineNumber >= richTextBox.Lines.Length) {
      return;
    }

    ignoredNextSourceCaretEvent_ = true;
    int start = richTextBox.GetFirstCharIndexFromLine(lineNumber);
    int length = richTextBox.Lines[lineNumber].Length;
    richTextBox.Select(start, length);
    richTextBox.SelectionBackColor = color;
    richTextBox.SelectionLength = 0;
  }

  private void SelectSourceLine(RichTextBox richTextBox, int lineNumber) {
    if (lineNumber < 0 || lineNumber >= richTextBox.Lines.Length) {
      return;
    }

    ignoredNextSourceCaretEvent_ = true;
    int start = richTextBox.GetFirstCharIndexFromLine(lineNumber);
    int length = richTextBox.Lines[lineNumber].Length;
    richTextBox.Select(start, length);
    richTextBox.ScrollToCaret();
  }

  private async void SourceOpenButton_Click(object sender, EventArgs e) {
    var currentFunc = GetSelectedFunction();

    if (currentFunc != null &&
        SourceOpenFileDialog.ShowDialog(this) == DialogResult.OK) {
      await LoadSourceFile(currentFunc, SourceOpenFileDialog.FileName);
    }
  }

  private void SourceTextBox_SelectionChanged(object sender, EventArgs e) {
    if (ignoredNextSourceCaretEvent_) {
      ignoredNextSourceCaretEvent_ = false;
      return;
    }

    var currentFunc = GetSelectedFunction();

    if (currentFunc == null || !currentFunc.HasSourceLines) {
      return;
    }

    ignoreSourceLineSelectedEvent_ = true;
    SourceLineListView.SelectedIndices.Clear();
    int lineIndex = SourceTextBox.GetLineFromCharIndex(SourceTextBox.SelectionStart);
    int firstIndex = -1;

    for (int i = 0; i < currentFunc.SourceLines.Count; i++) {
      if (currentFunc.SourceLines[i].Line == lineIndex + 1) {
        SourceLineListView.SelectedIndices.Add(i);
        firstIndex = i;
      }
    }

    ignoreSourceLineSelectedEvent_ = false;
    SourceLineListView.Focus();

    if (firstIndex != -1) {
      SourceLineListView.EnsureVisible(firstIndex);
    }
  }

  private async void MainForm_Load(object sender, EventArgs e) {
    string[]? args = Environment.GetCommandLineArgs();

    if (args.Length < 2) {
      return;
    }

    string? file = args[1];

    if (File.Exists(file)) {
      await LoadDebugFile(file);
    }
  }

  private void RegexCheckbox_CheckedChanged(object sender, EventArgs e) {
    UpdateFunctionListView();
  }

  private void SourceTextBox_VScroll(object sender, EventArgs e) {
    LineNumbersTextBox.Text = "";
    int firstVisibleLine = SourceTextBox.GetLineFromCharIndex(SourceTextBox.GetCharIndexFromPosition(new Point(0, 0)));
    int lastVisibleLine =
      SourceTextBox.GetLineFromCharIndex(SourceTextBox.GetCharIndexFromPosition(new Point(0, SourceTextBox.Height))) +
      1;

    for (int i = firstVisibleLine; i <= lastVisibleLine; i++) {
      LineNumbersTextBox.AppendText($"{i + 1}: \n");
    }

    LineNumbersTextBox.SelectAll();
    LineNumbersTextBox.SelectionAlignment = HorizontalAlignment.Right;
    LineNumbersTextBox.DeselectAll();

    // Synchronize scroll position.
    LineNumbersTextBox.Select(SourceTextBox.GetCharIndexFromPosition(new Point(0, 0)), 0);
    LineNumbersTextBox.ScrollToCaret();
  }

  private void SourceTextBox_TextChanged(object sender, EventArgs e) {
    SourceTextBox_VScroll(sender, e);
  }

  private void LineNumbersTextBox_MouseDown(object sender, MouseEventArgs e) {
    SourceTextBox.Focus();
  }

  private void PrintFunctionDetails(FunctionDebugInfo item, StringBuilder sb, bool includeName = true) {
    sb.AppendLine($"Start RVA: 0x{item.RVA:X} ({item.RVA})");
    sb.AppendLine($"End RVA: 0x{item.EndRVA:X} ({item.EndRVA})");
    sb.AppendLine($"Length: {item.Size} ({item.Size:X})");
    sb.AppendLine($"Kind: {(item.IsPublic ? "Public" : "Function")}");

    if (includeName) {
      sb.AppendLine();
      sb.AppendLine($"Name: {GetFunctionName(item, true)}");
      sb.AppendLine();
      sb.AppendLine($"Mangled name: {item.Name}");
    }
  }

  private void FunctionListViewOnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e) {
    if (filteredFuncList_ != null && e.ItemIndex < filteredFuncList_.Count) {
      var item = filteredFuncList_[e.ItemIndex];
      var lvi = new ListViewItem();
      lvi.Text = $"{item.RVA:X}";
      lvi.SubItems.Add(new ListViewItem.ListViewSubItem(lvi, GetFunctionName(item, DemangleCheckbox.Checked)));
      lvi.SubItems.Add(new ListViewItem.ListViewSubItem(lvi, $"{item.Size} ({item.Size:X})"));
      lvi.SubItems.Add(new ListViewItem.ListViewSubItem(lvi, $"{item.EndRVA:X}"));
      lvi.SubItems.Add(new ListViewItem.ListViewSubItem(lvi, item.IsPublic ? "Public" : "Function"));
      lvi.SubItems.Add(new ListViewItem.ListViewSubItem(lvi, item.Name));

      if (item.HasSelectionOverlap) {
        lvi.BackColor = Color.Pink;
      }
      else if (item.IsSelected) {
        lvi.BackColor = Color.LightBlue;
      }
      else if (item.HasOverlap) {
        lvi.BackColor = Color.PaleGoldenrod;
      }

      e.Item = lvi;
    }
  }

  private void AboutButton_Click(object sender, EventArgs e) {
    var window = new AboutBox();
    window.ShowDialog(this);
  }
}