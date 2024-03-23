using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClosedXML.Excel;
using HtmlAgilityPack;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerUI.Compilers;
using IRExplorerUI.Document;
using Microsoft.Extensions.Primitives;

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
  private SourceLineProcessingResult sourceLineProfileResult_;
  private bool columnsVisible_;
  private bool ignoreNextCaretEvent_;
  private bool disableCaretEvent_;
  private ReadOnlyMemory<char> sourceText_;
  private bool hasProfileInfo_;
  private Brush selectedLineBrush_;
  private TextViewSettingsBase settings_;
  private ProfileDocumentMarker profileMarker_;
  private bool isPreviewDocument_;
  private bool isSourceFileDocument_;
  private bool suspendColumnVisibilityHandler_;
  private ProfileSampleFilter profileFilter_;

  public ProfileIRDocument() {
    InitializeComponent();
    UpdateDocumentStyle();
    SetupEvents();
    ShowPerformanceCounterColumns = true;
    ShowPerformanceMetricColumns = true;
    DataContext = this;
    profileFilter_ = new ProfileSampleFilter();
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

  
  public event EventHandler<string> TitlePrefixChanged;
  public event EventHandler<string> TitleSuffixChanged;
  public event EventHandler<string> DescriptionPrefixChanged;
  public event EventHandler<string> DescriptionSuffixChanged;
  public event PropertyChangedEventHandler PropertyChanged;
  
  public ISession Session { get; set; }
  public IRDocument AssociatedDocument { get; set; }
  public IRTextSection Section => TextView.Section;

  public ProfileSampleFilter ProfileFilter {
    get => profileFilter_;
    set {
      profileFilter_ = value;
      DocumentUtils.SyncInstancesMenuWithFilter(InstancesMenu, value);
      DocumentUtils.SyncThreadsMenuWithFilter(ThreadsMenu, value);
    }
  }

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

  public async Task<bool> LoadSection(ParsedIRTextSection parsedSection,
                                      ProfileSampleFilter profileFilter = null) {
    isSourceFileDocument_ = false;
    await TextView.LoadSection(parsedSection);

    if (profileFilter != null) {
      ProfileFilter = profileFilter;
      await LoadAssemblyProfileInstance(parsedSection);
    }
    else {
      await LoadAssemblyProfile(parsedSection);
    }

    return true;
  }

  private async Task LoadAssemblyProfile(ParsedIRTextSection parsedSection,
                                         bool reloadFilterMenus = true) {
    var funcProfile = Session.ProfileData?.GetFunctionProfile(parsedSection.Section.ParentFunction);

    if (funcProfile == null) {
      return;
    }

    await MarkAssemblyProfile(parsedSection, funcProfile);

    if (reloadFilterMenus) {
      DocumentUtils.CreateInstancesMenu(InstancesMenu, parsedSection.Section, funcProfile,
                                        InstanceMenuItem_OnClick, settings_, Session);
      DocumentUtils.CreateThreadsMenu(ThreadsMenu, parsedSection.Section, funcProfile,
                                      ThreadMenuItem_OnClick, settings_, Session);
    }

    if (TextView.ProfileProcessingResult != null) {
      CreateProfileElementMenu(funcProfile, TextView.ProfileProcessingResult, true);
    }
  }

  private async Task LoadAssemblyProfileInstance(ParsedIRTextSection parsedSection) {
    UpdateProfileFilterUI();
    var instanceProfile = await Task.Run(
      () => Session.ProfileData.ComputeProfile(Session.ProfileData, profileFilter_, false));
    var funcProfile = instanceProfile.GetFunctionProfile(parsedSection.Section.ParentFunction);

    if (funcProfile == null) {
      return;
    }

    await MarkAssemblyProfile(parsedSection, funcProfile);
  }

  private async Task LoadSourceFileProfileInstance(IRTextSection section) {
    UpdateProfileFilterUI();
    var instanceProfile = await Task.Run(
      () => Session.ProfileData.ComputeProfile(Session.ProfileData, profileFilter_, false));
    var funcProfile = instanceProfile.GetFunctionProfile(section.ParentFunction);

    if (funcProfile == null) {
      return;
    }

    await MarkSourceFileProfile(section, funcProfile);
  }

  private async Task MarkAssemblyProfile(ParsedIRTextSection parsedSection, FunctionProfileData funcProfile) {
    profileMarker_ = new ProfileDocumentMarker(funcProfile, Session.ProfileData,
                                               settings_.ProfileMarkerSettings,
                                               settings_.ColumnSettings, Session.CompilerInfo);
    await profileMarker_.Mark(TextView, parsedSection.Function,
                              parsedSection.Section.ParentFunction);

    if (settings_.ProfileMarkerSettings.JumpToHottestElement) {
      JumpToHottestProfiledElement(true);
    }

    UpdateProfileFilterUI();
    UpdateProfileDescription(funcProfile);
    await UpdateProfilingColumns();
  }

  private void UpdateProfileFilterUI() {
    OnPropertyChanged(nameof(HasProfileInstanceFilter));
    OnPropertyChanged(nameof(HasProfileThreadFilter));
  }

  private void UpdateProfileDescription(FunctionProfileData funcProfile) {
    DescriptionPrefixChanged?.Invoke(this, DocumentUtils.
      GenerateProfileFunctionDescription(funcProfile, settings_.ProfileMarkerSettings, Session));
    TitlePrefixChanged?.Invoke(this, DocumentUtils.
      GenerateProfileFilterTitle(profileFilter_, Session));
    DescriptionSuffixChanged?.Invoke(this, DocumentUtils.
      GenerateProfileFilterDescription(profileFilter_, Session));
  }

  public bool HasProfileInstanceFilter {
    get => profileFilter_ is {HasInstanceFilter:true};
  }

  public bool HasProfileThreadFilter {
    get => profileFilter_ is {HasThreadFilter:true};
  }


  public async Task<bool> LoadSourceFile(SourceFileDebugInfo sourceInfo,
                                         IRTextSection section,
                                         ProfileSampleFilter profileFilter = null) {
    try {
      isSourceFileDocument_ = true;
      string text = await File.ReadAllTextAsync(sourceInfo.FilePath);
      SetSourceText(text, sourceInfo.FilePath);

      //? TODO: Is panel is not visible, scroll doesn't do anything,
      //? should be executed again when panel is activated

      var (firstSourceLineIndex, lastSourceLineIndex) =
        await DocumentUtils.FindFunctionSourceLineRange(section.ParentFunction, Session);

      if (firstSourceLineIndex != 0) {
        SelectLine(firstSourceLineIndex);
      }

      if (profileFilter != null) {
        ProfileFilter = profileFilter;
        await LoadSourceFileProfileInstance(section);
      }
      else {
        await LoadSourceFileProfile(section);
      }
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

  private async Task LoadSourceFileProfile(IRTextSection section, bool reloadFilterMenus = true) {
    var funcProfile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

    if (funcProfile == null) {
      return;
    }

    if (!(await MarkSourceFileProfile(section, funcProfile))) {
      return;
    }

    if (reloadFilterMenus) {
      DocumentUtils.CreateInstancesMenu(InstancesMenu, section, funcProfile,
                                        InstanceMenuItem_OnClick, settings_, Session);
      DocumentUtils.CreateThreadsMenu(ThreadsMenu, section, funcProfile,
                                      ThreadMenuItem_OnClick, settings_, Session);
    }

    if (TextView.ProfileProcessingResult != null) {
      CreateProfileElementMenu(funcProfile, TextView.ProfileProcessingResult, false);
    }
  }

  private async Task<bool> MarkSourceFileProfile(IRTextSection section, FunctionProfileData funcProfile) {
    profileMarker_ = new ProfileDocumentMarker(funcProfile, Session.ProfileData,
                                               settings_.ProfileMarkerSettings,
                                               settings_.ColumnSettings,
                                               Session.CompilerInfo);
    // Accumulate the instruction weight for each source line.
    var sourceLineProfileResult = await Task.Run(async () => {
      var debugInfo = await Session.GetDebugInfoProvider(section.ParentFunction);
      return funcProfile.ProcessSourceLines(debugInfo, Session.CompilerInfo.IR);
    });

    // Create a dummy FunctionIR that has fake tuples representing each
    // source line, with the profiling data attached to the tuples.
    var processingResult = profileMarker_.PrepareSourceLineProfile(funcProfile, TextView, sourceLineProfileResult);

    if (processingResult == null) {
      return false;
    }

    if (TextView.IsLoaded) {
      TextView.ClearInstructionMarkers();
    }

    var dummyParsedSection = new ParsedIRTextSection(section, sourceText_, processingResult.Function);
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

    if (settings_.ProfileMarkerSettings.JumpToHottestElement) {
      JumpToHottestProfiledElement(true);
    }

    UpdateProfileFilterUI();
    UpdateProfileDescription(funcProfile);
    await UpdateProfilingColumns();
    sourceLineProfileResult_ = sourceLineProfileResult;
    return true;
  }

  private async Task ApplyInstanceFilter(ProfileSampleFilter instanceFilter) {
      if (isSourceFileDocument_) {
        if (instanceFilter is {IncludesAll: false}) {
          await LoadSourceFileProfileInstance(TextView.Section);
        }
        else {
          await LoadSourceFileProfile(TextView.Section, false);
        }
      }
      else {
        var parsedSection = new ParsedIRTextSection(TextView.Section,
                                                    TextView.SectionText,
                                                    TextView.Function);
        if (instanceFilter is {IncludesAll: false}) {
          await LoadAssemblyProfileInstance(parsedSection);
        }
        else {
          await LoadAssemblyProfile(parsedSection, false);
        }
      } 
  }

  public async Task UpdateProfilingColumns() {
    var sourceColumnData = TextView.ProfileColumnData;
    ColumnsVisible = sourceColumnData is {HasData: true};

    if (ColumnsVisible) {
      if (UseCompactProfilingColumns) {
        // Use compact mode that shows only the time column.
        if (sourceColumnData.GetColumn(ProfileDocumentMarker.TimeColumnDefinition) is var timeColumn) {
          timeColumn.Style.ShowIcon = OptionalColumnStyle.PartVisibility.Never;
        }

        if (sourceColumnData.GetColumn(ProfileDocumentMarker.TimePercentageColumnDefinition) is var timePercColumn) {
          timePercColumn.IsVisible = false;
        }
      }

      // Hide perf counter columns.
      if (!ShowPerformanceCounterColumns ||
          !ShowPerformanceMetricColumns) {
        foreach (var column in sourceColumnData.Columns) {
          if ((!ShowPerformanceCounterColumns && column.IsPerformanceCounter) ||
              (!ShowPerformanceMetricColumns && column.IsPerformanceMetric)) {
            column.IsVisible = false;
          }
        }
      }

      ProfileColumns.UseSmallerFontSize = UseSmallerFontSize;
      ProfileColumns.Settings = settings_;
      ProfileColumns.ColumnSettings = settings_.ColumnSettings;

      profileMarker_.UpdateColumnStyles(sourceColumnData, TextView.Function, TextView);
      await ProfileColumns.Display(sourceColumnData, TextView);

      profileElements_ = TextView.ProfileProcessingResult.SampledElements;
      UpdateHighlighting();

      // Add the columns to the View menu.
      // While doing that, disable handling the MenuItem Checked event,
      // it gets triggered when the MenuItems are temporarily removed.
      suspendColumnVisibilityHandler_ = true;
      ProfileColumns.BuildColumnsVisibilityMenu(sourceColumnData, ProfileViewMenu, async () => {
        await UpdateProfilingColumns();
      });
      suspendColumnVisibilityHandler_ = false;
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
    sourceLineProfileResult_ = null;
    ProfileFilter = new ProfileSampleFilter();
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
    TextView.Initialize(settings, Session);
    TextView.FontFamily = new FontFamily(settings_.FontName);

    if (UseSmallerFontSize) {
      TextView.FontSize = settings_.FontSize - 2;
    }
    else {
      TextView.FontSize = settings_.FontSize;
    }
  }

  private async void ViewMenuItem_OnCheckedChanged(object sender, RoutedEventArgs e) {
    if (!suspendColumnVisibilityHandler_) {
      await UpdateProfilingColumns();
    }
  }

  private async void ExportFunctionProfileHtmlExecuted(object sender, ExecutedRoutedEventArgs e) {
    string path = Utils.ShowSaveFileDialog("HTML file|*.html", "*.html|All Files|*.*");
    bool success = true;

    if (!string.IsNullOrEmpty(path)) {
      try {
        success = await DocumentExporting.ExportSourceAsHtmlFile(TextView, path);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save function to {path}: {ex.Message}");
      }

      if (!success) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Failed to save list to {path}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  private async void ExportSourceExecuted(object sender, ExecutedRoutedEventArgs e) {
    await DocumentExporting.ExportToExcelFile(TextView,
                                              isSourceFileDocument_ ?
                                              DocumentExporting.ExportSourceAsExcelFile :
                                              DocumentExporting.ExportFunctionAsExcelFile);
  }

  private async void ExportSourceHtmlExecuted(object sender, ExecutedRoutedEventArgs e) {
    await DocumentExporting.ExportToHtmlFile(TextView,
                                             isSourceFileDocument_ ?
                                             DocumentExporting.ExportSourceAsHtmlFile :
                                             DocumentExporting.ExportFunctionAsHtmlFile);
  }

  private async void ExportSourceMarkdownExecuted(object sender, ExecutedRoutedEventArgs e) {
    await DocumentExporting.ExportToMarkdownFile(TextView,
                                                 isSourceFileDocument_ ?
                                                 DocumentExporting.ExportSourceAsMarkdownFile :
                                                 DocumentExporting.ExportFunctionAsMarkdownFile);
  }

  private async void CopySelectedLinesAsHtmlExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (isSourceFileDocument_) {
      await DocumentExporting.CopySelectedSourceLinesAsHtml(TextView);
    }
    else {
      await DocumentExporting.CopySelectedLinesAsHtml(TextView);
    }
  }

  private async void InstanceMenuItem_OnClick(object sender, RoutedEventArgs e) {
    await DocumentUtils.HandleInstanceMenuItemChanged(sender as MenuItem, InstancesMenu, profileFilter_);
    await ApplyInstanceFilter(profileFilter_);
  }

  private async void ThreadMenuItem_OnClick(object sender, RoutedEventArgs e) {
    await DocumentUtils.HandleThreadMenuItemChanged(sender as MenuItem, ThreadsMenu, profileFilter_);
    await ApplyInstanceFilter(profileFilter_);
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

  public async Task SwitchProfileInstanceAsync(ProfileSampleFilter instanceFilter) {
    ProfileFilter = instanceFilter;
    DocumentUtils.SyncInstancesMenuWithFilter(InstancesMenu, instanceFilter);
    DocumentUtils.SyncThreadsMenuWithFilter(ThreadsMenu, instanceFilter);
    await ApplyInstanceFilter(instanceFilter);
  }
}