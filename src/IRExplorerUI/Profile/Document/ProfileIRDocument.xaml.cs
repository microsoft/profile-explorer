using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClosedXML.Excel;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers;

namespace IRExplorerUI.Profile.Document;

public partial class ProfileIRDocument : UserControl, INotifyPropertyChanged {
  private List<(IRElement, TimeSpan)> profileElements_;
  private int profileElementIndex_;
  private FunctionProcessingResult sourceProfileResult_;
  private IRDocumentColumnData sourceColumnData_;
  private bool columnsVisible_;
  private bool ignoreNextCaretEvent_;
  private bool disableCaretEvent_;
  private int firstSourceLineIndex_;
  private int lastSourceLineIndex_;
  private ReadOnlyMemory<char> sourceText_;
  private bool hasProfileInfo_;
  private bool useCompactMode_;
  private Brush selectedLineBrush_;

  public ProfileIRDocument() {
    InitializeComponent();
    UpdateDocumentStyle();
    DataContext = this;

    SetupEvents();
  }

  private void SetupEvents() {
    TextView.TextArea.Caret.PositionChanged += Caret_PositionChanged;
    TextView.TextArea.TextView.ScrollOffsetChanged += TextViewOnScrollOffsetChanged;
    ProfileColumns.ScrollChanged += ProfileColumns_ScrollChanged;
    TextView.TextRegionFolded += TextViewOnTextRegionFolded;
    TextView.TextRegionUnfolded += TextViewOnTextRegionUnfolded;
  }

  private void TextViewOnTextRegionUnfolded(object sender, FoldingSection e) {
    ProfileColumns.HandleTextRegionUnfolded(e);
  }

  private void TextViewOnTextRegionFolded(object sender, FoldingSection e) {
    ProfileColumns.HandleTextRegionFolded(e);
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public ISession Session { get; set; }
  public IRDocument AssociatedDocument { get; set; }

  public bool HasProfileInfo {
    get => hasProfileInfo_;
    set => SetField(ref hasProfileInfo_, value);
  }

  public bool UseCompactMode {
    get => useCompactMode_;
    set => SetField(ref useCompactMode_, value);
  }

  public bool ColumnsVisible {
    get => columnsVisible_;
    set => SetField(ref columnsVisible_, value);
  }

  public Brush SelectedLineBrush {
    get => selectedLineBrush_;
    set => SetField(ref selectedLineBrush_, value);
  }

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
    if (EqualityComparer<T>.Default.Equals(field, value)) return false;
    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }

  public async Task<bool> LoadSection(ParsedIRTextSection parsedSection) {
    TextView.Initalize(App.Settings.DocumentSettings, Session);
    TextView.EarlyLoadSectionSetup(parsedSection);
    await TextView.LoadSection(parsedSection);

    //? TODO: UI option?
    if (true) {
      await ShowProfilingColumns();
    }

    return true;
  }

  public async Task<bool> LoadSourceFile(SourceFileDebugInfo sourceInfo,
                                         IRTextSection section,
                                         IDebugInfoProvider debugInfo) {
    try {
      string text = await File.ReadAllTextAsync(sourceInfo.FilePath);
      SetSourceText(text, sourceInfo.FilePath);
      await AnnotateSourceFileProfilerData(section, debugInfo);
      
      //? TODO: Is panel is not visible, scroll doesn't do anything,
      //? should be executed again when panel is activated
      TextView.ScrollToLine(sourceInfo.StartLine);
      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load source file {sourceInfo.FilePath}: {ex.Message}");
      return false;
    }
  }

  public void HandleMissingSourceFile(string failureText) {
    string text = "Failed to load source file.";

    if (!string.IsNullOrEmpty(failureText)) {
      text += $"\n{failureText}";
    }

    SetSourceText(text, "");
  }
  
  private async Task AnnotateSourceFileProfilerData(IRTextSection section,
                                                    IDebugInfoProvider debugInfo) {
    var funcProfile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

    if (funcProfile == null) {
      return;
    }

    //? TODO: Check if it's still the case
    //? Accessing the PDB (DIA) from another thread fails.
    //var result = await Task.Run(() => profile.ProcessSourceLines(debugInfo));
    var profileOptions = ProfileDocumentMarkerSettings.Default;
    var profileMarker =
      new ProfileDocumentMarker(funcProfile, Session.ProfileData, profileOptions, Session.CompilerInfo);
    var processingResult = profileMarker.PrepareSourceLineProfile(funcProfile, TextView, debugInfo);

    if (processingResult == null) {
      return;
    }

    // 
    if (TextView.IsLoaded) {
      TextView.ClearInstructionMarkers();
    }

    var dummyParsedSection = new ParsedIRTextSection(section, sourceText_, processingResult.Function);
    TextView.EarlyLoadSectionSetup(dummyParsedSection);
    await TextView.LoadSection(dummyParsedSection);

    TextView.SuspendUpdate();
    await profileMarker.MarkSourceLines(TextView, processingResult);

    //? TODO: UI option?
    if (true) {
      // Annotate call sites next to source lines by parsing the actual section
      // and mapping back the call sites to the dummy elements representing the source lines.
      var parsedSection = await Task.Run(() => Session.LoadAndParseSection(section));

      if (parsedSection != null) {
        profileMarker.MarkCallSites(TextView, parsedSection.Function,
                                    section.ParentFunction, processingResult);
      }
    }

    TextView.ResumeUpdate();
    sourceProfileResult_ = processingResult.Result;

    //? TODO: UI Option?
    await ShowProfilingColumns();
  }

  public async Task ShowProfilingColumns() {
    sourceColumnData_ = TextView.ProfileColumnData;
    ColumnsVisible = sourceColumnData_ != null && sourceColumnData_.HasData;

    if (ColumnsVisible) {
      if (UseCompactMode) {
        // Use compact mode that shows only the time column.
        if (sourceColumnData_.GetColumn(ProfileDocumentMarker.TIME_COLUMN) is var timeColumn) {
          timeColumn.Style.ShowMainColumnIcon = false;
        }

        if (sourceColumnData_.GetColumn(ProfileDocumentMarker.TIME_PERCENTAGE_COLUMN) is var timePercColumn) {
          timePercColumn.IsVisible = false;
        }
      }

      await ProfileColumns.Display(sourceColumnData_, TextView);
      profileElements_ = TextView.ProfileProcessingResult.SampledElements;
      UpdateHighlighting();
    }
    else {
      ProfileColumns.Reset();
    }

    HasProfileInfo = true;
  }

  private async void ExportFunctionProfileExecuted(object sender, ExecutedRoutedEventArgs e) {
    string path = Utils.ShowSaveFileDialog("Excel Worksheets|*.xlsx", "*.xlsx|All Files|*.*");

    if (!string.IsNullOrEmpty(path)) {
      try {
        await ExportFunctionAsExcelFile(path);
      }
      catch (Exception ex) {
        Utils.ShowErrorMessageBox($"Failed to save source profiling results to {path}: {ex.Message}", this);
      }
    }
  }

  public void SetSourceText(string text, string filePath) {
    disableCaretEvent_ = true; // Changing the text triggers the caret event twice.
    IHighlightingDefinition highlightingDef = null;

    switch (Utils.GetFileExtension(filePath)) {
      case ".c":
      case ".cpp":
      case ".cxx":
      case ".cc":
      case ".h":
      case ".hpp":
      case ".hxx": {
        highlightingDef = HighlightingManager.Instance.GetDefinition("C++");
        break;
      }
      case ".cs": {
        highlightingDef = HighlightingManager.Instance.GetDefinition("C#");
        break;
      }
    }

    TextView.SyntaxHighlighting = highlightingDef;
    TextView.Text = text;
    sourceText_ = text.AsMemory();
    disableCaretEvent_ = false;
  }

  private void ProfileColumns_ScrollChanged(object sender, ScrollChangedEventArgs e) {
    if (Math.Abs(e.VerticalChange) < double.Epsilon) {
      return;
    }

    TextView.ScrollToVerticalOffset(e.VerticalOffset);
  }

  private void UpdateDocumentStyle() {
    var settings = App.Settings.DocumentSettings;
    TextView.Background = ColorBrushes.GetBrush(settings.BackgroundColor);
    TextView.Foreground = ColorBrushes.GetBrush(settings.TextColor);
    TextView.FontFamily = new FontFamily(settings.FontName);
    TextView.FontSize = settings.FontSize;
  }

  private void JumpToProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!HasProfileElements()) {
      return;
    }

    profileElementIndex_ = 0;
    JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
  }

  private bool HasProfileElements() {
    return ColumnsVisible && profileElements_ != null && profileElements_.Count > 0;
  }

  private bool HasProfileElement(int offset) {
    return ColumnsVisible && profileElements_ != null &&
           profileElementIndex_ + offset >= 0 &&
           profileElementIndex_ + offset < profileElements_.Count;
  }

  private void JumpToNextProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToProfiledElement(-1);
  }

  private void JumpToPreviousProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToProfiledElement(1);
  }

  public void JumpToHottestProfiledElement() {
    Dispatcher.BeginInvoke(() => JumpToProfiledElement(0), DispatcherPriority.Background);
  }

  private void JumpToProfiledElement(int offset) {
    if (!HasProfileElement(offset)) {
      return;
    }

    profileElementIndex_ += offset;
    JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
  }

  private void JumpToProfiledElement(IRElement element) {
    TextView.ScrollToLine(element.TextLocation.Line);
    double offset = TextView.TextArea.TextView.VerticalOffset;
    SyncColumnsVerticalScrollOffset(offset);
  }

  private void JumpToNextProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = HasProfileElement(-1);
  }

  private void JumpToPreviousProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = HasProfileElement(1);
  }

  private void Caret_PositionChanged(object sender, EventArgs e) {
    if (columnsVisible_) {
      var line = TextView.Document.GetLineByOffset(TextView.TextArea.Caret.Offset);
      ProfileColumns.SelectRow(line.LineNumber - 1);
    }

    if (ignoreNextCaretEvent_) {
      ignoreNextCaretEvent_ = false;
      return;
    }

    if (disableCaretEvent_) {
      return;
    }

    HighlightElementsOnSelectedLine();
  }

  private void HighlightElementsOnSelectedLine() {
    var line = TextView.Document.GetLineByOffset(TextView.CaretOffset);

    if (line != null && AssociatedDocument != null) {
      AssociatedDocument.SelectElementsOnSourceLine(line.LineNumber, null);
    }
  }

  public void SelectLine(int line) {
    if (line <= 0 || line > TextView.Document.LineCount) {
      return;
    }

    var documentLine = TextView.Document.GetLineByNumber(line);
    ignoreNextCaretEvent_ = true;
    TextView.CaretOffset = documentLine.Offset;
    TextView.ScrollToLine(line);
  }

  public void Reset() {
    TextView.UnloadDocument();
    ProfileColumns.Reset();
    sourceText_ = null;
    profileElements_ = null;
    sourceProfileResult_ = null;
    sourceColumnData_ = null;
  }
  
  private void TextViewOnScrollOffsetChanged(object? sender, EventArgs e) {
    // Sync scrolling with the optional columns.
    double offset = TextView.TextArea.TextView.VerticalOffset;
    SyncColumnsVerticalScrollOffset(offset);
  }

  private void SyncColumnsVerticalScrollOffset(double offset) {
    // Sync scrolling with the optional columns.
    if (columnsVisible_) {
      ProfileColumns.ScrollToVerticalOffset(offset);
    }
  }

  private void UpdateHighlighting() {
    TextView.TextArea.TextView.Redraw();
  }

  private async Task ExportFunctionAsExcelFile(string filePath) {
    var function = TextView.Section.ParentFunction;
    var debugInfo = await Session.GetDebugInfoProvider(function);
    var funcProfile = Session.ProfileData?.GetFunctionProfile(function);

    if (debugInfo == null || funcProfile == null) {
      return;
    }

    //? TODO: Fix end
    //? TODO: Used only for Excel exporting, do it only then
    if (debugInfo.PopulateSourceLines(funcProfile.FunctionDebugInfo)) {
      firstSourceLineIndex_ = funcProfile.FunctionDebugInfo.StartSourceLine.Line;
      lastSourceLineIndex_ = firstSourceLineIndex_;
    }

    var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Source");
    var columnData = sourceColumnData_;
    int rowId = 2; // First row is for the table column names.
    int maxColumn = 2 + (columnData != null ? columnData.Columns.Count : 0);
    int maxLineLength = 0;

    for (int i = firstSourceLineIndex_; i <= lastSourceLineIndex_; i++) {
      var line = TextView.Document.GetLineByNumber(i);
      string text = TextView.Document.GetText(line.Offset, line.Length);
      ws.Cell(rowId, 1).Value = text;
      ws.Cell(rowId, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
      maxLineLength = Math.Max(text.Length, maxLineLength);

      ws.Cell(rowId, 2).Value = i;
      ws.Cell(rowId, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
      ws.Cell(rowId, 2).Style.Font.FontColor = XLColor.DarkGreen;

      if (columnData != null) {
        IRElement tuple = null;
        tuple = FindTupleOnSourceLine(i);

        if (tuple != null) {
          IRDocumentColumnData.ExportColumnsToExcel(columnData, tuple, ws, rowId, 3);
        }
      }

      rowId++;
    }

    var firstCell = ws.Cell(1, 1);
    var lastCell = ws.LastCellUsed();
    var range = ws.Range(firstCell.Address, lastCell.Address);
    var table = range.CreateTable();
    table.Theme = XLTableTheme.None;

    foreach (var cell in table.HeadersRow().Cells()) {
      if (cell.Address.ColumnNumber == 1) {
        cell.Value = "Source";
      }
      else if (cell.Address.ColumnNumber == 2) {
        cell.Value = "Line";
      }
      else if (columnData != null && cell.Address.ColumnNumber - 3 < columnData.Columns.Count) {
        cell.Value = columnData.Columns[cell.Address.ColumnNumber - 3].Title;
      }

      cell.Style.Font.Bold = true;
      cell.Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    for (int i = 1; i <= 1; i++) {
      ws.Column(i).AdjustToContents((double)1, maxLineLength);
    }

    wb.SaveAs(filePath);
  }

  private IRElement FindTupleOnSourceLine(int line) {
    var pair1 = sourceProfileResult_.SampledElements.Find(e => e.Item1.TextLocation.Line == line - 1);

    if (pair1.Item1 != null) {
      return pair1.Item1;
    }

    var pair2 = sourceProfileResult_.CounterElements.Find(e => e.Item1.TextLocation.Line == line - 1);
    return pair2.Item1;
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }
}
