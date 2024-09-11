// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Document;
using ProfileExplorer.UI.OptionsPanels;
using ProfileExplorer.UI.Profile;
using ProfileExplorer.UI.Profile.Document;
using ProfileExplorer.UI.Query;
using ProfileExplorer.UI.Utilities;
using ProtoBuf;

namespace ProfileExplorer.UI;

public static class DocumentHostCommand {
  public static readonly RoutedUICommand ShowSearch = new("Untitled", "ShowSearch", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ToggleSearch = new("Untitled", "ToggleSearch", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ShowSectionList = new("Untitled", "ShowSectionList", typeof(IRDocumentHost));
  public static readonly RoutedUICommand PreviousSection = new("Untitled", "PreviousSection", typeof(IRDocumentHost));
  public static readonly RoutedUICommand NextSection = new("Untitled", "NextSection", typeof(IRDocumentHost));
  public static readonly RoutedUICommand SearchSymbol = new("Untitled", "SearchSymbol", typeof(IRDocumentHost));
  public static readonly RoutedUICommand SearchSymbolAllSections =
    new("Untitled", "SearchSymbolAllSections", typeof(IRDocumentHost));
  public static readonly RoutedUICommand JumpToProfiledElement =
    new("Untitled", "JumpToProfiledElement", typeof(IRDocumentHost));
  public static readonly RoutedUICommand JumpToNextProfiledElement =
    new("Untitled", "JumpToNextProfiledElement", typeof(IRDocumentHost));
  public static readonly RoutedUICommand JumpToPreviousProfiledElement =
    new("Untitled", "JumpToPreviousProfiledElement", typeof(IRDocumentHost));
  public static readonly RoutedUICommand JumpToNextProfiledBlock =
    new("Untitled", "JumpToNextProfiledBlock", typeof(IRDocumentHost));
  public static readonly RoutedUICommand JumpToPreviousProfiledBlock =
    new("Untitled", "JumpToPreviousProfiledBlock", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ExportFunctionProfile =
    new("Untitled", "ExportFunctionProfile", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ExportFunctionProfileHTML =
    new("Untitled", "ExportFunctionProfileHTML", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ExportFunctionProfileMarkdown =
    new("Untitled", "ExportFunctionProfileMarkdown", typeof(IRDocumentHost));
  public static readonly RoutedUICommand CopySelectedLinesAsHTML =
    new("Untitled", "CopySelectedLinesAsHTML", typeof(IRDocumentHost));
  public static readonly RoutedUICommand CopySelectedText = new("Untitled", "CopySelectedText", typeof(IRDocumentHost));
}

[ProtoContract]
public class IRDocumentHostState {
  [ProtoMember(1)]
  public IRDocumentState DocumentState;
  [ProtoMember(2)]
  public double HorizontalOffset;
  [ProtoMember(3)]
  public double VerticalOffset;
  public bool HasAnnotations => DocumentState.HasAnnotations;
}

public partial class IRDocumentHost : UserControl, INotifyPropertyChanged {
  private const double ActionPanelInitialOpacity = 0.5;
  private const int ActionPanelHeight = 20;
  private const double AnimationDuration = 0.1;
  private const int ActionPanelOffset = 15;
  private bool actionPanelHovered_;
  private bool actionPanelFromClick_;
  private bool actionPanelVisible_;
  private bool duringSwitchSearchResults_;
  private IRElement hoveredElement_;
  private Point hoverPoint_;
  private bool optionsPanelVisible_;
  private bool remarkOptionsPanelVisible_;
  private IRElement remarkElement_;
  private IRElement selectedElement_;
  private RemarkSettings remarkSettings_;
  private RemarkPreviewPanel remarkPanel_;
  private Point remarkPanelLocation_;
  private CancelableTaskInstance loadTask_;
  private bool remarkPanelVisible_;
  private bool searchPanelVisible_;
  private SectionSearchResult searchResult_;
  private IRElement selectedBlock_;
  private ISession session_;
  private DocumentSettings settings_;
  private List<Remark> remarkList_;
  private RemarkContext activeRemarkContext_;
  private List<QueryPanel> activeQueryPanels_;
  private QueryValue mainQueryInputValue_;
  private bool pasOutputVisible_;
  private bool columnsVisible_;
  private bool duringSectionSwitching_;
  private double previousVerticalOffset_;
  private List<(IRElement, TimeSpan)> profileElements_;
  private List<(BlockIR, TimeSpan)> profileBlocks_;
  private int profileElementIndex_;
  private int profileBlockIndex_;
  private OptionsPanelHostPopup remarkOptionsPanelPopup_;
  private OptionsPanelHostPopup optionsPanelPopup_;
  private DocumentOptionsPanel optionsPanel_;
  private DelayedAction delayedHideActionPanel_;
  private bool profileVisible_;
  private double columnsListItemHeight_;
  private ProfileDocumentMarker profileMarker_;
  private ProfileSampleFilter profileFilter_;
  private FunctionProfileData funcProfile_;
  private bool ignoreNextRowSelectedEvent_;
  private bool ignoreNextSaveFunctionState_;
  private ProfileHistoryManager historyManager_;
  private MenuItem[] viewMenuItems_;

  public IRDocumentHost(ISession session) {
    InitializeComponent();
    DataContext = this;
    PassOutput.DataContext = this;
    ActionPanel.Visibility = Visibility.Collapsed;

    Session = session;
    remarkSettings_ = App.Settings.RemarkSettings;
    Settings = App.Settings.DocumentSettings;
    TextView.Initialize(Settings, Session);

    // Initialize pass output panel.
    PassOutput.Session = session;
    PassOutput.HasPinButton = false;
    PassOutput.HasDuplicateButton = false;
    PassOutput.DiffModeButtonVisible = false;
    PassOutput.SectionNameVisible = false;

    SetupEvents();
    var hover = new MouseHoverLogic(this);
    hover.MouseHover += Hover_MouseHover;
    loadTask_ = new CancelableTaskInstance(false, Session.SessionState.RegisterCancelableTask,
                                           Session.SessionState.UnregisterCancelableTask);
    activeQueryPanels_ = new List<QueryPanel>();
    profileFilter_ = new ProfileSampleFilter();
    historyManager_ = new ProfileHistoryManager(() => {
      var state = new ProfileFunctionState(TextView.Section, TextView.Function,
                                           TextView.SectionText, profileFilter_);

      if (funcProfile_ != null) {
        state.Weight = funcProfile_.Weight;
      }

      return state;
    }, () => {
      UpdateHistoryMenu();
    });

    viewMenuItems_ = new[] {
      ViewMenuItem1,
      ViewMenuItem2,
      ViewMenuItem3
    };
  }

  public string TitlePrefix { get; set; }
  public string TitleSuffix { get; set; }
  public string DescriptionPrefix { get; set; }
  public string DescriptionSuffix { get; set; }

  public double ColumnsListItemHeight {
    get => columnsListItemHeight_;
    set {
      if (Math.Abs(columnsListItemHeight_ - value) > double.Epsilon) {
        columnsListItemHeight_ = value;
        NotifyPropertyChanged(nameof(ColumnsListItemHeight));
      }
    }
  }

  public ISession Session {
    get => session_;
    private set => session_ = value;
  }

  public DocumentSettings Settings {
    get => settings_;
    set {
      settings_ = value;
      ProfileColumns.Settings = value;
      ProfileColumns.ColumnSettings = value.ColumnSettings;
      ProfileViewMenu.DataContext = value.ColumnSettings;
    }
  }

  public RemarkSettings RemarkSettings {
    get => remarkSettings_;
    set {
      if (!value.Equals(remarkSettings_)) {
        remarkSettings_ = value.Clone();

        NotifyPropertyChanged(nameof(ShowRemarks));
        NotifyPropertyChanged(nameof(ShowPreviousSections));
      }
    }
  }

  public bool ShowRemarks {
    get => remarkSettings_.ShowRemarks;
    set {
      if (value != remarkSettings_.ShowRemarks) {
        remarkSettings_.ShowRemarks = value;
        NotifyPropertyChanged(nameof(ShowRemarks));
        HandleRemarkSettingsChange();
      }
    }
  }

  public bool HasRemarks => remarkList_ is {Count: > 0};

  public bool ShowPreviousSections {
    get => ShowRemarks && remarkSettings_.ShowPreviousSections;
    set {
      if (value != remarkSettings_.ShowPreviousSections) {
        remarkSettings_.ShowPreviousSections = value;
        NotifyPropertyChanged(nameof(ShowPreviousSections));
        HandleRemarkSettingsChange();
      }
    }
  }

  public IRTextSection Section => TextView.Section;
  public FunctionIR Function => TextView.Function;

  public bool PassOutputVisible {
    get => pasOutputVisible_;
    set {
      if (pasOutputVisible_ != value) {
        if (!pasOutputVisible_) {
          PassOutput.SwitchSection(Section, TextView);
        }

        pasOutputVisible_ = value;
        NotifyPropertyChanged(nameof(PassOutputVisible));
        PassOutputVisibilityChanged?.Invoke(this, value);
      }
    }
  }

  public bool ColumnsVisible {
    get => columnsVisible_;
    set {
      if (columnsVisible_ != value) {
        columnsVisible_ = value;
        NotifyPropertyChanged(nameof(ColumnsVisible));
      }
    }
  }

  public bool ProfileVisible {
    get => profileVisible_;
    set {
      if (profileVisible_ != value) {
        profileVisible_ = value;
        NotifyPropertyChanged(nameof(ProfileVisible));
      }
    }
  }

  public bool HasPreviousFunctions => historyManager_.HasPreviousStates;
  public bool HasNextFunctions => historyManager_.HasNextStates;
  public bool HasProfileInstanceFilter => profileFilter_ is {HasInstanceFilter: true};
  public bool HasProfileThreadFilter => profileFilter_ is {HasThreadFilter: true};
  public RelayCommand<object> CopyDocumentCommand => new(async obj => {
    await DocumentExporting.CopyAllLinesAsHtml(TextView);
  });
  public event PropertyChangedEventHandler PropertyChanged;

  private void SetupEvents() {
    PassOutput.ScrollChanged += PassOutput_ScrollChanged;
    PassOutput.ShowBeforeOutputChanged += PassOutput_ShowBeforeOutputChanged;

    PreviewKeyDown += IRDocumentHost_PreviewKeyDown;
    TextView.PreviewMouseRightButtonDown += TextView_PreviewMouseRightButtonDown;
    TextView.MouseDoubleClick += TextViewOnMouseDoubleClick;
    TextView.PreviewMouseMove += TextView_PreviewMouseMove;
    TextView.PreviewMouseDown += TextView_PreviewMouseDown;
    TextView.BlockSelected += TextView_BlockSelected;
    TextView.ElementSelected += TextView_ElementSelected;
    TextView.ElementUnselected += TextView_ElementUnselected;
    TextView.GotKeyboardFocus += TextView_GotKeyboardFocus;
    TextView.CaretChanged += TextViewOnCaretChanged;
    TextView.TextArea.TextView.ScrollOffsetChanged += TextViewOnScrollOffsetChanged;
    TextView.TextArea.SelectionChanged += TextAreaOnSelectionChanged;
    TextView.TextRegionFolded += TextViewOnTextRegionFolded;
    TextView.TextRegionUnfolded += TextViewOnTextRegionUnfolded;
    TextView.FunctionCallOpen += TextViewOnFunctionCallOpen;

    SectionPanel.OpenSection += SectionPanel_OpenSection;
    SearchPanel.SearchChanged += SearchPanel_SearchChanged;
    SearchPanel.NavigateToPreviousResult += SearchPanel_NaviateToPreviousResult;
    SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
    SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;
    ProfileColumns.ScrollChanged += ProfileColumns_ScrollChanged;
    ProfileColumns.RowSelected += ProfileColumn_RowSelected;
    Unloaded += IRDocumentHost_Unloaded;
  }

  private async void TextViewOnFunctionCallOpen(object sender, IRTextSection targetSection) {
    var targetFunc = targetSection.ParentFunction;
    ProfileSampleFilter targetFilter = null;

    if (profileFilter_ is {IncludesAll: false}) {
      targetFilter = profileFilter_.CloneForCallTarget(targetFunc);
    }

    historyManager_.ClearNextStates(); // Reset forward history.
    var mode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTab :
      OpenSectionKind.ReplaceCurrent;
    await Session.OpenProfileFunction(targetFunc, mode,
                                      targetFilter, this);
  }

  private void ProfileColumn_RowSelected(object sender, int line) {
    if (ignoreNextRowSelectedEvent_) {
      ignoreNextRowSelectedEvent_ = false;
      return;
    }

    TextView.SelectLine(line + 1);
  }

  private void TextViewOnTextRegionUnfolded(object sender, FoldingSection e) {
    ProfileColumns.HandleTextRegionUnfolded(e);
  }

  private void TextViewOnTextRegionFolded(object sender, FoldingSection e) {
    ProfileColumns.HandleTextRegionFolded(e);
  }

  private void ProfileColumns_ScrollChanged(object sender, ScrollChangedEventArgs e) {
    if (Math.Abs(e.VerticalChange) < double.Epsilon) {
      return;
    }

    TextView.ScrollToVerticalOffset(e.VerticalOffset);
  }

  public event EventHandler<(double offset, double offsetChangeAmount)> VerticalScrollChanged;
  public event EventHandler<(double offset, double offsetChangeAmount)> PassOutputVerticalScrollChanged;
  public event EventHandler<bool> PassOutputShowBeforeChanged;
  public event EventHandler<bool> PassOutputVisibilityChanged;

  public void NotifyPropertyChanged(string propertyName) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  public async Task UpdateRemarkSettings(RemarkSettings newSettings) {
    RemarkSettings = newSettings;
    await HandleNewRemarkSettings(newSettings, false);
  }

  public async Task ReloadSettings(bool hasProfilingChanges = true) {
    using var task = await loadTask_.CancelCurrentAndCreateTaskAsync();
    await HandleNewRemarkSettings(App.Settings.RemarkSettings, false, true);
    TextView.Initialize(settings_, session_);

    if (hasProfilingChanges) {
      await LoadProfile();
    }
  }

  public async void UnloadSection(IRTextSection section, bool switchingActiveDocument) {
    // Cancel any running tasks and hide panels.
    using var task = await loadTask_.CancelCurrentAndCreateTaskAsync();

    if (Section != section) {
      return;
    }

    if (!duringSwitchSearchResults_ && !switchingActiveDocument) {
      HideSearchPanel();
    }

    await HideRemarkPanel();
    HideActionPanel();
    SaveSectionState(section);

    if (!switchingActiveDocument) {
      if (PassOutputVisible) {
        await PassOutput.UnloadSection(section, TextView);
      }

      // Clear references to IR objects that would keep the previous function alive.
      await RemoveRemarks();
      hoveredElement_ = null;
      selectedElement_ = null;
      remarkElement_ = null;
      selectedBlock_ = null;
      profileFilter_ = new ProfileSampleFilter();
      TitlePrefix = TitleSuffix = null;
      DescriptionPrefix = DescriptionSuffix = null;
      PassOutputVisible = false;
      BlockSelector.SelectedItem = null;
      BlockSelector.ItemsSource = null;
    }
  }

  public void OnSessionSave() {
    if (Section != null) {
      SaveSectionState(Section);
    }
  }

  public async Task SwitchSearchResultsAsync(SectionSearchResult searchResults, IRTextSection section,
                                             SearchInfo searchInfo) {
    // Ensure the right section is being displayed.
    duringSwitchSearchResults_ = true;
    var openArgs = new OpenSectionEventArgs(section, OpenSectionKind.ReplaceCurrent, this);
    await Session.SwitchDocumentSectionAsync(openArgs);
    duringSwitchSearchResults_ = false;

    // Show the search panel and mark all results on the document.
    searchResult_ = searchResults;
    searchInfo.CurrentResult = 1;
    searchInfo.ResultCount = searchResults.Results.Count;
    ShowSearchPanel(searchInfo);
    TextView.MarkSearchResults(searchResults.Results, Colors.Khaki);
  }

  public bool HasSameSearchResultSection(IRTextSection section) {
    if (Section != section) {
      return false;
    }

    // Force the search panel to be displayed in case it was closed.
    return searchPanelVisible_;
  }

  public void JumpToSearchResult(TextSearchResult result, int index) {
    if (index >= SearchPanel.SearchInfo.ResultCount) {
      return;
    }

    SearchPanel.SearchInfo.CurrentResult = index;
    TextView.JumpToSearchResult(result, Colors.LightSkyBlue);
  }

  public async Task LoadSectionMinimal(ParsedIRTextSection parsedSection) {
    using var task = await loadTask_.CancelCurrentAndCreateTaskAsync();

    // Save state of currently loaded function for going back.
    if (TextView.IsLoaded) {
      historyManager_.SaveCurrentState();
      TextView.UnloadDocument();
    }

    TextView.PreloadSection(parsedSection);
  }

  public async Task LoadSection(ParsedIRTextSection parsedSection) {
    using var task = await loadTask_.CancelCurrentAndCreateTaskAsync();
    duringSectionSwitching_ = true;
    object data = Session.LoadDocumentState(parsedSection.Section);
    double horizontalOffset = 0;
    double verticalOffset = 0;

    if (data != null) {
      var state = StateSerializer.Deserialize<IRDocumentHostState>(data, parsedSection.Function);
      await TextView.LoadSavedSection(parsedSection, state.DocumentState);
      horizontalOffset = state.HorizontalOffset;
      verticalOffset = state.VerticalOffset;
    }
    else {
      await TextView.LoadSection(parsedSection);
    }

    if (PassOutputVisible) {
      await PassOutput.SwitchSection(parsedSection.Section, TextView);
    }

    PopulateBlockSelector();
    await ReloadRemarks(task);

    // When applying profile, jump to hottest element
    // only if the vertical offset is 0.
    bool jumpToHottestElement = verticalOffset < double.Epsilon;

    if (parsedSection.LoadFailed ||
        !await LoadProfile(true, jumpToHottestElement)) {
      await HideProfile();
    }

    if (!jumpToHottestElement) {
      Dispatcher.BeginInvoke(() => {
        TextView.ScrollToHorizontalOffset(horizontalOffset);
        TextView.ScrollToVerticalOffset(verticalOffset);
      }, DispatcherPriority.Render);
    }

    duringSectionSwitching_ = false;
  }

  private void UpdateHistoryMenu() {
    NotifyPropertyChanged(nameof(HasPreviousFunctions));
    NotifyPropertyChanged(nameof(HasNextFunctions));
    DocumentUtils.CreateBackMenu(BackMenu, historyManager_.PreviousFunctions,
                                 BackMenuItem_OnClick,
                                 settings_, session_);
  }

  private async void BackMenuItem_OnClick(object sender, RoutedEventArgs e) {
    var state = ((MenuItem)sender)?.Tag as ProfileFunctionState;

    if (state != null) {
      historyManager_.RevertToState(state);
      await LoadPreviousSectionState(state);
    }
  }

  private async Task LoadPreviousSection() {
    var state = historyManager_.PopPreviousState();

    if (state != null) {
      await LoadPreviousSectionState(state);
    }
  }

  private async Task LoadNextSection() {
    var state = historyManager_.PopNextState();

    if (state != null) {
      await LoadPreviousSectionState(state);
    }
  }

  private async Task LoadPreviousSectionState(ProfileFunctionState state) {
    await session_.OpenDocumentSectionAsync(
      new OpenSectionEventArgs(state.Section, OpenSectionKind.ReplaceCurrent, this));

    if (state.ProfileFilter is {IncludesAll: false}) {
      await SwitchProfileInstanceAsync(state.ProfileFilter);
    }
  }

  //? TODO: Create a new class to do the remark finding/filtering work
  public bool IsAcceptedRemark(Remark remark, IRTextSection section, RemarkSettings remarkSettings) {
    if (!remarkSettings.ShowPreviousSections && remark.Section != section) {
      return false;
    }

    //? TODO: Move SearchText into a state object
    if (!string.IsNullOrEmpty(remarkSettings.SearchedText)) {
      if (!remark.RemarkText.Contains(remarkSettings.SearchedText, StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
    }

    bool kindResult = remark.Kind switch {
      RemarkKind.Analysis     => remarkSettings.Analysis,
      RemarkKind.Optimization => remarkSettings.Optimization,
      RemarkKind.Default      => remarkSettings.Default,
      RemarkKind.Verbose      => remarkSettings.Verbose,
      RemarkKind.Trace        => remarkSettings.Trace,
      _                       => false
    };

    if (!kindResult) {
      return false;
    }

    if (remark.Category.HasTitle && remarkSettings.HasCategoryFilters) {
      if (remarkSettings.CategoryFilter.TryGetValue(remark.Category.Title, out bool isCategoryEnabled)) {
        return isCategoryEnabled;
      }
    }

    return true;
  }

  public bool IsAcceptedContextRemark(Remark remark, IRTextSection section, RemarkSettings remarkSettings) {
    if (!IsAcceptedRemark(remark, section, remarkSettings)) {
      return false;
    }

    // Filter based on context, accept any context that is a child of the active context.
    if (activeRemarkContext_ != null) {
      return IsActiveContextTreeRemark(remark);
    }

    return true;
  }

  public bool IsActiveContextTreeRemark(Remark remark) {
    var context = remark.Context;

    while (context != null) {
      if (context == activeRemarkContext_) {
        Trace.TraceInformation($"=> Accept remark in context {remark.Context.Name}");
        Trace.TraceInformation($"      text \"{remark.RemarkText}\"");
        return true;
      }

      context = context.Parent;
    }

    return false;
  }

  public async Task EnterDiffMode() {
    if (Section != null) {
      SaveSectionState(Section);
    }

    await HideOptionalPanels();
    TextView.EnterDiffMode();
  }

  public async Task ExitDiffMode() {
    TextView.ExitDiffMode();

    if (PassOutputVisible) {
      await PassOutput.RestorePassOutput();
    }

    await HideOptionalPanels();
  }

  public async Task LoadDiffedFunction(DiffMarkingResult diffResult, IRTextSection newSection) {
    using var task = await loadTask_.CancelCurrentAndCreateTaskAsync();
    await TextView.LoadDiffedFunction(diffResult, newSection);

    if (PassOutputVisible) {
      await PassOutput.SwitchSection(newSection, TextView);
    }

    await ReloadRemarks(task);
  }

  public async Task LoadDiffedPassOutput(DiffMarkingResult diffResult) {
    if (PassOutputVisible) {
      await PassOutput.LoadDiffedPassOutput(diffResult);
    }
  }

  private void TextViewOnMouseDoubleClick(object sender, MouseButtonEventArgs e) {
    if (!Utils.IsControlModifierActive()) {
      return;
    }

    SearchSymbolImpl(Utils.IsShiftModifierActive());
  }

  private void TextViewOnCaretChanged(object? sender, int offset) {
    if (columnsVisible_) {
      ignoreNextRowSelectedEvent_ = true;
      var line = TextView.Document.GetLineByOffset(offset);
      ProfileColumns.SelectRow(line.LineNumber - 1);
    }
  }

  private void TextViewOnScrollOffsetChanged(object? sender, EventArgs e) {
    HideActionPanel();
    DetachRemarkPanel(true);

    double offset = TextView.TextArea.TextView.VerticalOffset;
    double changeAmount = offset - previousVerticalOffset_;
    previousVerticalOffset_ = offset;

    // Sync scrolling with the optional columns.
    SyncColumnsVerticalScrollOffset(offset);
    VerticalScrollChanged?.Invoke(this, (offset, changeAmount));
  }

  private void TextAreaOnSelectionChanged(object sender, EventArgs e) {
    if (funcProfile_ == null) {
      return;
    }

    // Compute the weight sum of the selected range of instructions
    // and display it in the main status bar.
    int startLine = TextView.TextArea.Selection.StartPosition.Line;
    int endLine = TextView.TextArea.Selection.EndPosition.Line;

    if (!ProfilingUtils.ComputeAssemblyWeightInRange(startLine, endLine,
                                                     Function, funcProfile_,
                                                     out var weightSum, out int count)) {
      Session.SetApplicationStatus("");
      return;
    }

    double weightPercentage = funcProfile_.ScaleWeight(weightSum);
    string text = $"Selected {count}: {weightPercentage.AsPercentageString()} ({weightSum.AsMillisecondsString()})";
    Session.SetApplicationStatus(text, "Sum of time for the selected instructions");
  }

  private void SyncColumnsVerticalScrollOffset(double offset) {
    // Sync scrolling with the optional columns.
    if (columnsVisible_) {
      ProfileColumns.ScrollToVerticalOffset(offset);
    }
  }

  private async void HandleRemarkSettingsChange() {
    if (!remarkPanelVisible_ && !remarkOptionsPanelVisible_) {
      await HandleNewRemarkSettings(remarkSettings_, false, true);
    }
  }

  private async void IRDocumentHost_Unloaded(object sender, RoutedEventArgs e) {
    await HideRemarkPanel(true);
  }

  private async void TextView_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
    await HideRemarkPanel();

    // Handle the back/forward mouse buttons
    // to navigate through the function history.
    if (e.ChangedButton == MouseButton.XButton1) {
      e.Handled = true;
      await LoadPreviousSection();
      return;
    }
    else if (e.ChangedButton == MouseButton.XButton2) {
      e.Handled = true;
      await LoadNextSection();
      return;
    }

    var point = e.GetPosition(TextView.TextArea.TextView);
    var element = TextView.GetElementAt(point);

    if (element == null) {
      HideActionPanel(true);
      return;
    }

    if (element != hoveredElement_ && !actionPanelHovered_) {
      await ShowActionPanel(element, true);
    }

    // Middle-button click sets the input element in the active query panel.
    if (mainQueryInputValue_ != null && e.MiddleButton == MouseButtonState.Pressed) {
      mainQueryInputValue_.ForceValueUpdate(element);
      e.Handled = true;
    }
  }

  private async void TextView_PreviewMouseMove(object sender, MouseEventArgs e) {
    if (!actionPanelVisible_) {
      return;
    }

    var point = e.GetPosition(TextView.TextArea.TextView);
    var element = TextView.GetElementAt(point);

    if (!remarkPanelVisible_ && !actionPanelHovered_ && !actionPanelFromClick_) {
      if (element == null || element != hoveredElement_) {
        HideActionPanel();
        await HideRemarkPanel();
      }
    }
  }

  private async void Hover_MouseHover(object sender, MouseEventArgs e) {
    if (!remarkSettings_.ShowActionButtonOnHover ||
        remarkSettings_.ShowActionButtonWithModifier && !Utils.IsKeyboardModifierActive()) {
      actionPanelHovered_ = false;
      return;
    }

    if (remarkPanelVisible_ || actionPanelHovered_) {
      return;
    }

    var point = e.GetPosition(TextView.TextArea.TextView);

    if (point.X <= 0 || point.Y <= 0) {
      // Don't consider the left margin and other elements outside the text view.
      return;
    }

    //? TODO: If other panels are opened over the document, don't consider their area.
    var element = TextView.GetElementAt(point);

    if (element != null) {
      // If the panel is already showing for this element, ignore the action
      // so that it doesn't move around after the mouse cursor.
      if (element != hoveredElement_) {
        await ShowActionPanel(element);
        hoveredElement_ = element;
      }
    }
    else {
      HideActionPanel();
      hoveredElement_ = null;
    }
  }

  private async void TextView_ElementUnselected(object sender, IRElementEventArgs e) {
    HideActionPanel(true);
    await HideRemarkPanel();
  }

  private async void TextView_ElementSelected(object sender, IRElementEventArgs e) {
    selectedElement_ = e.Element;
    await ShowActionPanel(e.Element);
  }

  private IRElement GetRemarkElement(IRElement element) {
    if (element.GetTag<RemarkTag>() != null) {
      return element;
    }

    // If it's an operand, check if the instr. has a remark instead.
    if (element is OperandIR op) {
      var instr = op.ParentTuple;

      if (instr.GetTag<RemarkTag>() != null) {
        return instr;
      }
    }

    return null;
  }

  private async Task ShowActionPanel(IRElement element, bool fromClickEvent = false) {
    remarkElement_ = GetRemarkElement(element);
    var visualElement = remarkElement_;

    if (remarkElement_ == null) {
      await HideRemarkPanel();

      // If there are action buttons in the panel, keep showing it.
      if (!ActionPanel.HasActionButtons) {
        HideActionPanel();
        return;
      }

      visualElement = element;
      ActionPanel.ShowRemarksButton = false;
    }
    else {
      ActionPanel.ShowRemarksButton = true;
    }

    var visualLine = TextView.TextArea.TextView.GetVisualLine(visualElement.TextLocation.Line + 1);

    if (visualLine != null) {
      // If there is an ongoing hiding operation, cancel it since it would
      // likely hide the action panel being set up here.
      if (delayedHideActionPanel_ != null) {
        delayedHideActionPanel_.Cancel();
        delayedHideActionPanel_ = null;
      }

      var linePos = visualLine.GetVisualPosition(0, VisualYPosition.LineBottom);
      double x = Mouse.GetPosition(this).X + ActionPanelOffset;
      double y = linePos.Y + DocumentToolbar.ActualHeight -
                 1 - TextView.TextArea.TextView.ScrollOffset.Y;

      Canvas.SetLeft(ActionPanel, x);
      Canvas.SetTop(ActionPanel, y);
      ActionPanel.Opacity = 0.0;
      ActionPanel.Visibility = Visibility.Visible;

      var animation2 = new DoubleAnimation(ActionPanelInitialOpacity,
                                           TimeSpan.FromSeconds(fromClickEvent ? 0 : AnimationDuration));
      ActionPanel.BeginAnimation(OpacityProperty, animation2,
                                 HandoffBehavior.SnapshotAndReplace);

      actionPanelFromClick_ = fromClickEvent;
      actionPanelVisible_ = true;
      remarkPanelLocation_ = new Point(x, y + ActionPanelHeight);
    }
  }

  private void HideActionPanel(bool force = false) {
    // Ignore if panel not visible or in process of being hidden.
    if (!actionPanelVisible_ || delayedHideActionPanel_ != null) {
      return;
    }

    if (force) {
      HideActionPanelImpl();
      return;
    }

    delayedHideActionPanel_ = DelayedAction.StartNew(() => {
      if (remarkPanelVisible_ || ActionPanel.IsMouseOver) {
        return;
      }

      HideActionPanelImpl();
    });
  }

  private void HideActionPanelImpl() {
    var animation = new DoubleAnimation(0.0, TimeSpan.FromSeconds(AnimationDuration));
    animation.Completed += (s, e) => { ActionPanel.Visibility = Visibility.Collapsed; };
    ActionPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    actionPanelVisible_ = false;
    delayedHideActionPanel_ = null;
  }

  private void ShowRemarkPanel() {
    if (remarkPanelVisible_ || remarkElement_ == null) {
      return;
    }

    remarkPanel_ = new RemarkPreviewPanel();
    remarkPanel_.PopupClosed += RemarkPanel__PanelClosed;
    remarkPanel_.PopupDetached += RemarkPanel__PanelDetached;
    remarkPanel_.RemarkContextChanged += RemarkPanel__RemarkContextChanged;
    remarkPanel_.RemarkChanged += RemarkPanel__RemarkChanged;
    remarkPanel_.Opacity = 0.0;
    remarkPanel_.IsOpen = true;

    var animation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(AnimationDuration));
    remarkPanel_.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    remarkPanelVisible_ = true;

    InitializeRemarkPanel(remarkElement_);
  }

  private void RemarkPanel__RemarkChanged(object sender, Remark e) {
    TextView.SelectDocumentRemark(e);
  }

  private void RemarkPanel__PanelDetached(object sender, EventArgs e) {
    // Keep the remark panel floating over the document.
    DetachRemarkPanel();
  }

  private void DetachRemarkPanel(bool notifyPanel = false) {
    if (remarkPanel_ == null) {
      return;
    }

    Session.RegisterDetachedPanel(remarkPanel_);
    HideActionPanel();

    if (notifyPanel) {
      remarkPanel_.PopupDetached -= RemarkPanel__PanelDetached;
      remarkPanel_.DetachPanel();
    }

    remarkPanelVisible_ = false;
    remarkPanel_ = null;
  }

  private async void RemarkPanel__PanelClosed(object sender, EventArgs e) {
    // If it's one of the detached panels, unregister it.
    var panel = (RemarkPreviewPanel)sender;

    if (panel.IsDetached) {
      Session.UnregisterDetachedPanel(panel);
      return;
    }

    await HideRemarkPanel();
  }

  private async void RemarkPanel__RemarkContextChanged(object sender, RemarkContextChangedEventArgs e) {
    activeRemarkContext_ = e.Context;

    if (e.Context != null && e.Remarks != null) {
      await UpdateDocumentRemarks(e.Remarks);
    }
    else {
      // Filtering of context remarks disabled.
      await UpdateDocumentRemarks(remarkList_);
    }
  }

  private void InitializeRemarkPanel(IRElement element) {
    remarkPanel_.Session = Session;
    remarkPanel_.Function = Function;
    remarkPanel_.Section = Section;
    remarkPanel_.Initialize(element, remarkPanelLocation_, this, remarkSettings_);
  }

  private async Task HideRemarkPanel(bool force = false) {
    if (!remarkPanelVisible_) {
      return;
    }

    if (force) {
      remarkPanel_.IsOpen = false;
      remarkPanel_ = null;
      return;
    }

    await ResetActiveRemarkContext();
    var animation = new DoubleAnimation(0.0, TimeSpan.FromSeconds(AnimationDuration));

    animation.Completed += (s, e) => {
      if (remarkPanel_ != null) { // When section unloads, can be before animation completes.
        remarkPanel_.IsOpen = false;
        remarkPanel_ = null;
      }
    };

    remarkPanel_.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    remarkPanelVisible_ = false;
  }

  private async Task ResetActiveRemarkContext() {
    if (activeRemarkContext_ != null) {
      activeRemarkContext_ = null;
      await UpdateDocumentRemarks(remarkList_);
    }
  }

  private async void OptionsPanel_SettingsChanged(object sender, EventArgs e) {
    if (optionsPanelVisible_) {
      var newSettings = (DocumentSettings)optionsPanelPopup_.Settings;

      if (newSettings != null) {
        await LoadNewSettings(newSettings, optionsPanel_.SyntaxFileChanged, false);
        optionsPanelPopup_.Settings = newSettings.Clone();
      }
    }
  }

  private async Task LoadNewSettings(DocumentSettings newSettings, bool force, bool commit) {
    if (force || !newSettings.Equals(Settings)) {
      bool hasProfilingChanges = newSettings.HasProfilingChanges(Settings);
      App.Settings.DocumentSettings = newSettings;
      Settings = newSettings;
      await ReloadSettings(hasProfilingChanges);
    }

    if (commit) {
      // Apply settings to other open documents in the session.
      await Session.ReloadDocumentSettings(newSettings, TextView);
      App.SaveApplicationSettings();
    }
  }

  private void TextView_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
    //CloseSectionPanel();
  }

  private void SearchPanel_CloseSearchPanel(object sender, SearchInfo e) {
    HideSearchPanel();
  }

  private void SearchPanel_NavigateToNextResult(object sender, SearchInfo e) {
    TextView.JumpToSearchResult(searchResult_.Results[e.CurrentResult], Colors.LightSkyBlue);
  }

  private void SearchPanel_NaviateToPreviousResult(object sender, SearchInfo e) {
    TextView.JumpToSearchResult(searchResult_.Results[e.CurrentResult], Colors.LightSkyBlue);
  }

  private async void IRDocumentHost_PreviewKeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Escape) {
      CloseSectionPanel();
      HideSearchPanel();
      e.Handled = true;
    }
    else if (e.Key == Key.C && Utils.IsControlModifierActive()) {
      // Override Ctrl+C to copy instruction details instead of just text,
      // but not if Shift/Alt key is also pressed, copy plain text then.
      if (!Utils.IsAltModifierActive() &&
          !Utils.IsShiftModifierActive() &&
          !TextView.HandleOverlayKeyPress(e)) { // Send to overlays first.
        await DocumentExporting.CopySelectedLinesAsHtml(TextView);
        e.Handled = true;
      }
    }
    else if (e.Key == Key.Back) {
      if (Utils.IsKeyboardModifierActive()) {
        await LoadNextSection();
      }
      else {
        await LoadPreviousSection();
      }
    }
    else if (e.Key == Key.H && Utils.IsControlModifierActive()) {
      JumpToHottestProfiledElement();
    }
    else if (e.Key == Key.F2) {
      if (Utils.IsShiftModifierActive()) {
        JumpToProfiledElement(1);
      }
      else {
        JumpToProfiledElement(-1);
      }
    }
  }

  private void SectionPanel_ClosePanel(object sender, EventArgs e) {
    CloseSectionPanel();
  }

  private async void SectionPanel_OpenSection(object sender, OpenSectionEventArgs e) {
    SectionPanelHost.Visibility = Visibility.Collapsed;
    e.TargetDocument = this;
    await Session.SwitchDocumentSectionAsync(e);
    TextView.Focus();
  }

  private void CloseSectionPanel() {
    if (SectionPanelHost.Visibility == Visibility.Visible) {
      SectionPanelHost.Visibility = Visibility.Collapsed;
    }
  }

  private async Task RemoveRemarks() {
    if (HasRemarks) {
      remarkList_ = null;
      activeRemarkContext_ = null;
      await UpdateDocumentRemarks(remarkList_);
    }
  }

  private void SaveSectionState(IRTextSection section) {
    // Annotations made in diff mode are not saved right now,
    // since the text and function IR can be different from the original function.
    if (TextView.DiffModeEnabled) {
      return;
    }

    var state = new IRDocumentHostState();
    state.DocumentState = TextView.SaveState();
    state.HorizontalOffset = TextView.HorizontalOffset;
    state.VerticalOffset = TextView.VerticalOffset;
    byte[] data = StateSerializer.Serialize(state, Function);

    Session.SaveDocumentState(data, section);
    Session.SetSectionAnnotationState(section, state.HasAnnotations);
  }

  private async Task<bool> LoadProfile(bool reloadFilterMenus = true,
                                       bool jumpToHottestElement = true) {
    if (Session.ProfileData == null) {
      return false;
    }

    UpdateProfileFilterUI();
    var funcProfile = Session.ProfileData.GetFunctionProfile(Section.ParentFunction);

    if (funcProfile == null) {
      return false;
    }

    await MarkFunctionProfile(funcProfile);

    if (reloadFilterMenus) {
      ProfilingUtils.CreateInstancesMenu(InstancesMenu, Section, funcProfile,
                                         InstanceMenuItem_OnClick,
                                         InstanceMenuItem_OnRightClick,
                                         settings_, Session);
      ProfilingUtils.CreateThreadsMenu(ThreadsMenu, Section, funcProfile,
                                       ThreadMenuItem_OnClick, settings_, Session);
    }

    if (jumpToHottestElement &&
        settings_.ProfileMarkerSettings.JumpToHottestElement) {
      JumpToHottestProfiledElement();
    }

    return true;
  }

  private void InstanceMenuItem_OnRightClick(object sender, MouseButtonEventArgs e) {
    if (sender is MenuItem menuItem) {
      if (menuItem.Tag is ProfileCallTreeNode node) {
        Session.SelectProfileFunctionInPanel(node, ToolPanelKind.FlameGraph);
        Session.SelectProfileFunctionInPanel(node, ToolPanelKind.CallTree);
        e.Handled = true;
      }
    }
  }

  private async Task<bool> UpdateProfilingColumns() {
    var columnData = TextView.ProfileColumnData;
    ColumnsVisible = columnData is {HasData: true};

    if (columnData == null || !columnData.HasData) {
      ProfileColumns.Reset();
      ProfileBlocksMenu.Items.Clear();
      return false;
    }

    ResetViewMenuItemEvents();
    ProfileColumns.Settings = settings_;
    ProfileColumns.ColumnSettings = settings_.ColumnSettings;

    await ProfileColumns.Display(columnData, TextView);
    profileMarker_.UpdateColumnStyles(columnData, Function, TextView);
    ProfileColumns.UpdateColumnWidths();

    ProfileColumns.ColumnSettingsChanged -= OnProfileColumnsOnColumnSettingsChanged;
    ProfileColumns.ColumnSettingsChanged += OnProfileColumnsOnColumnSettingsChanged;

    // Add the columns to the View menu.
    ProfileColumns.BuildColumnsVisibilityMenu(columnData, ProfileViewMenu, async () => {
      await UpdateProfilingColumns();
    });

    SetViewMenuItemEvents();
    return true;
  }

  private void SetViewMenuItemEvents() {
    foreach (var item in viewMenuItems_) {
      item.Checked += ViewMenuItem_OnCheckedChanged;
      item.Unchecked += ViewMenuItem_OnCheckedChanged;
    }
  }

  private void ResetViewMenuItemEvents() {
    foreach (var item in viewMenuItems_) {
      item.Checked -= ViewMenuItem_OnCheckedChanged;
      item.Unchecked -= ViewMenuItem_OnCheckedChanged;
    }
  }

  private async void OnProfileColumnsOnColumnSettingsChanged(object sender, OptionalColumn column) {
    ProfileDocumentMarker.UpdateColumnStyle(column, TextView.ProfileColumnData, Function, TextView,
                                            settings_.ProfileMarkerSettings,
                                            settings_.ColumnSettings);
  }

  private void CreateProfileBlockMenu(FunctionProfileData funcProfile,
                                      FunctionProcessingResult result) {
    profileBlocks_ = result.BlockSampledElements;
    var list = new List<ProfileMenuItem>(result.BlockSampledElements.Count);
    double maxWidth = 0;

    ProfileBlocksMenu.Items.Clear();
    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings_.ProfileMarkerSettings;
    int order = 0;

    foreach (var (block, weight) in result.BlockSampledElements) {
      double weightPercentage = funcProfile.ScaleWeight(weight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage)) {
        break;
      }

      string prefixText = $"B{block.Number}";
      string text = $"({markerSettings.FormatWeightValue(weight)})";

      var value = new ProfileMenuItem(text, weight.Ticks, weightPercentage) {
        Element = block,
        PrefixText = prefixText,
        ToolTip = $"Line {block.TextLocation.Line + 1}",
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush()
      };

      var item = new MenuItem {
        Header = value,
        Tag = list.Count,
        HeaderTemplate = valueTemplate
      };

      item.Click += (sender, args) => {
        var menuItem = (MenuItem)sender;
        JumpToProfiledBlockAt((int)menuItem.Tag);
      };

      ProfileBlocksMenu.Items.Add(item);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(prefixText, ref maxWidth, ProfileBlocksMenu);
      list.Add(value);
    }

    foreach (var value in list) {
      value.MinTextWidth = maxWidth;
    }
  }

  private void CreateProfileElementMenu(FunctionProfileData funcProfile,
                                        FunctionProcessingResult result) {
    var list = new List<ProfileMenuItem>(result.SampledElements.Count);
    ProfileElementsMenu.Items.Clear();

    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings_.ProfileMarkerSettings;
    int order = 0;
    double maxWidth = 0;

    foreach (var (element, weight) in result.SampledElements) {
      double weightPercentage = funcProfile.ScaleWeight(weight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage)) {
        break;
      }

      string prefixText = DocumentUtils.GenerateElementPreviewText(element, TextView.SectionText, 50);
      string text = $"({markerSettings.FormatWeightValue(weight)})";

      var value = new ProfileMenuItem(text, weight.Ticks, weightPercentage) {
        Element = element,
        PrefixText = prefixText,
        ToolTip = $"Line {element.TextLocation.Line + 1}",
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush()
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

  private async Task ApplyProfileFilter() {
    using var task = await loadTask_.CancelCurrentAndCreateTaskAsync();

    if (profileFilter_ is {IncludesAll: false}) {
      await LoadProfileInstance();
    }
    else {
      await LoadProfile(false);
    }

    // Apply the same filter in the source file panel.
    await Session.OpenProfileSourceFile(Section.ParentFunction, profileFilter_);

    TitlePrefix = ProfilingUtils.CreateProfileFilterTitle(profileFilter_, session_);
    DescriptionSuffix = "\n\n" + ProfilingUtils.CreateProfileFilterDescription(profileFilter_, Session);
    Session.UpdateDocumentTitles();
  }

  public async Task SwitchProfileInstanceAsync(ProfileSampleFilter instanceFilter) {
    profileFilter_ = instanceFilter;
    ProfilingUtils.SyncInstancesMenuWithFilter(InstancesMenu, instanceFilter);
    ProfilingUtils.SyncThreadsMenuWithFilter(ThreadsMenu, instanceFilter);
    await LoadProfileInstance();
  }

  private async Task LoadProfileInstance() {
    UpdateProfileFilterUI();
    var instanceProfile = await ComputeInstanceProfile();
    var funcProfile = instanceProfile.GetFunctionProfile(Section.ParentFunction);

    if (funcProfile == null) {
      return;
    }

    await MarkFunctionProfile(funcProfile);
  }

  private async Task<ProfileData> ComputeInstanceProfile() {
    return await LongRunningAction.Start(
      async () => await Task.Run(() => Session.ProfileData.
                                   ComputeProfile(Session.ProfileData, profileFilter_, false)),
      TimeSpan.FromMilliseconds(500),
      "Filtering function instance", this, Session);
  }

  private void UpdateProfileFilterUI() {
    NotifyPropertyChanged(nameof(HasProfileInstanceFilter));
    NotifyPropertyChanged(nameof(HasProfileThreadFilter));
  }

  private async Task MarkFunctionProfile(FunctionProfileData funcProfile) {
    // Mark instructions.
    profileMarker_ = new ProfileDocumentMarker(funcProfile, Session.ProfileData,
                                               settings_.ProfileMarkerSettings,
                                               settings_.ColumnSettings, Session.CompilerInfo);
    await profileMarker_.Mark(TextView, Function, Section.ParentFunction);

    // Redraw the flow graphs, may have loaded before the marker set the node tags.
    Session.RedrawPanels();

    if (TextView.ProfileProcessingResult != null) {
      var inlineeList = profileMarker_.GenerateInlineeList(TextView.ProfileProcessingResult);
      ProfilingUtils.CreateInlineesMenu(InlineesMenu, Section, inlineeList,
                                        funcProfile, InlineeMenuItem_OnClick, settings_, Session);
      CreateProfileBlockMenu(funcProfile, TextView.ProfileProcessingResult);
      CreateProfileElementMenu(funcProfile, TextView.ProfileProcessingResult);
    }

    UpdateDocumentTitle(funcProfile);
    profileElements_ = TextView.ProfileProcessingResult?.SampledElements;
    funcProfile_ = funcProfile;
    ProfileVisible = true;

    // Show optional columns with timing, counters, etc.
    // First remove any previous columns.
    await UpdateProfilingColumns();
  }

  private void UpdateDocumentTitle(FunctionProfileData funcProfile) {
    // Update document tooltip.
    DescriptionPrefix =
      ProfilingUtils.CreateProfileFunctionDescription(funcProfile, settings_.ProfileMarkerSettings, Session) + "\n\n";
    Session.UpdateDocumentTitles();
  }

  private async Task HideProfile() {
    ProfileVisible = false;
    ColumnsVisible = false;
    ResetProfilingMenus();
  }

  private void ResetProfilingMenus() {
    DocumentUtils.RemoveNonDefaultMenuItems(ProfileBlocksMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(ProfileElementsMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(InstancesMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(ThreadsMenu);
    DocumentUtils.RemoveNonDefaultMenuItems(InlineesMenu);
  }

  private async Task ReloadRemarks(CancelableTask loadTask) {
    await RemoveRemarks();

    // Loading remarks can take several seconds for very large functions,
    // this makes it possible to cancel the work if section switches.
    var prevRemarkList = remarkList_;
    remarkList_ = await FindRemarks(loadTask);

    if (loadTask.IsCanceled) {
      return;
    }

    if (HasRemarks) {
      await AddRemarks(remarkList_);
    }
    else if (prevRemarkList is {Count: > 0}) {
      TextView.RemoveRemarks();
    }
  }

  private async Task<List<Remark>> FindRemarks(CancelableTask cancelableTask) {
    var remarkProvider = Session.CompilerInfo.RemarkProvider;

    return await Task.Run(() => {
      var sections = remarkProvider.GetSectionList(Section, remarkSettings_.SectionHistoryDepth,
                                                   remarkSettings_.StopAtSectionBoundaries);
      var document = Session.SessionState.FindLoadedDocument(Section);
      var options = new RemarkProviderOptions();
      var results = remarkProvider.ExtractAllRemarks(sections, Function, document, options, cancelableTask);
      return results;
    });
  }

  private async Task AddRemarks(List<Remark> remarks) {
    await AddRemarkTags(remarks);
    await UpdateDocumentRemarks(remarks);
  }

  private (List<Remark>, List<RemarkLineGroup>) FilterDocumentRemarks(List<Remark> remarks) {
    // Filter list based on selected options.
    var filteredList = new List<Remark>(remarks.Count);

    foreach (var remark in remarks) {
      if (IsAcceptedContextRemark(remark, Section, remarkSettings_)) {
        filteredList.Add(remark);
      }
    }

    // Group remarks by element line number.
    var markerRemarksGroups = new List<RemarkLineGroup>(remarks.Count);

    if (remarkSettings_.ShowMarginRemarks) {
      var markerRemarksMap = new Dictionary<int, RemarkLineGroup>(remarks.Count);

      foreach (var remark in filteredList) {
        if (!remark.Category.AddLeftMarginMark) {
          continue;
        }

        if (remark.Section != Section) {
          // Remark is from previous section. Accept only if user wants
          // to see previous optimization remarks on the left margin.
          bool isAccepted = remark.Category.Kind == RemarkKind.Optimization &&
                            remarkSettings_.ShowPreviousOptimizationRemarks ||
                            remark.Category.Kind == RemarkKind.Analysis &&
                            remarkSettings_.ShowPreviousAnalysisRemarks;

          if (!isAccepted) {
            continue;
          }
        }

        bool handled = false;
        int elementLine = -1;

        foreach (var element in remark.ReferencedElements) {
          elementLine = element.TextLocation.Line;

          if (markerRemarksMap.TryGetValue(elementLine, out var remarkGroup)) {
            remarkGroup.Add(remark, Section);
            handled = true;
            break;
          }
        }

        if (!handled) {
          var remarkGroup = new RemarkLineGroup(elementLine, remark);
          markerRemarksMap[elementLine] = remarkGroup;
          markerRemarksGroups.Add(remarkGroup);
        }
      }
    }

    return (remarkSettings_.ShowDocumentRemarks ? filteredList : null,
            remarkSettings_.ShowMarginRemarks ? markerRemarksGroups : null);
  }

  private async Task UpdateDocumentRemarks(List<Remark> remarks) {
    if (remarks == null || !remarkSettings_.ShowRemarks ||
        !remarkSettings_.ShowMarginRemarks &&
        !remarkSettings_.ShowDocumentRemarks) {
      TextView.RemoveRemarks(); // No remarks or disabled.
      return;
    }

    var (allRemarks, markerRemarksGroups) = await Task.Run(() => FilterDocumentRemarks(remarks));
    TextView.UpdateRemarks(allRemarks, markerRemarksGroups, activeRemarkContext_ != null);
  }

  private void RemoveRemarkTags() {
    Function.ForEachElement(element => {
      element.RemoveTag<RemarkTag>();
      return true;
    });
  }

  private Task AddRemarkTags(List<Remark> remarks) {
    return Task.Run(() => {
      RemoveRemarkTags();

      foreach (var remark in remarks) {
        foreach (var element in remark.ReferencedElements) {
          var remarkTag = element.GetOrAddTag<RemarkTag>();
          remarkTag.Remarks.Add(remark);
        }
      }
    });
  }

  private async Task HideOptionalPanels() {
    HideSearchPanel();
    HideActionPanel();
    await HideRemarkPanel();
  }

  private void TextView_BlockSelected(object sender, IRElementEventArgs e) {
    if (e.Element != selectedBlock_) {
      selectedBlock_ = e.Element;
      BlockSelector.SelectedItem = e.Element;
    }
  }

  private void PopulateBlockSelector() {
    if (TextView.Blocks != null) {
      var blockList = new CollectionView(TextView.Blocks);
      BlockSelector.ItemsSource = blockList;
    }
    else {
      BlockSelector.ItemsSource = null;
    }
  }

  private void TextView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
    hoverPoint_ = e.GetPosition(TextView.TextArea.TextView);
    TextView.SelectElementAt(hoverPoint_);
  }

  private void MenuItem_Click(object sender, RoutedEventArgs e) {
    TextView.ClearMarkedElementAt(hoverPoint_);
  }

  private void ComboBox_Loaded(object sender, RoutedEventArgs e) {
    if (sender is ComboBox control) {
      Utils.PatchComboBoxStyle(control);
    }
  }

  private void BlockSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (duringSectionSwitching_ || e.AddedItems.Count != 1) {
      return;
    }

    var block = e.AddedItems[0] as BlockIR;

    // If the event triggers during loading the section, while the combobox is update,
    // ignore it, otherwise it selects the first block.
    if (block != selectedBlock_ && !TextView.DuringSectionLoading) {
      selectedBlock_ = block;
      TextView.GoToBlock(block);
    }
  }

  private void NextBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
    TextView.GoToNextBlock();
  }

  private void PreviousBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
    TextView.GoToPreviousBlock();
  }

  private void FocusBlockSelectorExecuted(object sender, ExecutedRoutedEventArgs e) {
    BlockSelector.Focus();
    BlockSelector.IsDropDownOpen = true;
  }

  private void ToggleSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
    ShowSearchPanel(true);
  }

  private void ShowSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
    ShowSearchPanel(false);
  }

  private void ShowSearchPanel(bool fromKeyboardShortcut) {
    // Use selected text as initial search input.
    var info = new SearchInfo();
    bool hasInitialText = false;

    if (TextView.SelectionLength > 1) {
      info.SearchedText = TextView.SelectedText;
      info.IsCaseInsensitive = true;
      hasInitialText = true;
    }

    if (!searchPanelVisible_) {
      ShowSearchPanel(info);
    }
    else if (fromKeyboardShortcut) {
      // For a subsequent keyboard shortcut press,
      // don't hide the visible panel, instead either use the new selected text,
      // or there is no selection, select the entire text in the search panel.
      SearchPanel.SearchInfo.SearchedText = info.SearchedText;
      SearchPanel.SearchInfo.IsCaseInsensitive = info.IsCaseInsensitive;
      SearchPanel.Show(SearchPanel.SearchInfo,
                       SearchPanel.SearchInfo.SearchAll, !hasInitialText);
    }
    else {
      HideSearchPanel();
    }
  }

  private void HideSearchPanel() {
    if (!searchPanelVisible_) {
      return;
    }

    searchPanelVisible_ = false;
    SearchPanel.Hide();
    SearchPanel.Reset();
    SearchPanel.Visibility = Visibility.Collapsed;
    SearchButton.IsChecked = false;
  }

  private void ShowSearchPanel(SearchInfo searchInfo, bool searchAll = false) {
    SearchPanel.Visibility = Visibility.Visible;
    SearchPanel.Show(searchInfo, searchAll);
    SearchButton.IsChecked = true;
    searchPanelVisible_ = true;
  }

  private void ShowSectionListExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (SectionPanelHost.Visibility == Visibility.Visible) {
      SectionPanelHost.Visibility = Visibility.Collapsed;
    }
    else {
      SectionPanel.CompilerInfo = Session.CompilerInfo;
      SectionPanel.Session = Session;
      SectionPanel.Summary = Session.GetDocumentSummary(Section);
      SectionPanel.SelectSection(Section, true, true);
      SectionPanelHost.Visibility = Visibility.Visible;
      SectionPanelHost.Focus();
    }
  }

  private void PreviousSectionExecuted(object sender, ExecutedRoutedEventArgs e) {
    Session.SwitchToPreviousSection(Section, TextView);
    TextView.Focus();
  }

  private void NextSectionExecuted(object sender, ExecutedRoutedEventArgs e) {
    Session.SwitchToNextSection(Section, TextView);
    TextView.Focus();
  }

  private void SearchSymbolExecuted(object sender, ExecutedRoutedEventArgs e) {
    SearchSymbolImpl(false);
  }

  private void SearchSymbolAllSectionsExecuted(object sender, ExecutedRoutedEventArgs e) {
    SearchSymbolImpl(true);
  }

  private void JumpToProfiledElement(IRElement element) {
    TextView.SetCaretAtElement(element);
    double offset = TextView.TextArea.TextView.VerticalOffset;
    SyncColumnsVerticalScrollOffset(offset);
  }

  private void SearchSymbolImpl(bool searchAllSections) {
    var element = TextView.GetSelectedElement();

    if (element == null || !element.HasName) {
      return;
    }

    string symbolName = element.Name;
    var searchInfo = new SearchInfo();
    searchInfo.SearchedText = symbolName;
    searchInfo.SearchAll = searchAllSections;
    ShowSearchPanel(searchInfo);
  }

  private async void SearchPanel_SearchChanged(object sender, SearchInfo info) {
    string searchedText = info.SearchedText.Trim();

    if (searchedText.Length > 1) {
      searchResult_ = await Session.SearchSectionAsync(info, Section, TextView);

      if (!searchResult_.HasResults) {
        // Nothing found in the current document.
        info.ResultCount = 0;
        TextView.ClearSearchResults();
        return;
      }

      info.ResultCount = searchResult_.Results.Count;
      TextView.MarkSearchResults(searchResult_.Results, Colors.Khaki);

      if (searchResult_.Results.Count > 0) {
        TextView.JumpToSearchResult(searchResult_.Results[0], Colors.LightSkyBlue);
      }
    }
    else if (searchedText.Length == 0) {
      // Reset search panel and markers.
      if (info.ResultCount > 0) {
        SearchPanel.Reset();
      }

      await Session.SearchSectionAsync(info, Section, TextView);
      TextView.ClearSearchResults();
      searchResult_ = null;
    }
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private async void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
    ShowOptionsPanel();
  }

  private void ShowOptionsPanel() {
    if (optionsPanelPopup_ != null) {
      optionsPanelPopup_.ClosePopup();
      optionsPanelPopup_ = null;
      return;
    }

    FrameworkElement relativeElement = ProfileVisible ? ProfileColumns : TextView;
    optionsPanelPopup_ = OptionsPanelHostPopup.Create<DocumentOptionsPanel, DocumentSettings>(
      Settings.Clone(), relativeElement, Session,
      async (newSettings, commit) => {
        if (!newSettings.Equals(Settings)) {
          await LoadNewSettings(newSettings, true, commit);
        }

        if (commit) {
          TextView.EnableOverlayEventHandlers();
        }

        return newSettings.Clone();
      },
      () => optionsPanelPopup_ = null);
  }

  private void ShowRemarkOptionsPanel() {
    if (remarkOptionsPanelVisible_) {
      return;
    }

    // double width = Math.Max(RemarkOptionsPanel.MinimumWidth,
    //                         Math.Min(TextView.ActualWidth, RemarkOptionsPanel.DefaultWidth));
    // double height = Math.Max(RemarkOptionsPanel.MinimumHeight,
    //                          Math.Min(TextView.ActualHeight, RemarkOptionsPanel.DefaultHeight));
    // var position = new Point(RemarkOptionsPanel.LeftMargin, 0);
    //
    // remarkOptionsPanelPopup_ = new OptionsPanelHostPopup(new RemarkOptionsPanel(),
    //                                                      position, width, height, TextView,
    //                                                      remarkSettings_.Clone(), Session);
    // remarkOptionsPanelPopup_.PanelClosed += RemarkOptionsPanel_PanelClosed;
    // remarkOptionsPanelPopup_.PanelReset += RemarkOptionsPanel_PanelReset;
    // remarkOptionsPanelPopup_.SettingsChanged += RemarkOptionsPanel_SettingsChanged;
    // remarkOptionsPanelPopup_.IsOpen = true;
    remarkOptionsPanelVisible_ = true;
  }

  private async Task CloseRemarkOptionsPanel() {
    if (!remarkOptionsPanelVisible_) {
      return;
    }

    remarkOptionsPanelPopup_.IsOpen = false;
    remarkOptionsPanelPopup_.PanelClosed -= RemarkOptionsPanel_PanelClosed;
    remarkOptionsPanelPopup_.PanelReset -= RemarkOptionsPanel_PanelReset;
    remarkOptionsPanelPopup_.SettingsChanged -= RemarkOptionsPanel_SettingsChanged;

    var newSettings = (RemarkSettings)remarkOptionsPanelPopup_.Settings;
    await HandleNewRemarkSettings(newSettings, true);

    remarkOptionsPanelPopup_ = null;
    remarkOptionsPanelVisible_ = false;
  }

  private async Task HandleNewRemarkSettings(RemarkSettings newSettings, bool commit, bool force = false) {
    if (commit) {
      await Session.ReloadRemarkSettings(newSettings, TextView);
      App.Settings.RemarkSettings = newSettings;
      App.SaveApplicationSettings();
    }

    // Toolbar remark buttons change remarkSettings_ directly,
    // force an update in this case since newSettings is remarkSettings_.
    if (force || !newSettings.Equals(remarkSettings_)) {
      await ApplyRemarkSettings(newSettings);
    }
  }

  private async Task ApplyRemarkSettings(RemarkSettings newSettings) {
    // If only the remark filters changed, don't recompute the list of remarks.
    bool rebuildRemarkList = remarkList_ == null ||
                             newSettings.ShowPreviousSections &&
                             (newSettings.StopAtSectionBoundaries != remarkSettings_.StopAtSectionBoundaries ||
                              newSettings.SectionHistoryDepth != remarkSettings_.SectionHistoryDepth);
    App.Settings.RemarkSettings = newSettings;
    await UpdateRemarkSettings(newSettings);

    if (rebuildRemarkList) {
      Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Find and load remarks");
      await ReloadRemarks(loadTask_.CurrentInstance);
    }
    else {
      Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Load remarks");
      await UpdateDocumentRemarks(remarkList_);
    }
  }

  private async void RemarkOptionsPanel_SettingsChanged(object sender, EventArgs e) {
    if (remarkOptionsPanelVisible_) {
      var newSettings = (RemarkSettings)remarkOptionsPanelPopup_.Settings;

      if (newSettings != null) {
        await HandleNewRemarkSettings(newSettings, false);
        remarkOptionsPanelPopup_.Settings = remarkSettings_.Clone();
      }
    }
  }

  private async void RemarkOptionsPanel_PanelReset(object sender, EventArgs e) {
    await HandleNewRemarkSettings(new RemarkSettings(), true);
    remarkOptionsPanelPopup_.Settings = remarkSettings_.Clone();
  }

  private async void RemarkOptionsPanel_PanelClosed(object sender, EventArgs e) {
    await CloseRemarkOptionsPanel();
  }

  private void PassOutput_ScrollChanged(object sender, ScrollChangedEventArgs e) {
    PassOutputVerticalScrollChanged?.Invoke(this, (e.VerticalOffset, e.VerticalChange));
  }

  private void PassOutput_ShowBeforeOutputChanged(object sender, bool e) {
    PassOutputShowBeforeChanged?.Invoke(this, e);
  }

  private void ActionPanel_MouseEnter(object sender, MouseEventArgs e) {
    var animation = new DoubleAnimation(1, TimeSpan.FromSeconds(AnimationDuration));
    ActionPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    actionPanelHovered_ = true;
  }

  private void ActionPanel_MouseLeave(object sender, MouseEventArgs e) {
    actionPanelHovered_ = false;
  }

  private async void MenuItem_Click_1(object sender, RoutedEventArgs e) {
    if (remarkOptionsPanelVisible_) {
      await CloseRemarkOptionsPanel();
    }
    else {
      ShowRemarkOptionsPanel();
    }
  }

  private void QueryMenuItem_SubmenuOpened(object sender, RoutedEventArgs e) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(QueryMenuItem);
    QueryMenuItem.Items.Clear();

    // Append the available queries.
    var queries = Session.CompilerInfo.BuiltinQueries;

    foreach (var query in queries) {
      var item = new MenuItem {
        Header = query.Name,
        ToolTip = query.Description,
        Tag = query
      };

      item.Click += QueryMenuItem_Click;
      QueryMenuItem.Items.Add(item);
    }

    // Add back the default menu items.
    DocumentUtils.RestoreDefaultMenuItems(QueryMenuItem, defaultItems);
  }

  private void QueryMenuItem_Click(object sender, RoutedEventArgs e) {
    var menuItem = (MenuItem)sender;
    var query = (QueryDefinition)menuItem.Tag;
    var queryPanel = CreateQueryPanel();
    queryPanel.AddQuery(query);

    CreateQueryActionButtons(query.Data);
  }

  private QueryPanel CreateQueryPanel() {
    var documentHost = this;
    var position = new Point();

    if (documentHost != null) {
      double left = documentHost.ActualWidth - QueryPanel.DefaultWidth - 32;
      double top = documentHost.ActualHeight - QueryPanel.DefaultHeight - 32;
      position = new Point(left, top);
    }

    var queryPanel = new QueryPanel(position, QueryPanel.DefaultWidth, QueryPanel.DefaultHeight,
                                    documentHost, Session);
    queryPanel.PanelActivated += QueryPanel_PanelActivated;
    queryPanel.PanelTitle = "Queries";
    queryPanel.ShowAddButton = true;
    queryPanel.PopupClosed += QueryPanel_Closed;
    queryPanel.IsOpen = true;
    queryPanel.StaysOpen = true;

    SwitchActiveQueryPanel(queryPanel);
    Session.RegisterDetachedPanel(queryPanel);
    return queryPanel;
  }

  private void QueryPanel_PanelActivated(object sender, EventArgs e) {
    // Change action buttons when another query is activated.
    var panel = (QueryPanel)sender;
    SwitchActiveQueryPanel(panel);
  }

  private void SwitchActiveQueryPanel(QueryPanel panel) {
    if (activeQueryPanels_.Count > 0) {
      // Deactivate the currently active panel.
      var currentPanel = activeQueryPanels_[^1];

      if (currentPanel != panel) {
        currentPanel.IsActivePanel = false;
        mainQueryInputValue_ = null;
        SetActiveQueryPanel(panel);
      }
    }
    else {
      SetActiveQueryPanel(panel);
    }
  }

  private void SetActiveQueryPanel(QueryPanel panel) {
    // Bring to end of list, which is the top of the "stack" of panels.
    activeQueryPanels_.Remove(panel);
    activeQueryPanels_.Add(panel);
    panel.IsActivePanel = true;

    if (panel.QueryCount > 0) {
      // Update the action panel buttons.
      CreateQueryActionButtons(panel.GetQueryAt(0).Data);
    }
  }

  private void QueryPanel_Closed(object sender, EventArgs e) {
    var queryPanel = (QueryPanel)sender;
    CloseQueryPanel(queryPanel);
  }

  private void CloseQueryPanel(QueryPanel queryPanel) {
    queryPanel.PopupClosed -= QueryPanel_Closed;
    queryPanel.IsOpen = false;
    Session.UnregisterDetachedPanel(queryPanel);

    // Update the active query.
    activeQueryPanels_.Remove(queryPanel);

    if (activeQueryPanels_.Count > 0) {
      SetActiveQueryPanel(activeQueryPanels_[^1]);
    }
    else {
      RemoveQueryActionButtons();
    }
  }

  private async void TaskMenuItem_SubmenuOpened(object sender, RoutedEventArgs e) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(TaskMenuItem);
    TaskMenuItem.Items.Clear();

    foreach (var action in Session.CompilerInfo.BuiltinFunctionTasks) {
      AddFunctionTaskDefinitionMenuItem(action);
    }

    // Since first loading the scripts takes 1-2 sec,
    // temporarily add a menu entry to show initially in the menu.
    var item = new MenuItem {
      Header = "Loading scripts...",
      IsEnabled = false
    };

    TaskMenuItem.Items.Add(item);

    var scriptTasks = await Task.Run(() => Session.CompilerInfo.ScriptFunctionTasks);

    foreach (var action in scriptTasks) {
      AddFunctionTaskDefinitionMenuItem(action);
    }

    DocumentUtils.RestoreDefaultMenuItems(TaskMenuItem, defaultItems);
    TaskMenuItem.Items.Remove(item);
  }

  private void AddFunctionTaskDefinitionMenuItem(FunctionTaskDefinition action) {
    var item = new MenuItem {
      Header = action.TaskInfo.Name,
      ToolTip = action.TaskInfo.Description,
      Tag = action
    };

    item.Click += TaskActionMenuItem_Click;
    TaskMenuItem.Items.Add(item);
  }

  private async void TaskActionMenuItem_Click(object sender, RoutedEventArgs e) {
    var menuItem = (MenuItem)sender;
    var task = (FunctionTaskDefinition)menuItem.Tag;

    if (!await LoadDocumentTask(task)) {
      //? TODO: Error handling, message box
    }
  }

  private QueryPanel CreateFunctionTaskQueryPanel() {
    var documentHost = this;
    var position = new Point();

    if (documentHost != null) {
      double left = documentHost.ActualWidth - QueryPanel.DefaultWidth - 32;
      double top = documentHost.ActualHeight - QueryPanel.DefaultHeight - 32;
      position = documentHost.PointToScreen(new Point(left, top));
    }

    var queryPanel = new QueryPanel(position, QueryPanel.DefaultWidth, QueryPanel.DefaultHeight, documentHost, Session);
    Session.RegisterDetachedPanel(queryPanel);

    queryPanel.PanelTitle = "Function Tasks";
    queryPanel.ShowAddButton = false;
    queryPanel.PopupClosed += FunctionTaskPanel_PopupClosed;
    queryPanel.IsOpen = true;
    queryPanel.StaysOpen = true;
    return queryPanel;
  }

  private void AddFunctionTaskPanelButtons(QueryPanel queryPanel, IFunctionTask taskInstance, QueryData optionsData) {
    optionsData.AddButton("Execute", async (sender, value) => {
      taskInstance.LoadOptionsFromValues(optionsData);
      taskInstance.SaveOptions();
      await ExecuteFunctionTask(taskInstance, optionsData, queryPanel);
    });

    optionsData.AddButton("Reset", (sender, value) => {
      taskInstance.ResetOptions();
      taskInstance.SaveOptions();

      // Force a refresh by recreating the query panel.
      var dummyQuery = queryPanel.GetQueryAt(0);
      dummyQuery.Data = taskInstance.GetOptionsValues();
      AddFunctionTaskPanelButtons(queryPanel, taskInstance, dummyQuery.Data);
    });
  }

  private async Task ExecuteFunctionTask(IFunctionTask taskInstance, QueryData optionsData, QueryPanel queryPanel) {
    var cancelableTask = new CancelableTask();
    optionsData.ResetOutputValues();

    if (!await taskInstance.Execute(Function, TextView, cancelableTask)) {
      string description = "";

      if (taskInstance is ScriptFunctionTask scriptTask) {
        description = scriptTask.ScriptException != null ?
          scriptTask.ScriptException.Message : "";
      }

      optionsData.SetOutputWarning("Task failed to execute!", description);
    }
    else if (!string.IsNullOrEmpty(taskInstance.ResultMessage)) {
      if (taskInstance.Result) {
        optionsData.SetOutputInfo(taskInstance.ResultMessage);
      }
      else {
        optionsData.SetOutputWarning(taskInstance.ResultMessage);
      }
    }

    if (!string.IsNullOrEmpty(taskInstance.OutputText)) {
      optionsData.ReplaceButton("View Output", async (sender, value) => {
        var view = new NotesPopup(new Point(queryPanel.HorizontalOffset,
                                            queryPanel.VerticalOffset + queryPanel.Height),
                                  500, 200, null);
        view.Session = Session;
        Session.RegisterDetachedPanel(view);
        var button = (QueryButton)sender;
        button.IsEnabled = false;

        view.PanelTitle = "Function Task Output";
        view.IsOpen = true;
        view.PopupClosed += (sender, value) => {
          //? TODO: Should save size of panel and use it next time
          view.IsOpen = false;
          button.IsEnabled = true;
          Session.UnregisterDetachedPanel(view);
        };

        view.DetachPopup();
        await view.SetText(taskInstance.OutputText, Function, Section, TextView, Session);
      });
    }
  }

  private async Task<bool> LoadDocumentTask(FunctionTaskDefinition task) {
    var instance = task.CreateInstance(Session);

    if (instance == null) {
      using var centerForm = new DialogCenteringHelper(this);
      MessageBox.Show($"Failed to create function task instance for {task.TaskInfo.Name}", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Warning);
      return false;
    }

    CreateFunctionTaskOptionsPanel(task, instance);
    return true;
  }

  private void CreateFunctionTaskOptionsPanel(FunctionTaskDefinition task, IFunctionTask instance) {
    QueryData optionsValues;

    if (task.TaskInfo.HasOptionsPanel) {
      optionsValues = instance.GetOptionsValues();
    }
    else {
      optionsValues = new QueryData();
    }

    var dummyQuery = new QueryDefinition(typeof(DummyQuery),
                                         task.TaskInfo.Name, task.TaskInfo.Description);
    dummyQuery.Data = optionsValues;

    var queryPanel = CreateFunctionTaskQueryPanel();
    AddFunctionTaskPanelButtons(queryPanel, instance, optionsValues);
    queryPanel.AddQuery(dummyQuery);
  }

  private void CreateQueryActionButtons(QueryData optionsValues) {
    RemoveQueryActionButtons();
    int actionButtonIndex = 1;

    foreach (var inputValue in optionsValues.InputValues) {
      if (inputValue.IsElement) {
        ActionPanel.AddActionButton($"{actionButtonIndex}", inputValue);

        if (actionButtonIndex == 1) {
          // Attach event only once if it's needed.
          ActionPanel.ActionButtonClicked += ActionPanel_ActionButtonClicked;
          mainQueryInputValue_ = inputValue;
        }

        actionButtonIndex++;
      }
    }
  }

  private void RemoveQueryActionButtons() {
    ActionPanel.ClearActionButtons();
    mainQueryInputValue_ = null;
  }

  private void ActionPanel_ActionButtonClicked(object sender, ActionPanelButton e) {
    var inputValue = (QueryValue)e.Tag;

    if (hoveredElement_ != null) {
      inputValue.ForceValueUpdate(hoveredElement_);
    }

    if (selectedElement_ != null) {
      inputValue.ForceValueUpdate(selectedElement_);
    }
  }

  private void FunctionTaskPanel_PopupClosed(object sender, EventArgs e) {
    var queryPanel = (QueryPanel)sender;
    queryPanel.PopupClosed -= FunctionTaskPanel_PopupClosed;
    queryPanel.IsOpen = false;
    Session.UnregisterDetachedPanel(queryPanel);
  }

  private async void ActionPanel_RemarksButtonClicked(object sender, EventArgs e) {
    if (remarkPanelVisible_) {
      await HideRemarkPanel();
    }
    else {
      ShowRemarkPanel();
    }
  }

  private void CloseAllQueryPanelsMenuItem_Click(object sender, RoutedEventArgs e) {
    while (activeQueryPanels_.Count > 0) {
      CloseQueryPanel(activeQueryPanels_[0]);
    }
  }

  private void ColumnsList_ScrollChanged(object sender, ScrollChangedEventArgs e) {
    if (duringSectionSwitching_ || Math.Abs(e.VerticalChange) < double.Epsilon) {
      return;
    }

    TextView.ScrollToVerticalOffset(e.VerticalOffset);
  }

  private void JumpToProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToHottestProfiledElement();
  }

  private void JumpToHottestProfiledElement() {
    Dispatcher.BeginInvoke(() => {
      if (!HasProfileElements()) {
        return;
      }

      profileElementIndex_ = 0;
      JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
    }, DispatcherPriority.Render);
  }

  private bool HasProfileElements() {
    return ProfileVisible && profileElements_ != null && profileElements_.Count > 0;
  }

  private bool HasProfileElement(int offset) {
    return ProfileVisible && profileElements_ != null &&
           profileElementIndex_ + offset >= 0 &&
           profileElementIndex_ + offset < profileElements_.Count;
  }

  private bool HasProfiledBlock(int offset) {
    return ProfileVisible && profileBlocks_ != null &&
           profileBlockIndex_ + offset >= 0 &&
           profileBlockIndex_ + offset < profileElements_.Count;
  }

  private void JumpToNextProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToProfiledElement(-1);
  }

  private void JumpToProfiledElement(int offset) {
    if (!HasProfileElement(offset)) {
      return;
    }

    profileElementIndex_ += offset;
    JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
  }

  private void JumpToProfiledElementAt(int index) {
    profileElementIndex_ = index;
    JumpToProfiledElement(0);
  }

  private void JumpToProfiledBlock(int offset) {
    if (!HasProfiledBlock(offset)) {
      return;
    }

    profileBlockIndex_ += offset;
    JumpToProfiledBlock(profileBlocks_[profileBlockIndex_].Item1);
  }

  private void JumpToProfiledBlockAt(int index) {
    profileBlockIndex_ = index;
    JumpToProfiledBlock(0);
  }

  private void JumpToProfiledBlock(BlockIR block) {
    TextView.GoToBlock(block);
  }

  private void JumpToPreviousProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToProfiledElement(1);
  }

  private void JumpToNextProfiledBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToProfiledBlock(-1);
  }

  private void JumpToPreviousProfiledBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToProfiledBlock(1);
  }

  private void JumpToNextProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = HasProfileElement(-1);
  }

  private void JumpToPreviousProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = HasProfileElement(1);
  }

  private void JumpToNextProfiledBlockCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = HasProfiledBlock(-1);
  }

  private void JumpToPreviousProfiledBlockCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = HasProfiledBlock(1);
  }

  private async void ExportFunctionProfileExecuted(object sender, ExecutedRoutedEventArgs e) {
    await DocumentExporting.ExportToExcelFile(TextView, DocumentExporting.ExportFunctionAsExcelFile);
  }

  private async void ExportFunctionProfileHtmlExecuted(object sender, ExecutedRoutedEventArgs e) {
    await DocumentExporting.ExportToHtmlFile(TextView, DocumentExporting.ExportFunctionAsHtmlFile);
  }

  private async void ExportFunctionProfileMarkdownExecuted(object sender, ExecutedRoutedEventArgs e) {
    await DocumentExporting.ExportToMarkdownFile(TextView, DocumentExporting.ExportFunctionAsMarkdownFile);
  }

  private async void CopySelectedLinesAsHtmlExecuted(object sender, ExecutedRoutedEventArgs e) {
    await DocumentExporting.CopySelectedLinesAsHtml(TextView);
  }

  private void CopySelectedTextExecuted(object sender, ExecutedRoutedEventArgs e) {
    TextView.Copy();
  }

  private async void ViewMenuItem_OnCheckedChanged(object sender, RoutedEventArgs e) {
    await UpdateProfilingColumns();
  }

  private async void InstanceMenuItem_OnClick(object sender, RoutedEventArgs e) {
    ProfilingUtils.HandleInstanceMenuItemChanged(sender as MenuItem, InstancesMenu, profileFilter_);
    await ApplyProfileFilter();
  }

  private async void ThreadMenuItem_OnClick(object sender, RoutedEventArgs e) {
    ProfilingUtils.HandleThreadMenuItemChanged(sender as MenuItem, ThreadsMenu, profileFilter_);
    await ApplyProfileFilter();
  }

  private async void InlineeMenuItem_OnClick(object sender, RoutedEventArgs e) {
    var inlinee = ((MenuItem)sender)?.Tag as InlineeListItem;

    if (inlinee != null && inlinee.ElementWeights is {Count: > 0}) {
      // Sort by weight and bring the hottest element into view.
      var elements = inlinee.SortedElements;
      TextView.SetCaretAtElement(elements[0]);
      TextView.SelectElements(elements);
    }
  }

  private async void OpenPopupButton_Click(object sender, RoutedEventArgs e) {
    if (Section == null) {
      return; //? TODO: Button should rather be disabled
    }

    await IRDocumentPopupInstance.ShowPreviewPopup(Section.ParentFunction, "",
                                                   this, Session, profileFilter_);
  }

  private async void BackButton_Click(object sender, RoutedEventArgs e) {
    await LoadPreviousSection();
  }

  private async void NextButton_Click(object sender, RoutedEventArgs e) {
    await LoadNextSection();
  }

  private class DummyQuery : IElementQuery {
    public ISession Session { get; }

    public bool Initialize(ISession session) {
      return true;
    }

    public bool Execute(QueryData data) {
      return true;
    }
  }
}

class RemarksButtonState : INotifyPropertyChanged {
  private RemarkSettings remarkSettings_;

  public RemarksButtonState(RemarkSettings settings) {
    remarkSettings_ = settings.Clone();
  }

  public RemarkSettings Settings {
    get => remarkSettings_;
    set {
      if (!value.Equals(remarkSettings_)) {
        NotifyPropertyChanged(nameof(ShowRemarks));
        NotifyPropertyChanged(nameof(ShowPreviousSections));
      }

      remarkSettings_ = value.Clone();
    }
  }

  public bool ShowRemarks {
    get => remarkSettings_.ShowRemarks;
    set {
      if (value != remarkSettings_.ShowRemarks) {
        remarkSettings_.ShowRemarks = value;
        NotifyPropertyChanged(nameof(ShowRemarks));
      }
    }
  }

  public bool ShowPreviousSections {
    get => ShowRemarks && remarkSettings_.ShowPreviousSections;
    set {
      if (value != remarkSettings_.ShowPreviousSections) {
        remarkSettings_.ShowPreviousSections = value;
        NotifyPropertyChanged(nameof(ShowPreviousSections));
      }
    }
  }

  public event PropertyChangedEventHandler PropertyChanged;

  public void NotifyPropertyChanged(string propertyName) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }
}