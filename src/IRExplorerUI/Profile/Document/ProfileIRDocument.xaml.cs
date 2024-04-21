using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading;
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
  private CancelableTaskInstance loadTask_;
  private SourceStackFrame inlinee_;
  private bool ignoreNextRowSelectedEvent_;
  private ProfileHistoryManager historyManager_;

  public ProfileIRDocument() {
    InitializeComponent();
    UpdateDocumentStyle();
    SetupEvents();
    ShowPerformanceCounterColumns = true;
    ShowPerformanceMetricColumns = true;
    DataContext = this;
    loadTask_ = new CancelableTaskInstance();
    profileFilter_ = new ProfileSampleFilter();
    historyManager_ = new ProfileHistoryManager(() =>
      new ProfileFunctionState(TextView.Section, TextView.Function,
      TextView.SectionText, profileFilter_), () => {
      FunctionHistoryChanged?.Invoke(this, EventArgs.Empty);
    });
  }

  private void SetupEvents() {
    TextView.TextArea.Caret.PositionChanged += Caret_PositionChanged;
    TextView.TextArea.TextView.ScrollOffsetChanged += TextViewOnScrollOffsetChanged;
    TextView.TextArea.SelectionChanged += TextAreaOnSelectionChanged;
    ProfileColumns.ScrollChanged += ProfileColumns_ScrollChanged;
    ProfileColumns.RowSelected += ProfileColumns_RowSelected;
    TextView.TextRegionFolded += TextViewOnTextRegionFolded;
    TextView.TextRegionUnfolded += TextViewOnTextRegionUnfolded;
    TextView.PreviewMouseDown += TextView_PreviewMouseDown;
    PreviewKeyDown += TextView_PreviewKeyDown;
    TextView.FunctionCallOpen += TextViewOnFunctionCallOpen;
  }

  private async void TextView_PreviewKeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Back) {
      if (Utils.IsKeyboardModifierActive()) {
        await LoadNextSection();
      }
      else {
        await LoadPreviousSection();
      }
    }
    else if (e.Key == Key.C && Utils.IsControlModifierActive()) {
      // Override Ctrl+C to copy instruction details instead of just text,
      // but not if Shift/Alt key is also pressed, copy plain text then.
      if (!Utils.IsAltModifierActive() &&
          !Utils.IsShiftModifierActive()) {
        await CopySelectedLinesAsHtml();
        e.Handled = true;
      }
    }
  }

  private async void TextView_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
    // Handle the back/forward mouse buttons
    // to navigate through the function history.
    if (e.ChangedButton == MouseButton.XButton1) {
      e.Handled = true;
      await LoadPreviousSection();
    }
    else if (e.ChangedButton == MouseButton.XButton2) {
      e.Handled = true;
      await LoadNextSection();
    }
  }

  public async Task LoadPreviousSection() {
    var state = historyManager_.PopPreviousState();

    if (state != null) {
      await LoadPreviousSectionState(state);
    }
  }

  public async Task LoadNextSection() {
    var state = historyManager_.PopNextState();

    if (state != null) {
      await LoadPreviousSectionState(state);
    }
  }

  private async Task LoadPreviousSectionState(ProfileFunctionState state) {
    await LoadSection(state.ParsedSection, state.ProfileFilter);
  }
  private async void TextViewOnFunctionCallOpen(object sender, IRTextSection targetSection) {
    var targetFunc = targetSection.ParentFunction;
    ProfileSampleFilter targetFilter = null;

    if (profileFilter_ is {IncludesAll: false}) {
      targetFilter = profileFilter_.CloneForCallTarget(targetFunc);
    }

    historyManager_.ClearNextStates(); // Reset forward history.
    var parsedSection = await Session.LoadAndParseSection(targetFunc.Sections[0]);

    if (parsedSection != null) {
      await LoadSection(parsedSection, targetFilter);
    }
  }

  public event EventHandler<string> TitlePrefixChanged;
  public event EventHandler<string> TitleSuffixChanged;
  public event EventHandler<string> DescriptionPrefixChanged;
  public event EventHandler<string> DescriptionSuffixChanged;
  public event EventHandler<ParsedIRTextSection> LoadedFunctionChanged;
  public event EventHandler<int> LineSelected;
  public event EventHandler FunctionHistoryChanged;
  public event PropertyChangedEventHandler PropertyChanged;

  public ISession Session { get; set; }
  public IRTextSection Section => TextView.Section;

  public ProfileSampleFilter ProfileFilter {
    get => profileFilter_;
    set {
      if (value == null) {
        profileFilter_ = new ProfileSampleFilter();
      }
      else {
        profileFilter_ = value.Clone(); // Clone to detect changes later.
      }

      UpdateProfileFilterUI();
      ProfilingUtils.SyncInstancesMenuWithFilter(InstancesMenu, profileFilter_);
      ProfilingUtils.SyncThreadsMenuWithFilter(ThreadsMenu, profileFilter_);
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

  public bool IsSourceFileDocument {
    get => isSourceFileDocument_;
    set => SetField(ref isSourceFileDocument_, value);
  }

  public Brush SelectedLineBrush {
    get => selectedLineBrush_;
    set => SetField(ref selectedLineBrush_, value);
  }

  public bool HasPreviousFunctions => historyManager_.HasPreviousStates;
  public bool HasNextFunctions => historyManager_.HasNextStates;

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
    using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
    IsSourceFileDocument = false;

    if (TextView.IsLoaded) {
      historyManager_.SaveCurrentState();
    }

    await TextView.LoadSection(parsedSection);

    // Apply profile filter if needed.
    ProfileFilter = profileFilter;
    bool success = true;

    if (!parsedSection.LoadFailed) {
      if (profileFilter is {IncludesAll: false}) {
        success = await LoadAssemblyProfileInstance(parsedSection);
      }
      else {
        success = await LoadAssemblyProfile(parsedSection);
      }
    }
    else {
      success = false;
    }

    if (!success) {
      await HideProfile();
      return false;
    }

    LoadedFunctionChanged?.Invoke(this, parsedSection);
    return true;
  }


  private async Task HideProfile() {
    HasProfileInfo = false;
    ColumnsVisible = false;
    ResetProfilingMenus();
  }

  private void ResetProfilingMenus() {
    DocumentUtils.RemoveNonDefaultMenuItems(ProfileElementsMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(InstancesMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(ThreadsMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(InlineesMenu);
  }

  private async Task<bool> LoadAssemblyProfile(ParsedIRTextSection parsedSection,
                                               bool reloadFilterMenus = true) {
    var funcProfile = Session.ProfileData?.GetFunctionProfile(parsedSection.Section.ParentFunction);

    if (funcProfile == null) {
      return false;
    }

    await MarkAssemblyProfile(parsedSection, funcProfile);
    CreateProfileElementMenus(funcProfile);

    if (reloadFilterMenus) {
      CreateProfileFilterMenus(parsedSection.Section, funcProfile);
    }

    return true;
  }

  private void CreateProfileElementMenus(FunctionProfileData funcProfile) {
    if (TextView.ProfileProcessingResult != null) {
      if (!isSourceFileDocument_) {
        var inlineeList = profileMarker_.GenerateInlineeList(TextView.ProfileProcessingResult);
        ProfilingUtils.CreateInlineesMenu(InlineesMenu, Section, inlineeList,
          funcProfile, InlineeMenuItem_OnClick, settings_, Session);
      }

      CreateProfileElementMenu(funcProfile, TextView.ProfileProcessingResult);
    }
  }

  private async Task<bool> LoadAssemblyProfileInstance(ParsedIRTextSection parsedSection,
                                                       bool reloadFilterMenus = true) {
    UpdateProfileFilterUI();
    var instanceProfile = await ComputeInstanceProfile();
    var funcProfile = instanceProfile.GetFunctionProfile(parsedSection.Section.ParentFunction);

    if (funcProfile == null) {
      return false;
    }

    await MarkAssemblyProfile(parsedSection, funcProfile);
    CreateProfileElementMenus(funcProfile);

    if (reloadFilterMenus) {
      CreateProfileFilterMenus(parsedSection.Section, funcProfile);
    }

    return true;
  }

  private async Task<ProfileData> ComputeInstanceProfile() {
    return await LongRunningAction.Start(
      async () => await Task.Run(() => Session.ProfileData.
        ComputeProfile(Session.ProfileData, profileFilter_, false)),
      TimeSpan.FromMilliseconds(500),
      "Filtering function instance", this, Session);
  }

  private async Task<bool> LoadSourceFileProfileInstance(IRTextSection section,
                                                         bool reloadFilterMenus = true) {
    UpdateProfileFilterUI();
    var instanceProfile = await ComputeInstanceProfile();
    var funcProfile = instanceProfile.GetFunctionProfile(section.ParentFunction);

    if (funcProfile == null) {
      return false;
    }

    if(!(await MarkSourceFileProfile(section, funcProfile))) {
      return false;
    }

    CreateProfileElementMenus(funcProfile);

    if (reloadFilterMenus) {
      CreateProfileFilterMenus(section, funcProfile);
    }

    return true;
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
    DescriptionPrefixChanged?.Invoke(this, ProfilingUtils.
      CreateProfileFunctionDescription(funcProfile, settings_.ProfileMarkerSettings, Session));
    TitlePrefixChanged?.Invoke(this, ProfilingUtils.
      CreateProfileFilterTitle(profileFilter_, Session));
    DescriptionSuffixChanged?.Invoke(this, ProfilingUtils.
      CreateProfileFilterDescription(profileFilter_, Session));
  }

  public bool HasProfileInstanceFilter => profileFilter_ is {HasInstanceFilter:true};
  public bool HasProfileThreadFilter => profileFilter_ is {HasThreadFilter:true};

  public async Task<bool> LoadSourceFile(SourceFileDebugInfo sourceInfo,
                                         IRTextSection section,
                                         ProfileSampleFilter profileFilter = null,
                                         SourceStackFrame inlinee = null) {
    try {
      using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
      IsSourceFileDocument = true;
      string text = await File.ReadAllTextAsync(sourceInfo.FilePath);
      SetSourceText(text, sourceInfo.FilePath);

      // Apply profile filter if needed.
      ProfileFilter = profileFilter;
      inlinee_ = inlinee;
      ignoreNextCaretEvent_ = true;
      bool success = true;

      if (profileFilter is {IncludesAll:false}) {
        success = await LoadSourceFileProfileInstance(section);
      }
      else {
        success = await LoadSourceFileProfile(section);
      }

      if (!success) {
        await HideProfile();
        return true; // Only profile part failed, keep text.
      }

      //? TODO: Is panel is not visible, scroll doesn't do anything,
      //? should be executed again when panel is activated.
      if (!settings_.ProfileMarkerSettings.JumpToHottestElement) {
        var (firstSourceLineIndex, lastSourceLineIndex) =
          await DocumentUtils.FindFunctionSourceLineRange(section.ParentFunction, TextView);

        if (firstSourceLineIndex != 0) {
          SelectLine(firstSourceLineIndex);
        }
      }

      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load source file {sourceInfo.FilePath}: {ex.Message}");
      return false;
    }
  }

  public async Task HandleMissingSourceFile(string failureText) {
    string text = "Failed to load source file.";

    if (!string.IsNullOrEmpty(failureText)) {
      text += $"\n{failureText}";
    }

    SetSourceText(text, "");
    await HideProfile();
  }

  private async Task<bool> LoadSourceFileProfile(IRTextSection section, bool reloadFilterMenus = true) {
    var funcProfile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

    if (funcProfile == null) {
      return false;
    }

    if (!(await MarkSourceFileProfile(section, funcProfile))) {
      return false;
    }

    CreateProfileElementMenus(funcProfile);

    if (reloadFilterMenus) {
      CreateProfileFilterMenus(section, funcProfile);
    }

    return true;
  }

  private void CreateProfileFilterMenus(IRTextSection section, FunctionProfileData funcProfile) {
    ProfilingUtils.CreateInstancesMenu(InstancesMenu, section, funcProfile,
                                      InstanceMenuItem_OnClick, settings_, Session);
    ProfilingUtils.CreateThreadsMenu(ThreadsMenu, section, funcProfile,
                                    ThreadMenuItem_OnClick, settings_, Session);
    ProfilingUtils.SyncInstancesMenuWithFilter(InstancesMenu, profileFilter_);
    ProfilingUtils.SyncThreadsMenuWithFilter(ThreadsMenu, profileFilter_);
  }

  private async Task<bool> MarkSourceFileProfile(IRTextSection section, FunctionProfileData funcProfile) {
    profileMarker_ = new ProfileDocumentMarker(funcProfile, Session.ProfileData,
                                               settings_.ProfileMarkerSettings,
                                               settings_.ColumnSettings,
                                               Session.CompilerInfo);
    // Accumulate the instruction weight for each source line.
    var sourceLineProfileResult = await Task.Run(async () => {
      var debugInfo = await Session.GetDebugInfoProvider(section.ParentFunction).ConfigureAwait(false);
      return funcProfile.ProcessSourceLines(debugInfo, Session.CompilerInfo.IR, inlinee_);
    });

    // Create a dummy FunctionIR that has fake tuples representing each
    // source line, with the profiling data attached to the tuples.
    var processingResult = profileMarker_.PrepareSourceLineProfile(funcProfile, TextView,
                                                                   sourceLineProfileResult);

    if (processingResult == null) {
      return false;
    }

    // Clear markers when switching between ASM/source modes.
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

    sourceLineProfileResult_ = sourceLineProfileResult;
    TextView.ResumeUpdate();

    if (settings_.ProfileMarkerSettings.JumpToHottestElement) {
      JumpToHottestProfiledElement(true);
    }

    UpdateProfileFilterUI();
    UpdateProfileDescription(funcProfile);
    await UpdateProfilingColumns();
    return true;
  }

  private async Task ApplyProfileFilter() {
    using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();

    if (isSourceFileDocument_) {
      if (profileFilter_ is {IncludesAll: false}) {
        await LoadSourceFileProfileInstance(TextView.Section, false);
      }
      else {
        await LoadSourceFileProfile(TextView.Section, false);
      }
    }
    else {
      var parsedSection = new ParsedIRTextSection(TextView.Section,
        TextView.SectionText,
        TextView.Function);

      if (profileFilter_ is {IncludesAll: false}) {
        await LoadAssemblyProfileInstance(parsedSection, false);
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

      ProfileColumns.Settings = settings_;
      ProfileColumns.ColumnSettings = settings_.ColumnSettings;

      await ProfileColumns.Display(sourceColumnData, TextView);
      profileMarker_.UpdateColumnStyles(sourceColumnData, TextView.Function, TextView);
      ProfileColumns.UpdateColumnWidths();

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
                                        FunctionProcessingResult result) {
    var list = new List<ProfileMenuItem>(result.SampledElements.Count);
    double maxWidth = 0;

    ProfileElementsMenu.Items.Clear();
    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings_.ProfileMarkerSettings;
    int order = 0;

    foreach (var (element, weight) in result.SampledElements) {
      double weightPercentage = funcProfile.ScaleWeight(weight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage)) {
        break;
      }

      string text = $"({markerSettings.FormatWeightValue(null, weight)})";
      string prefixText;

      if (isSourceFileDocument_) {
        prefixText = element.GetText(TextView.SectionText).ToString();
        prefixText = prefixText.Trim().TrimToLength(80);
      }
      else {
        prefixText = DocumentUtils.GenerateElementPreviewText(element, TextView.SectionText, 50);
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
      Utils.UpdateMaxMenuItemWidth(prefixText, ref maxWidth, ProfileElementsMenu);
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
      ignoreNextRowSelectedEvent_ = true;
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

    if (line != null) {
      LineSelected?.Invoke(this, line.LineNumber);
    }
  }

  public void SelectLine(int line) {
    ignoreNextCaretEvent_ = true;
    TextView.SelectLine(line);
  }

  public void Reset() {
    ResetProfilingMenus();
    TextView.UnloadDocument();
    ProfileColumns.Reset();
    sourceText_ = null;
    profileElements_ = null;
    sourceLineProfileResult_ = null;
    inlinee_ = null;
    ProfileFilter = new ProfileSampleFilter();
    historyManager_.Reset();
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
      TextView.FontSize = settings_.FontSize - 1;
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
    await CopySelectedLinesAsHtml();
  }

  public async Task CopySelectedLinesAsHtml() {
    if (isSourceFileDocument_) {
      await DocumentExporting.CopySelectedSourceLinesAsHtml(TextView);
    }
    else {
      await DocumentExporting.CopySelectedLinesAsHtml(TextView);
    }
  }

  private async void InstanceMenuItem_OnClick(object sender, RoutedEventArgs e) {
    ProfilingUtils.HandleInstanceMenuItemChanged(sender as MenuItem, InstancesMenu, profileFilter_);
    await ApplyProfileFilter();
  }

  private async void ThreadMenuItem_OnClick(object sender, RoutedEventArgs e) {
    ProfilingUtils.HandleThreadMenuItemChanged(sender as MenuItem, ThreadsMenu, profileFilter_);
    await ApplyProfileFilter();
  }


  private void ProfileColumns_RowSelected(object sender, int line) {
    if (ignoreNextRowSelectedEvent_) {
      ignoreNextRowSelectedEvent_ = false;
      return;
    }

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
    await ApplyProfileFilter();
  }

  private async void InlineeMenuItem_OnClick(object sender, RoutedEventArgs e) {
    var inlinee = ((MenuItem)sender)?.Tag as InlineeListItem;

    if (inlinee != null && inlinee.ElementWeights is {Count:>0}) {
      // Sort by weight and bring the hottest element into view.
      var elements = inlinee.SortedElements;
      TextView.SelectElements(elements);
      TextView.BringElementIntoView(elements[0]);
    }
  }

  private void CopySelectedTextExecuted(object sender, ExecutedRoutedEventArgs e) {
    TextView.Copy();
  }
}