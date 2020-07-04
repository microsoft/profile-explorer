// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IRExplorer.Document;
using IRExplorerCore;
using IRExplorerCore.IR;
using ICSharpCode.AvalonEdit.Rendering;
using ProtoBuf;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using IRExplorer.OptionsPanels;
using IRExplorer.OptionsPanels;

namespace IRExplorer {
    public static class DocumentHostCommand {
        public static readonly RoutedUICommand ShowSearch =
            new RoutedUICommand("Untitled", "ShowSearch", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ToggleSearch =
            new RoutedUICommand("Untitled", "ToggleSearch", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ShowSectionList =
            new RoutedUICommand("Untitled", "ShowSectionList", typeof(IRDocumentHost));
        public static readonly RoutedUICommand PreviousSection =
            new RoutedUICommand("Untitled", "PreviousSection", typeof(IRDocumentHost));
        public static readonly RoutedUICommand NextSection =
            new RoutedUICommand("Untitled", "NextSection", typeof(IRDocumentHost));
        public static readonly RoutedUICommand SearchSymbol =
            new RoutedUICommand("Untitled", "SearchSymbol", typeof(IRDocumentHost));
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

    public partial class IRDocumentHost : UserControl {
        private const double ActionPanelInitialOpacity = 0.75;
        private readonly double AnimationDuration = 0.1;
        private bool actionPanelHovered_;
        private bool actionPanelFromClick_;
        private bool actionPanelVisible_;
        private bool duringSwitchSearchResults_;
        private IRElement hoveredElement_;
        private Point hoverPoint_;
        private bool optionsPanelVisible_;
        private bool remarkOptionsPanelVisible_;
        private IRElement remarkElement_;
        private RemarkSettings remarkSettings_;
        private RemarkPanel remarkPanel_;
        private Point remarkPanelLocation_;

        private bool remarkPanelVisible_;
        private bool searchPanelVisible_;
        private SectionSearchResult searchResult_;
        private IRElement selectedBlock_;
        private ISessionManager session_;
        private DocumentSettings settings_;
        private List<Remark> remarkList_;

        public IRDocumentHost(ISessionManager session) {
            InitializeComponent();
            ActionPanel.Visibility = Visibility.Collapsed;
            Session = session;
            Settings = App.Settings.DocumentSettings;
            PreviewKeyDown += IRDocumentHost_PreviewKeyDown;
            TextView.PreviewMouseRightButtonDown += TextView_PreviewMouseRightButtonDown;
            TextView.PreviewMouseMove += TextView_PreviewMouseMove;
            TextView.PreviewMouseDown += TextView_PreviewMouseDown;
            TextView.BlockSelected += TextView_BlockSelected;
            TextView.ElementSelected += TextView_ElementSelected;
            TextView.ElementUnselected += TextView_ElementUnselected;
            TextView.PropertyChanged += TextView_PropertyChanged;
            TextView.GotKeyboardFocus += TextView_GotKeyboardFocus;

            SectionPanel.OpenSection += SectionPanel_OpenSection;
            SearchPanel.SearchChanged += SearchPanel_SearchChanged;
            SearchPanel.NaviateToPreviousResult += SearchPanel_NaviateToPreviousResult;
            SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
            SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;
            Unloaded += IRDocumentHost_Unloaded;

            var hover = new MouseHoverLogic(this);
            hover.MouseHover += Hover_MouseHover;
            remarkSettings_ = App.Settings.RemarkSettings;
        }


        public ISessionManager Session {
            get => session_;
            set {
                session_ = value;
                TextView.Session = session_;
            }
        }

        public DocumentSettings Settings {
            get => settings_;
            set {
                settings_ = value;
                ReloadSettings();
            }
        }

        public RemarkSettings RemarkSettings {
            get => remarkSettings_;
            set {
                HandleNewRemarkSettings(value, false);
            }
        }

        public IRTextSection Section => TextView.Section;
        public FunctionIR Function => TextView.Function;
        public bool DuringSectionLoading => TextView.DuringSectionLoading;

        public event EventHandler<ScrollChangedEventArgs> ScrollChanged;

        private void IRDocumentHost_Unloaded(object sender, RoutedEventArgs e) {
            remarkPanel_?.Close();
        }

        private void TextView_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            var point = e.GetPosition(TextView.TextArea.TextView);
            var element = TextView.GetElementAt(point);

            if (element == null) {
                HideActionPanel();
                HideRemarkPanel();
            }
            else if (element != hoveredElement_ && !actionPanelHovered_) {
                ShowActionPanel(element, true);
            }
        }

        private void TextView_PreviewMouseMove(object sender, MouseEventArgs e) {
            if (!actionPanelVisible_) {
                return;
            }

            var point = e.GetPosition(TextView.TextArea.TextView);
            var element = TextView.GetElementAt(point);

            if (!remarkPanelVisible_ && !actionPanelHovered_ && !actionPanelFromClick_) {
                if (element == null || element != hoveredElement_) {
                    HideActionPanel();
                    HideRemarkPanel();
                }
            }
        }

        private void Hover_MouseHover(object sender, MouseEventArgs e) {
            if (!remarkSettings_.ShowActionButtonOnHover ||
                (remarkSettings_.ShowActionButtonWithModifier && !Utils.IsKeyboardModifierActive())) {
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

            var element = TextView.GetElementAt(point);

            if (element != null) {
                ShowActionPanel(element);
                hoveredElement_ = element;
            }
            else {
                HideActionPanel();
                hoveredElement_ = null;
            }
        }

        private void TextView_ElementUnselected(object sender, IRElementEventArgs e) {
            HideActionPanel();
            HideRemarkPanel();
        }

        private void TextView_ElementSelected(object sender, IRElementEventArgs e) {
            ShowActionPanel(e.Element);
        }

        private IRElement GetRemarkElement(IRElement element) {
            if (element.GetTag<RemarkTag>() != null) {
                return element;
            }

            // If it's an operand, check if the instr. has a remark instead.
            if (element is OperandIR op) {
                var instr = element.ParentTuple;

                if (instr.GetTag<RemarkTag>() != null) {
                    return instr;
                }
            }

            return null;
        }

        private void ShowActionPanel(IRElement element, bool fromClickEvent = false) {
            remarkElement_ = GetRemarkElement(element);

            if (remarkElement_ == null) {
                HideRemarkPanel();
                HideActionPanel();
                return;
            }

            var visualLine = TextView.TextArea.TextView.GetVisualLine(remarkElement_.TextLocation.Line + 1);

            if (visualLine != null) {
                var linePos = visualLine.GetVisualPosition(0, VisualYPosition.LineBottom);
                double x = Mouse.GetPosition(this).X + ActionPanel.ActualWidth / 8;
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
                remarkPanelLocation_ = PointToScreen(new Point(x, y + 20));

                if (remarkPanelVisible_) {
                    // Panel already visible, update element.
                    InitializeRemarkPanel(remarkElement_);
                }
            }
        }

        private void HideActionPanel() {
            if (!actionPanelVisible_) {
                return;
            }

            var animation = new DoubleAnimation(0.0, TimeSpan.FromSeconds(AnimationDuration));
            animation.Completed += (s, e) => { ActionPanel.Visibility = Visibility.Collapsed; };
            ActionPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            actionPanelVisible_ = false;
        }

        private void ShowRemarkPanel() {
            if (remarkPanelVisible_ || remarkElement_ == null) {
                return;
            }

            remarkPanel_ = new RemarkPanel();
            remarkPanel_.RemarkContextChanged += RemarkPanel__RemarkContextChanged;
            remarkPanel_.Opacity = 0.0;
            remarkPanel_.Show();

            var animation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(AnimationDuration));
            remarkPanel_.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            remarkPanelVisible_ = true;

            InitializeRemarkPanel(remarkElement_);
        }

        private void RemarkPanel__RemarkContextChanged(object sender, RemarkContext e) {
            activeRemarkContext_ = e;
            UpdateDocumentRemarks(remarkList_);
        }

        private void InitializeRemarkPanel(IRElement element) {
            remarkPanel_.Session = Session;
            remarkPanel_.Function = Function;
            remarkPanel_.Section = Section;
            remarkPanel_.RemarkFilter = remarkSettings_;
            remarkPanel_.Element = element;

            // Due to various DPI settings, setting the Window coordinates needs
            // some adjustment of the values based on the monitor.
            Point corner = Utils.CoordinatesToScreen(remarkPanelLocation_, this);
            remarkPanel_.Initialize(corner.X, corner.Y);
        }

        private void HideRemarkPanel() {
            if (!remarkPanelVisible_) {
                return;
            }

            var animation = new DoubleAnimation(0.0, TimeSpan.FromSeconds(AnimationDuration));

            animation.Completed += (s, e) => {
                remarkPanel_.Close();
                remarkPanel_ = null;
            };

            remarkPanel_.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            remarkPanelVisible_ = false;
        }

        private void OptionsPanel_SettingsChanged(object sender, EventArgs e) {
            if (optionsPanelVisible_) {
                var newSettings = (DocumentSettings)optionsPanelWindow_.Settings;

                if (newSettings != null) {
                    LoadNewSettings(newSettings, optionsPanel_.SyntaxFileChanged, false);
                    optionsPanelWindow_.Settings = null;
                    optionsPanelWindow_.Settings = newSettings.Clone();
                }
            }
        }

        private void LoadNewSettings(DocumentSettings newSettings, bool force, bool commit) {
            if (force || newSettings.HasChanges(Settings)) {
                App.Settings.DocumentSettings = newSettings;
                Settings = newSettings;
            }

            if (commit) {
                Session.ReloadDocumentSettings(newSettings, TextView);
                App.SaveApplicationSettings();
            }
        }

        private void OptionsPanel_PanelReset(object sender, EventArgs e) {
            var newOptions = new DocumentSettings();
            LoadNewSettings(newOptions, true, false);
            optionsPanelWindow_.Settings = null;
            optionsPanelWindow_.Settings = newOptions;
        }

        private void OptionsPanel_PanelClosed(object sender, EventArgs e) {
            CloseOptionsPanel(optionsPanel_.SyntaxFileChanged);
        }

        public void ReloadSettings() {
            TextView.Settings = settings_;
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

        private void IRDocumentHost_PreviewKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                CloseSectionPanel();
                HideSearchPanel();
                e.Handled = true;
            }
        }

        private void SectionPanel_ClosePanel(object sender, EventArgs e) {
            CloseSectionPanel();
        }

        private async void SectionPanel_OpenSection(object sender, OpenSectionEventArgs e) {
            SectionPanelHost.Visibility = Visibility.Collapsed;
            await Session.SwitchDocumentSection(e, Session.CurrentDocument);
            TextView.Focus();
        }

        private void CloseSectionPanel() {
            if (SectionPanelHost.Visibility == Visibility.Visible) {
                SectionPanelHost.Visibility = Visibility.Collapsed;
            }
        }

        public void UnloadSection(IRTextSection section, bool switchingActiveDocument) {
            if (!duringSwitchSearchResults_ && !switchingActiveDocument) {
                HideSearchPanel();
            }

            HideRemarkPanel();
            HideActionPanel();
            SaveSectionState(section);

            if (!switchingActiveDocument) {
                RemoveRemarks();
            }

            // Clear references to IR objects that would keep the previous function alive.
            hoveredElement_ = null;
            remarkElement_ = null;
            selectedBlock_ = null;

            if (switchingActiveDocument) {
                BlockSelector.SelectedItem = null;
                BlockSelector.ItemsSource = null;
            }
        }

        private void RemoveRemarks() {
            remarkList_ = null;
            UpdateDocumentRemarks(remarkList_);
        }

        private void SaveSectionState(IRTextSection section) {
            // Annotations made in diff mode are not saved right now,
            // since the text and function IR can be different than the original function.
            if (TextView.DiffModeEnabled) {
                return;
            }

            var state = new IRDocumentHostState();
            state.DocumentState = TextView.SaveState();
            state.HorizontalOffset = TextView.HorizontalOffset;
            state.VerticalOffset = TextView.VerticalOffset;
            var data = StateSerializer.Serialize(state, Function);
            Session.SaveDocumentState(data, section);
            Session.SetSectionAnnotationState(section, state.HasAnnotations);
        }

        public void OnSessionSave() {
            if (Section != null) {
                SaveSectionState(Section);
            }
        }

        public async Task SwitchSearchResultsAsync(SectionSearchResult searchResults, IRTextSection section,
                                                   SearchInfo searchInfo) {
            duringSwitchSearchResults_ = true;
            var openArgs = new OpenSectionEventArgs(section, OpenSectionKind.ReplaceCurrent);
            await Session.SwitchDocumentSection(openArgs, TextView);

            duringSwitchSearchResults_ = false;
            searchResult_ = searchResults;
            searchInfo.CurrentResult = 1;
            searchInfo.ResultCount = searchResults.Results.Count;
            TextView.MarkSearchResults(searchResults.Results, Colors.Khaki);
            ShowSearchPanel(searchInfo);
        }

        public void JumpToSearchResult(TextSearchResult result, int index) {
            if (index >= SearchPanel.SearchInfo.ResultCount) {
                throw new InvalidOperationException("Invalid search result index");
            }

            SearchPanel.SearchInfo.CurrentResult = index;
            TextView.JumpToSearchResult(result, Colors.LightSkyBlue);
        }

        public void LoadSectionMinimal(ParsedSection parsedSection) {
            TextView.EarlyLoadSectionSetup(parsedSection);
        }

        public async void LoadSection(ParsedSection parsedSection) {
            var data = Session.LoadDocumentState(parsedSection.Section);

            if (data != null) {
                var state = StateSerializer.Deserialize<IRDocumentHostState>(data, parsedSection.Function);
                TextView.LoadSavedSection(parsedSection, state.DocumentState);
                TextView.ScrollToHorizontalOffset(state.HorizontalOffset);
                TextView.ScrollToVerticalOffset(state.VerticalOffset);
            }
            else {
                TextView.ScrollToVerticalOffset(0);
                TextView.LoadSection(parsedSection);
            }

            await AddRemarks();
        }

        private async Task AddRemarks() {
            var remarkProvider = Session.CompilerInfo.RemarkProvider as UTCRemarkProvider;
            var task = Task.Run(() => {

                var sections = remarkProvider.GetSectionList(Section, remarkSettings_.SectionHistoryDepth,
                                                             remarkSettings_.StopAtSectionBoundaries);
                var document = Session.SessionState.FindLoadedDocument(Section);
                return remarkProvider.ExtractAllRemarks(sections, Function, document);
            });

            var remarks = await task;
            remarkList_ = remarks;

            //? TODO: Async
            AddRemarkTags(remarks);
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
            var markerRemarksGroups = new List<RemarkLineGroup>();

            if (remarkSettings_.ShowMarginRemarks) {
                var markerRemarksMap = new Dictionary<int, RemarkLineGroup>(remarks.Count);

                foreach (var remark in filteredList) {
                    if (!remark.Category.AddLeftMarginMark) {
                        continue;
                    }

                    if (remark.Section != Section) {
                        // Remark is from previous section. Accept only if user wants
                        // to see previous optimization remarks on the left margin.
                        bool isAccepted = (remark.Category.Kind == RemarkKind.Optimization &&
                                           remarkSettings_.ShowPreviousOptimizationRemarks) ||
                                           (remark.Category.Kind == RemarkKind.Analysis &&
                                            remarkSettings_.ShowPreviousAnalysisRemarks);
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
                (!remarkSettings_.ShowMarginRemarks &&
                !remarkSettings_.ShowDocumentRemarks)) {
                TextView.RemoveRemarks();
                return;
            }

            var (allRemarks, markerRemarksGroups) = await Task.Run(() => FilterDocumentRemarks(remarks));
            TextView.UpdateRemarks(allRemarks, markerRemarksGroups);
        }

        private RemarkContext activeRemarkContext_;

        //? TODO: Create a new class to do the remark finding/filtering work
        public static bool IsAcceptedRemark(Remark remark, IRTextSection section, RemarkSettings remarkSettings) {
            if (!remarkSettings.ShowPreviousSections && remark.Section != section) {
                return false;
            }

            //? TODO: Move SearchText into a state object
            if (!string.IsNullOrEmpty(remarkSettings.SearchedText)) {
                if (!remark.RemarkText.Contains(remarkSettings.SearchedText, StringComparison.InvariantCulture)) {
                    return false;
                }
            }

            var kindResult = remark.Kind switch
            {
                RemarkKind.Analysis => remarkSettings.Analysis,
                RemarkKind.Optimization => remarkSettings.Optimization,
                RemarkKind.Default => remarkSettings.Default,
                RemarkKind.Verbose => remarkSettings.Verbose,
                RemarkKind.Trace => remarkSettings.Trace,
                _ => false
            };

            if(!kindResult) {
                return false;
            }

            if(remark.Category.HasTitle && remarkSettings.HasCategoryFilters) {
                if(remarkSettings.CategoryFilter.TryGetValue(remark.Category.Title, out bool isCategoryEnabled)) {
                    return isCategoryEnabled;
                }
            }

            return true;
        }

        public bool IsAcceptedContextRemark(Remark remark, IRTextSection section, RemarkSettings remarkSettings) {
            if(!IsAcceptedRemark(remark, section, remarkSettings)) {
                return false;
            }

            //? Filter based on context - accept any children
            if (activeRemarkContext_ != null) {
                if (remark.Context != activeRemarkContext_) {
                    return false;
                }
            }

            return true;
        }

        private void RemoveRemarkTags() {
            Function.ForEachElement(element => {
                element.RemoveTag<RemarkTag>();
                return true;
            });
        }

        private void AddRemarkTags(List<Remark> remarks) {
            RemoveRemarkTags();

            foreach (var remark in remarks) {
                foreach (var element in remark.ReferencedElements) {
                    var remarkTag = element.GetOrAddTag<RemarkTag>();
                    remarkTag.Remarks.Add(remark);
                }
            }
        }

        public void EnterDiffMode() {
            if (Section != null) {
                SaveSectionState(Section);
            }

            //? TODO: Make remarks work with diff mode!
            //?  - document text must be obtained from the session manager
            RemoveRemarks();
            AddRemarks();
            TextView.EnterDiffMode();
        }

        public void ExitDiffMode() {
            TextView.ExitDiffMode();
            HideOptionalPanels();
            RemoveRemarks();
            AddRemarks();
        }

        private void HideOptionalPanels() {
            HideSearchPanel();
            HideRemarkPanel();
            HideActionPanel();
        }

        private void TextView_BlockSelected(object sender, IRElementEventArgs e) {
            if (e.Element != selectedBlock_) {
                selectedBlock_ = e.Element;
                BlockSelector.SelectedItem = e.Element;
            }
        }

        private void TextView_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            var blockList = new CollectionView(TextView.Blocks);
            BlockSelector.ItemsSource = blockList;
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
            if (e.AddedItems.Count == 1) {
                var block = e.AddedItems[0] as BlockIR;

                // If the event triggers during loading the section, while the combobox is update,
                // ignore it, otherwise it selects the first block.
                if (block != selectedBlock_ && !TextView.DuringSectionLoading) {
                    selectedBlock_ = block;
                    TextView.GoToBlock(block);
                }
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
            SearchButton.IsChecked = !SearchButton.IsChecked;
            ShowSearchExecuted(sender, e);
        }

        private void ShowSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (SearchButton.IsChecked.HasValue && SearchButton.IsChecked.Value) {
                if (!searchPanelVisible_) {
                    ShowSearchPanel();
                }
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
            SearchPanel.Reset();
            SearchPanel.Visibility = Visibility.Collapsed;
            SearchButton.IsChecked = false;
        }

        private void ShowSearchPanel(SearchInfo searchInfo = null, bool searchAll = false) {
            searchPanelVisible_ = true;
            SearchPanel.Visibility = Visibility.Visible;
            SearchPanel.Show(searchInfo, searchAll);
            SearchButton.IsChecked = true;
        }

        private void ShowSectionListExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (SectionPanelHost.Visibility == Visibility.Visible) {
                SectionPanelHost.Visibility = Visibility.Collapsed;
            }
            else {
                SectionPanel.CompilerInfo = Session.CompilerInfo;
                SectionPanel.Summary = Session.GetDocumentSummary(Section);
                SectionPanel.SelectSection(Section);
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
            var element = TextView.TryGetSelectedElement();

            if (element == null || !element.HasName) {
                return;
            }

            string symbolName = element.NameValue.ToString();
            var searchInfo = new SearchInfo();
            searchInfo.SearchedText = symbolName;
            searchInfo.SearchAll = true;
            searchInfo.SearchAllEnabled = Session.IsInDiffMode;
            ShowSearchPanel(searchInfo);
        }

        private async void SearchPanel_SearchChanged(object sender, SearchInfo info) {
            string searchedText = info.SearchedText.Trim();

            if (searchedText.Length > 1) {
                searchResult_ = await Session.SearchSectionAsync(info, Section, TextView);
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
                TextView.MarkSearchResults(new List<TextSearchResult>(), Colors.Transparent);
                searchResult_ = null;
            }
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
            if (optionsPanelVisible_) {
                CloseOptionsPanel(false);
            }
            else {
                ShowOptionsPanel();
            }
        }

        private void ShowOptionsPanel() {
            if (optionsPanelVisible_) {
                return;
            }

            var width = Math.Max(DocumentOptionsPanel.MinimumWidth,
                    Math.Min(TextView.ActualWidth, DocumentOptionsPanel.DefaultWidth));
            var height = Math.Max(DocumentOptionsPanel.MinimumHeight,
                    Math.Min(TextView.ActualHeight, DocumentOptionsPanel.DefaultHeight));
            var position = TextView.PointToScreen(new Point(TextView.ActualWidth - width, 0));

            optionsPanel_ = new DocumentOptionsPanel();
            optionsPanelWindow_ = new OptionsPanelHostWindow(optionsPanel_, position, width, height, this);

            optionsPanelWindow_.PanelClosed += OptionsPanel_PanelClosed;
            optionsPanelWindow_.PanelReset += OptionsPanel_PanelReset;
            optionsPanelWindow_.SettingsChanged += OptionsPanel_SettingsChanged;
            optionsPanelWindow_.Settings = settings_.Clone();
            optionsPanelWindow_.Show();
            optionsPanelVisible_ = true;
        }

        private void CloseOptionsPanel(bool syntaxFileChanged) {
            if (!optionsPanelVisible_) {
                return;
            }

            optionsPanelWindow_.Close();
            optionsPanelWindow_.PanelClosed -= OptionsPanel_PanelClosed;
            optionsPanelWindow_.PanelReset -= OptionsPanel_PanelReset;
            optionsPanelWindow_.SettingsChanged -= OptionsPanel_SettingsChanged;

            var newSettings = (DocumentSettings)optionsPanelWindow_.Settings;
            LoadNewSettings(newSettings, syntaxFileChanged, true);

            optionsPanel_ = null;
            optionsPanelWindow_ = null;
            optionsPanelVisible_ = false;
        }

        private OptionsPanelHostWindow remarkOptionsPanelWindow_;

        private OptionsPanelHostWindow optionsPanelWindow_;
        private DocumentOptionsPanel optionsPanel_;

        private void ShowRemarkOptionsPanel() {
            if (remarkOptionsPanelVisible_) {
                return;
            }

            var width = Math.Max(RemarkOptionsPanel.MinimumWidth,
                    Math.Min(TextView.ActualWidth, RemarkOptionsPanel.DefaultWidth));
            var height = Math.Max(RemarkOptionsPanel.MinimumHeight,
                    Math.Min(TextView.ActualHeight, RemarkOptionsPanel.DefaultHeight));
            var position = TextView.PointToScreen(new Point(RemarkOptionsPanel.LeftMargin, 0));

            remarkOptionsPanelWindow_ = new OptionsPanelHostWindow(new RemarkOptionsPanel(Session.CompilerInfo),
                                                                   position, width, height, this);
            remarkOptionsPanelWindow_.PanelClosed += RemarkOptionsPanel_PanelClosed;
            remarkOptionsPanelWindow_.PanelReset += RemarkOptionsPanel_PanelReset;
            remarkOptionsPanelWindow_.SettingsChanged += RemarkOptionsPanel_SettingsChanged;
            remarkOptionsPanelWindow_.Settings = remarkSettings_.Clone();
            remarkOptionsPanelWindow_.Show();
            remarkOptionsPanelVisible_ = true;
        }

        private async Task CloseRemarkOptionsPanel() {
            if (!remarkOptionsPanelVisible_) {
                return;
            }

            remarkOptionsPanelWindow_.Close();
            remarkOptionsPanelWindow_.PanelClosed -= RemarkOptionsPanel_PanelClosed;
            remarkOptionsPanelWindow_.PanelReset -= RemarkOptionsPanel_PanelReset;
            remarkOptionsPanelWindow_.SettingsChanged -= RemarkOptionsPanel_SettingsChanged;

            var newSettings = (RemarkSettings)remarkOptionsPanelWindow_.Settings;
            await HandleNewRemarkSettings(newSettings, true);

            remarkOptionsPanelWindow_ = null;
            remarkOptionsPanelVisible_ = false;
        }

        private async Task HandleNewRemarkSettings(RemarkSettings newSettings, bool commit) {
            if (commit) {
                Session.ReloadRemarkSettings(newSettings, TextView);
                App.Settings.RemarkSettings = newSettings;
                App.SaveApplicationSettings();
            }

            if (newSettings.Equals(remarkSettings_)) {
                return;
            }

            //? TODO: If history depth changes, remarks must be recomputed!
            bool rebuildRemarkList = newSettings.ShowPreviousSections &&
                                    (newSettings.StopAtSectionBoundaries != remarkSettings_.StopAtSectionBoundaries ||
                                     newSettings.SectionHistoryDepth != remarkSettings_.SectionHistoryDepth);
            App.Settings.RemarkSettings = newSettings;
            remarkSettings_ = newSettings;

            if (rebuildRemarkList) {
                await AddRemarks();
            }
            else {
                await UpdateDocumentRemarks(remarkList_);
            }
        }

        private async void RemarkOptionsPanel_SettingsChanged(object sender, EventArgs e) {
            if (remarkOptionsPanelVisible_) {
                var newSettings = (RemarkSettings)remarkOptionsPanelWindow_.Settings;

                if (newSettings != null) {
                    await HandleNewRemarkSettings(newSettings, false);
                    remarkOptionsPanelWindow_.Settings = null;
                    remarkOptionsPanelWindow_.Settings = remarkSettings_.Clone();
                }
            }
        }

        private async void RemarkOptionsPanel_PanelReset(object sender, EventArgs e) {
            await HandleNewRemarkSettings(new RemarkSettings(), true);
            remarkOptionsPanelWindow_.Settings = null;
            remarkOptionsPanelWindow_.Settings = remarkSettings_.Clone();
        }

        private async void RemarkOptionsPanel_PanelClosed(object sender, EventArgs e) {
            await CloseRemarkOptionsPanel();
        }

        private void TextView_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            HideActionPanel();
            HideRemarkPanel();
            ScrollChanged?.Invoke(this, e);
        }

        private void RemarkPanelButton_Checked(object sender, RoutedEventArgs e) {
            ShowRemarkPanel();
        }

        private void RemarkPanelButton_Unchecked(object sender, RoutedEventArgs e) {
            HideRemarkPanel();
        }

        private void ActionPanel_MouseEnter(object sender, MouseEventArgs e) {
            var animation = new DoubleAnimation(1, TimeSpan.FromSeconds(AnimationDuration));
            ActionPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            actionPanelHovered_ = true;
        }

        private void ActionPanel_MouseLeave(object sender, MouseEventArgs e) {
            if (!remarkPanelVisible_) {
                var animation =
                    new DoubleAnimation(ActionPanelInitialOpacity, TimeSpan.FromSeconds(AnimationDuration));

                ActionPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }

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
    }
}
