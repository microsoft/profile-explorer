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
using IRExplorerUI.Document;

namespace IRExplorerUI.Profile.Document;


public class ProfileMenuItem : BindableObject {
  private Thickness borderThickness_;
  private Brush borderBrush_;
  private string text_;
  private string prefixText_;
  private double minTextWidth_;
  private string toolTip_;
  private Brush textColor_;
  private Brush backColor_;
  private bool showPercentageBar_;
  private Brush percentageBarBackColor__;
  private double percentageBarBorderThickness_;
  private Brush percentageBarBorderBrush_;
  private FontWeight textWeight_;
  private double textSize_;
  private FontFamily textFont_;

  public ProfileMenuItem(string text, long value = 0, double valueValuePercentage = 0.0) {
    Text = text;
    Value = value;
    ValuePercentage = valueValuePercentage;
    TextWeight = FontWeights.Normal;
    TextColor = Brushes.Black;
  }

  public IRElement Element { get; set; }
  public long Value { get; set; }
  public double ValuePercentage { get; set; }

  public Thickness BorderThickness {
    get => borderThickness_;
    set => SetAndNotify(ref borderThickness_, value);
  }

  public Brush BorderBrush {
    get => borderBrush_;
    set => SetAndNotify(ref borderBrush_, value);
  }

  public string Text {
    get => text_;
    set => SetAndNotify(ref text_, value);
  }

  public string PrefixText {
    get => prefixText_;
    set => SetAndNotify(ref prefixText_, value);
  }

  public double MinTextWidth {
    get => minTextWidth_;
    set => SetAndNotify(ref minTextWidth_, value);
  }

  public string ToolTip {
    get => toolTip_;
    set => SetAndNotify(ref toolTip_, value);
  }

  public Brush TextColor {
    get => textColor_;
    set => SetAndNotify(ref textColor_, value);
  }

  public Brush BackColor {
    get => backColor_;
    set => SetAndNotify(ref backColor_, value);
  }

  public bool ShowPercentageBar {
    get => showPercentageBar_;
    set => SetAndNotify(ref showPercentageBar_, value);
  }

  public Brush PercentageBarBackColor {
    get => percentageBarBackColor__;
    set => SetAndNotify(ref percentageBarBackColor__, value);
  }

  public double PercentageBarBorderThickness {
    get => percentageBarBorderThickness_;
    set => SetAndNotify(ref percentageBarBorderThickness_, value);
  }

  public Brush PercentageBarBorderBrush {
    get => percentageBarBorderBrush_;
    set => SetAndNotify(ref percentageBarBorderBrush_, value);
  }

  public FontWeight TextWeight {
    get => textWeight_;
    set => SetAndNotify(ref textWeight_, value);
  }

  public double TextSize {
    get => textSize_;
    set => SetAndNotify(ref textSize_, value);
  }

  public FontFamily TextFont {
    get => textFont_;
    set => SetAndNotify(ref textFont_, value);
  }
}

public partial class ProfileIRDocument : UserControl, INotifyPropertyChanged {
  private List<(IRElement, TimeSpan)> profileElements_;
  private int profileElementIndex_;
  private FunctionProcessingResult sourceProfileResult_;
  private SourceLineProcessingResult sourceLineProfileResult_;
  private IRDocumentColumnData sourceColumnData_;
  private bool columnsVisible_;
  private bool ignoreNextCaretEvent_;
  private bool disableCaretEvent_;
  private ReadOnlyMemory<char> sourceText_;
  private bool hasProfileInfo_;
  private Brush selectedLineBrush_;
  private TextViewSettingsBase settings_;
  private ProfileDocumentMarker profileMarker_;
  private bool isPreviewDocument_;

  public ProfileIRDocument() {
    InitializeComponent();
    UpdateDocumentStyle();
    SetupEvents();
    ShowPerformanceCounterColumns = true;
    ShowPerformanceMetricColumns = true;
    DataContext = this;
  }

  private void SetupEvents() {
    TextView.TextArea.Caret.PositionChanged += Caret_PositionChanged;
    TextView.TextArea.TextView.ScrollOffsetChanged += TextViewOnScrollOffsetChanged;
    TextView.TextArea.SelectionChanged += TextAreaOnSelectionChanged;
    ProfileColumns.ScrollChanged += ProfileColumns_ScrollChanged;
    ProfileColumns.RowSelected += ProfileColumns_RowSelected;
    TextView.TextRegionFolded += TextViewOnTextRegionFolded;
    TextView.TextRegionUnfolded += TextViewOnTextRegionUnfolded;
  }

  private void ProfileColumns_RowSelected(object sender, int line) {
    TextView.SelectLine(line + 1);
  }

  private void TextAreaOnSelectionChanged(object sender, EventArgs e) {
    // For source files, compute the sum of the selected lines time.
    if(sourceLineProfileResult_ == null) {
      return;
    }

    int startLine = TextView.TextArea.Selection.StartPosition.Line;
    int endLine = TextView.TextArea.Selection.EndPosition.Line;
    var weightSum = TimeSpan.Zero;

    for(int i = startLine; i<= endLine; i++) {
      if(sourceLineProfileResult_.SourceLineWeight.TryGetValue(i, out var weight)) {
        weightSum += weight;
      }
    }

    if(weightSum == TimeSpan.Zero) {
      Session.SetApplicationStatus("");
      return;
    }

    var funcProfile = Session.ProfileData.GetFunctionProfile(TextView.Section.ParentFunction);
    double weightPercentage = funcProfile.ScaleWeight(weightSum);
    string text = $"{weightPercentage.AsPercentageString()} ({weightSum.AsMillisecondsString()})";
    Session.SetApplicationStatus(text, "Sum of time for the selected lines");
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

  public bool UseCompactProfilingColumns { get; set; }
  public bool ShowPerformanceCounterColumns { get; set; }
  public bool ShowPerformanceMetricColumns { get; set; }

  public bool UseSmallerFontSize {
    get => ProfileColumns.UseSmallerFontSize;
    set => ProfileColumns.UseSmallerFontSize = value;
  }

  public bool ColumnsVisible {
    get => columnsVisible_;
    set => SetField(ref columnsVisible_, value);
  }

  public bool IsPreviewDocument {
    get => isPreviewDocument_;
    set => SetField(ref isPreviewDocument_, value);
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
    TextView.Initialize(App.Settings.DocumentSettings, Session);
    TextView.EarlyLoadSectionSetup(parsedSection);
    await TextView.LoadSection(parsedSection);

    var debugInfo = await Session.GetDebugInfoProvider(parsedSection.Section.ParentFunction);
    await AnnotateAssemblyProfile(parsedSection, debugInfo);
    return true;
  }

  private async Task AnnotateAssemblyProfile(ParsedIRTextSection parsedSection,
                                             IDebugInfoProvider debugInfo) {
    var funcProfile = Session.ProfileData?.GetFunctionProfile(parsedSection.Section.ParentFunction);

    if (funcProfile == null) {
      return;
    }

    profileMarker_ = new ProfileDocumentMarker(funcProfile, Session.ProfileData,
                                               settings_.ProfileMarkerSettings,
                                               settings_.ColumnSettings, Session.CompilerInfo);
    await profileMarker_.Mark(TextView, parsedSection.Function,
                              parsedSection.Section.ParentFunction);
    await UpdateProfilingColumns();

    if (TextView.ProfileProcessingResult != null) {
      CreateProfileElementMenu(funcProfile, TextView.ProfileProcessingResult, true);
    }
  }

  public async Task<bool> LoadSourceFile(SourceFileDebugInfo sourceInfo,
                                         IRTextSection section,
                                         IDebugInfoProvider debugInfo) {
    try {
      string text = await File.ReadAllTextAsync(sourceInfo.FilePath);
      SetSourceText(text, sourceInfo.FilePath);
      await AnnotateSourceFileProfile(section, debugInfo);

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

  private async Task AnnotateSourceFileProfile(IRTextSection section,
                                               IDebugInfoProvider debugInfo) {
    var funcProfile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

    if (funcProfile == null) {
      return;
    }

    //? TODO: Check if it's still the case
    //? Accessing the PDB (DIA) from another thread fails.
    //var result = await Task.Run(() => profile.ProcessSourceLines(debugInfo));
    profileMarker_ =
      new ProfileDocumentMarker(funcProfile, Session.ProfileData,
      settings_.ProfileMarkerSettings, settings_.ColumnSettings,
      Session.CompilerInfo);
    var processingResult = profileMarker_.PrepareSourceLineProfile(funcProfile, TextView, debugInfo);

    if (processingResult == null) {
      return;
    }

    if (TextView.IsLoaded) {
      TextView.ClearInstructionMarkers();
    }

    var dummyParsedSection = new ParsedIRTextSection(section, sourceText_, processingResult.Function);
    TextView.EarlyLoadSectionSetup(dummyParsedSection);
    await TextView.LoadSection(dummyParsedSection);

    TextView.SuspendUpdate();
    await profileMarker_.MarkSourceLines(TextView, processingResult);

    // Annotate call sites next to source lines by parsing the actual section
    // and mapping back the call sites to the dummy elements representing the source lines.
    var parsedSection = await Session.LoadAndParseSection(section);

    if (parsedSection != null) {
      profileMarker_.MarkCallSites(TextView, parsedSection.Function,
                                   section.ParentFunction, processingResult);
    }

    TextView.ResumeUpdate();
    sourceProfileResult_ = processingResult.Result;
    sourceLineProfileResult_ = processingResult.SourceLineResult;
    await UpdateProfilingColumns();

    if (TextView.ProfileProcessingResult != null) {
      CreateProfileElementMenu(funcProfile, TextView.ProfileProcessingResult, false);
    }
  }

  public async Task UpdateProfilingColumns() {
    sourceColumnData_ = TextView.ProfileColumnData;
    ColumnsVisible = sourceColumnData_ is {HasData: true};

    if (ColumnsVisible) {
      if (UseCompactProfilingColumns) {
        // Use compact mode that shows only the time column.
        if (sourceColumnData_.GetColumn(ProfileDocumentMarker.TimeColumnDefinition) is var timeColumn) {
          timeColumn.Style.ShowIcon = OptionalColumnStyle.PartVisibility.Never;
        }

        if (sourceColumnData_.GetColumn(ProfileDocumentMarker.TimePercentageColumnDefinition) is var timePercColumn) {
          timePercColumn.IsVisible = false;
        }
      }

      // Hide perf counter columns.
      if (!ShowPerformanceCounterColumns ||
          !ShowPerformanceMetricColumns) {
        foreach (var column in sourceColumnData_.Columns) {
          if ((!ShowPerformanceCounterColumns && column.IsPerformanceCounter) ||
              (!ShowPerformanceMetricColumns && column.IsPerformanceMetric)) {
            column.IsVisible = false;
          }
        }
      }

      ProfileColumns.UseSmallerFontSize = UseSmallerFontSize;
      ProfileColumns.Settings = settings_;
      ProfileColumns.ColumnSettings = settings_.ColumnSettings;

      profileMarker_.UpdateColumnStyles(sourceColumnData_, TextView.Function, TextView);
      await ProfileColumns.Display(sourceColumnData_, TextView);

      profileElements_ = TextView.ProfileProcessingResult.SampledElements;
      UpdateHighlighting();

      ProfileColumns.BuildColumnsVisibilityMenu(sourceColumnData_, ProfileColumnsMenu, async () => {
        await UpdateProfilingColumns();
      });
    }
    else {
      ProfileColumns.Reset();
    }

    HasProfileInfo = true;
  }

  private void CreateProfileElementMenu(FunctionProfileData funcProfile,
                                        FunctionProcessingResult result,
                                        bool isAssemblyView) {
    var list = new List<ProfileMenuItem>(result.SampledElements.Count);
    double maxWidth = 0;

    ProfileElementsMenu.Items.Clear();
    var valueTemplate = (DataTemplate)Application.Current.FindResource("BlockPercentageValueTemplate");
    var markerSettings = settings_.ProfileMarkerSettings;
    int order = 0;

    foreach (var (element, weight) in result.SampledElements) {
      double weightPercentage = funcProfile.ScaleWeight(weight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage)) {
        break;
      }

      string text = $"({markerSettings.FormatWeightValue(null, weight)})";
      string prefixText;

      if (isAssemblyView) {
        prefixText = DocumentUtils.GenerateElementPreviewText(element, TextView.SectionText, 50);
      }
      else {
        prefixText = element.GetText(TextView.SectionText).ToString();
        prefixText = prefixText.Trim().TrimToLength(50);
      }

      var value = new ProfileMenuItem(text, weight.Ticks, weightPercentage) {
        Element = element,
        PrefixText = prefixText,
        ToolTip = $"Line {element.TextLocation.Line + 1}",
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
      };

      var item = new MenuItem {
        Header = value,
        Tag = list.Count,
        HeaderTemplate = valueTemplate
      };

      item.Click += (sender, args) => {
        var menuItem = (MenuItem)sender;
        JumpToProfiledElementAt((int)menuItem.Tag);
      };

      ProfileElementsMenu.Items.Add(item);

      // Make sure percentage rects are aligned.
      double width = Utils.MeasureString(prefixText, settings_.FontName, settings_.FontSize).Width;
      maxWidth = Math.Max(width, maxWidth);
      list.Add(value);
    }

    foreach (var value in list) {
      value.MinTextWidth = maxWidth;
    }
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

    switch (Utils.GetFileExtension(filePath).ToLowerInvariant()) {
      case ".c":
      case ".cpp":
      case ".cxx":
      case ".c++":
      case ".cc":
      case ".cp":
      case ".h":
      case ".hh":
      case ".hpp":
      case ".hxx":
      case ".inl":
      case ".ixx": {
        highlightingDef = HighlightingManager.Instance.GetDefinition("C++");
        break;
      }
      case ".cs": {
        highlightingDef = HighlightingManager.Instance.GetDefinition("C#");
        break;
      }
      case ".rs": {
        //? TODO: Rust syntax highlighting
        highlightingDef = HighlightingManager.Instance.GetDefinition("C++");
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

  public void JumpToHottestProfiledElement(bool onLoad = false) {
    Dispatcher.BeginInvoke(() => {
      if (onLoad) {
        // Don't select the associated document instructions
        // when jumping during the source file load.
        ignoreNextCaretEvent_ = true;
      }

      JumpToProfiledElement(0);
    }, DispatcherPriority.ContextIdle);
  }

  private void JumpToProfiledElement(int offset) {
    if (!HasProfileElement(offset)) {
      return;
    }

    profileElementIndex_ += offset;
    JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
  }

  private void JumpToProfiledElement(IRElement element) {
    TextView.SetCaretAtElement(element);
    double offset = TextView.TextArea.TextView.VerticalOffset;
    SyncColumnsVerticalScrollOffset(offset);
  }

  private void JumpToProfiledElementAt(int index) {
    profileElementIndex_ = index;
    JumpToProfiledElement(0);
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
    sourceLineProfileResult_ = null;
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
    var (firstSourceLineIndex, lastSourceLineIndex) = await FindFunctionSourceLineRange(function);

    if (firstSourceLineIndex == 0) {
      Utils.ShowWarningMessageBox("Failed to export source file", this);
      return;
    }

    var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Source");
    var columnData = sourceColumnData_;
    int rowId = 2; // First row is for the table column names.
    int maxLineLength = 0;

    for (int i = firstSourceLineIndex; i <= lastSourceLineIndex; i++) {
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
          columnData.ExportColumnsToExcel(tuple, ws, rowId, 3);
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

  public async Task<(int, int)> FindFunctionSourceLineRange(IRTextFunction function) {
    var debugInfo = await Session.GetDebugInfoProvider(function);
    var funcProfile = Session.ProfileData?.GetFunctionProfile(function);

    if (debugInfo == null || funcProfile == null) {
      return (0, 0);
    }

    int firstSourceLineIndex = 0;
    int lastSourceLineIndex = 0;

    if (debugInfo.PopulateSourceLines(funcProfile.FunctionDebugInfo)) {
      firstSourceLineIndex = funcProfile.FunctionDebugInfo.FirstSourceLine.Line;
      lastSourceLineIndex = funcProfile.FunctionDebugInfo.LastSourceLine.Line;
    }

    return (firstSourceLineIndex, lastSourceLineIndex);
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

  public async Task ReloadSettings() {
    Initialize(settings_);
    await UpdateProfilingColumns();
  }

  public void Initialize(TextViewSettingsBase settings) {
    settings_ = settings;
    ProfileViewMenu.DataContext = settings_.ColumnSettings;
    TextView.FontFamily = new FontFamily(settings_.FontName);

    if (UseSmallerFontSize) {
      TextView.FontSize = settings_.FontSize - 2;
    }
    else {
      TextView.FontSize = settings_.FontSize;
    }
  }

  private async void ViewMenuItem_OnCheckedChanged(object sender, RoutedEventArgs e) {
    await UpdateProfilingColumns();
  }
}