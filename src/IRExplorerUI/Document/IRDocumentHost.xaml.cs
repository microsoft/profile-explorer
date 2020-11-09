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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerUI.Document;
using IRExplorerUI.OptionsPanels;
using IRExplorerCore;
using IRExplorerCore.IR;
using ProtoBuf;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using IRExplorerUI.Query;
using IRExplorerUI.Controls;

namespace IRExplorerUI {
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
        public static readonly RoutedUICommand SearchSymbolAllSections =
            new RoutedUICommand("Untitled", "SearchSymbolAllSections", typeof(IRDocumentHost));
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

    class RemarksButtonState : INotifyPropertyChanged {
        private RemarkSettings remarkSettings_;
        private RemarkSettings previousSettings_;

        public RemarksButtonState(RemarkSettings settings) {
            remarkSettings_ = (RemarkSettings)settings.Clone();
        }

        public RemarkSettings Settings {
            get {
                return remarkSettings_;
            }
            set {
                if (!value.Equals(remarkSettings_)) {
                    NotifyPropertyChanged(nameof(ShowRemarks));
                    NotifyPropertyChanged(nameof(ShowPreviousSections));
                }

                remarkSettings_ = (RemarkSettings)value.Clone();
            }
        }

        public bool ShowRemarks {
            get {
                return remarkSettings_.ShowRemarks;
            }
            set {
                if (value != remarkSettings_.ShowRemarks) {
                    remarkSettings_.ShowRemarks = value;
                    NotifyPropertyChanged(nameof(ShowRemarks));
                }
            }
        }

        public bool ShowPreviousSections {
            get {
                return ShowRemarks && remarkSettings_.ShowPreviousSections;
            }
            set {
                if (value != remarkSettings_.ShowPreviousSections) {
                    remarkSettings_.ShowPreviousSections = value;
                    NotifyPropertyChanged(nameof(ShowPreviousSections));
                }
            }
        }

        public void NotifyPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class IRDocumentHost : UserControl {
        private const double ActionPanelInitialOpacity = 0.5;
        private const int ActionPanelHeight = 20;
        private const double ActionPanelHideTimeout = 0.5;
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

        private bool remarkPanelVisible_;
        private bool searchPanelVisible_;
        private SectionSearchResult searchResult_;
        private IRElement selectedBlock_;
        private ISession session_;
        private DocumentSettings settings_;
        private List<Remark> remarkList_;
        private RemarksButtonState remarksButtonState_;
        private RemarkContext activeRemarkContext_;
        private List<QueryPanel> activeQueryPanels_;

        public IRDocumentHost(ISession session) {
            InitializeComponent();
            ActionPanel.Visibility = Visibility.Collapsed;
            Session = session;
            Settings = App.Settings.DocumentSettings;
            activeQueryPanels_ = new List<QueryPanel>();

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
            SearchPanel.NavigateToPreviousResult += SearchPanel_NaviateToPreviousResult;
            SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
            SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;
            Unloaded += IRDocumentHost_Unloaded;

            var hover = new MouseHoverLogic(this);
            hover.MouseHover += Hover_MouseHover;

            remarkSettings_ = App.Settings.RemarkSettings;
            remarksButtonState_ = new RemarksButtonState(remarkSettings_);
            remarksButtonState_.PropertyChanged += RemarksButtonState_PropertyChanged;
            DocumentToolbar.DataContext = remarksButtonState_;
        }

        private async void RemarksButtonState_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (!remarkPanelVisible_ && !remarkOptionsPanelVisible_) {
                await HandleNewRemarkSettings(remarksButtonState_.Settings, false);
            }
        }

        public ISession Session {
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
            if (remarkPanelVisible_) {
                remarkPanel_.IsOpen = false;
                remarkPanelVisible_ = false;
                remarkPanel_ = null;
            }
        }

        private async void TextView_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            await HideRemarkPanel();

            var point = e.GetPosition(TextView.TextArea.TextView);
            var element = TextView.GetElementAt(point);

            if (element == null) {
                HideActionPanel(true);
            }
            else if (element != hoveredElement_ && !actionPanelHovered_) {
                await ShowActionPanel(element, true);
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

            //? TODO: If other panels are opened over the document, don't consider their area.

            var element = TextView.GetElementAt(point);

            if (element != null) {
                // If the panel is already showing for this element, ignore the action
                // so that it doesn't move around after the mouse cursor.
                if (element != hoveredElement_) {
                    ShowActionPanel(element);
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
                var t = Mouse.GetPosition(this);
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
                remarkPanelLocation_ = PointToScreen(new Point(x, y + ActionPanelHeight));
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

            delayedHideActionPanel_ = DelayedAction.StartNew(TimeSpan.FromSeconds(ActionPanelHideTimeout), () => {
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

        private async void RemarkPanel__PanelDetached(object sender, EventArgs e) {
            // Keep the remark panel floating over the document.
            DetachRemarkPanel();
        }

        private void DetachRemarkPanel(bool notifyPanel = false) {
            if(remarkPanel_ == null) {
                return;
            }

            Session.RegisterDetachedPanel(remarkPanel_);
            HideActionPanel();

            if(notifyPanel) {
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

        private async Task HideRemarkPanel() {
            if (!remarkPanelVisible_) {
                return;
            }

            await ResetActiveRemarkContext();
            var animation = new DoubleAnimation(0.0, TimeSpan.FromSeconds(AnimationDuration));

            animation.Completed += (s, e) => {
                if (remarkPanel_ != null) { // When section unloads, can be before animation completes.
                    remarkPanel_.IsOpen = false;
                    remarkPanel_.PopupClosed -= RemarkPanel__PanelClosed;
                    remarkPanel_.PopupDetached -= RemarkPanel__PanelDetached;
                    remarkPanel_.RemarkContextChanged -= RemarkPanel__RemarkContextChanged;
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
            await Session.SwitchDocumentSectionAsync(e, Session.CurrentDocument);
            TextView.Focus();
        }

        private void CloseSectionPanel() {
            if (SectionPanelHost.Visibility == Visibility.Visible) {
                SectionPanelHost.Visibility = Visibility.Collapsed;
            }
        }

        public async void UnloadSection(IRTextSection section, bool switchingActiveDocument) {
            if (!duringSwitchSearchResults_ && !switchingActiveDocument) {
                HideSearchPanel();
            }

            await HideRemarkPanel();
            HideActionPanel();
            SaveSectionState(section);

            if (!switchingActiveDocument) {
                await RemoveRemarks();

                // Clear references to IR objects that would keep the previous function alive.
                hoveredElement_ = null;
                selectedElement_ = null;
                remarkElement_ = null;
                selectedBlock_ = null;
                BlockSelector.SelectedItem = null;
                BlockSelector.ItemsSource = null;
            }
        }

        private async Task RemoveRemarks() {
            remarkList_ = null;
            activeRemarkContext_ = null;
            await UpdateDocumentRemarks(remarkList_);
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
            // Ensure the right section is being displayed.
            duringSwitchSearchResults_ = true;
            var openArgs = new OpenSectionEventArgs(section, OpenSectionKind.ReplaceCurrent);
            await Session.SwitchDocumentSectionAsync(openArgs, TextView);
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

        public void LoadSectionMinimal(ParsedIRTextSection parsedSection) {
            TextView.EarlyLoadSectionSetup(parsedSection);
        }

        public async Task LoadSection(ParsedIRTextSection parsedSection) {
            var data = Session.LoadDocumentState(parsedSection.Section);

            if (data != null) {
                var state = StateSerializer.Deserialize<IRDocumentHostState>(data, parsedSection.Function);
                await TextView.LoadSavedSection(parsedSection, state.DocumentState);
                TextView.ScrollToHorizontalOffset(state.HorizontalOffset);
                TextView.ScrollToVerticalOffset(state.VerticalOffset);
            }
            else {
                TextView.ScrollToVerticalOffset(0);
                await TextView.LoadSection(parsedSection);
            }

            await ReloadRemarks();
        }

        private async Task ReloadRemarks() {
            await RemoveRemarks();
            remarkList_ = await FindRemarks();
            await AddRemarks(remarkList_);
        }

        private async Task<List<Remark>> FindRemarks() {
            var remarkProvider = Session.CompilerInfo.RemarkProvider;
            return await Task.Run(() => {
                var sections = remarkProvider.GetSectionList(Section, remarkSettings_.SectionHistoryDepth,
                                                             remarkSettings_.StopAtSectionBoundaries);
                var document = Session.SessionState.FindLoadedDocument(Section);
                var options = new RemarkProviderOptions();
                return remarkProvider.ExtractAllRemarks(sections, Function, document, options);
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
                TextView.RemoveRemarks(); // No remarks or disabled.
                return;
            }

            var (allRemarks, markerRemarksGroups) = await Task.Run(() => FilterDocumentRemarks(remarks));
            TextView.UpdateRemarks(allRemarks, markerRemarksGroups, activeRemarkContext_ != null);
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

            var kindResult = remark.Kind switch
            {
                RemarkKind.Analysis => remarkSettings.Analysis,
                RemarkKind.Optimization => remarkSettings.Optimization,
                RemarkKind.Default => remarkSettings.Default,
                RemarkKind.Verbose => remarkSettings.Verbose,
                RemarkKind.Trace => remarkSettings.Trace,
                _ => false
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
            //if (activeRemarkContext_ != null) {
            //    return IsActiveContextTreeRemark(remark);
            //}

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

        public async Task EnterDiffMode() {
            if (Section != null) {
                SaveSectionState(Section);
            }

            TextView.EnterDiffMode();
        }

        public async Task ExitDiffMode() {
            TextView.ExitDiffMode();
            await HideOptionalPanels();
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
                    var info = new SearchInfo();

                    // Use selected text as initial search input.
                    if (TextView.SelectionLength > 1) {
                        info.SearchedText = TextView.SelectedText;
                        info.IsCaseInsensitive = true;
                    }

                    ShowSearchPanel(info);
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
            SearchPanel.Hide();
            SearchPanel.Reset();
            SearchPanel.Visibility = Visibility.Collapsed;
            SearchButton.IsChecked = false;
        }

        private void ShowSearchPanel(SearchInfo searchInfo, bool searchAll = false) {
            searchInfo.SearchAllEnabled = !Session.IsInDiffMode;

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
            SearchSymbolImpl(false);
        }

        private void SearchSymbolAllSectionsExecuted(object sender, ExecutedRoutedEventArgs e) {
            SearchSymbolImpl(true);
        }

        private void SearchSymbolImpl(bool searchAllSections) {
            var element = TextView.TryGetSelectedElement();

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
            optionsPanelWindow_.IsOpen = true;
            optionsPanelVisible_ = true;
        }

        private void CloseOptionsPanel(bool syntaxFileChanged) {
            if (!optionsPanelVisible_) {
                return;
            }

            optionsPanelWindow_.IsOpen = false;
            ;
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
        private DelayedAction delayedHideActionPanel_;

        private void ShowRemarkOptionsPanel() {
            if (remarkOptionsPanelVisible_) {
                return;
            }

            var width = Math.Max(RemarkOptionsPanel.MinimumWidth,
                    Math.Min(TextView.ActualWidth, RemarkOptionsPanel.DefaultWidth));
            var height = Math.Max(RemarkOptionsPanel.MinimumHeight,
                    Math.Min(TextView.ActualHeight, RemarkOptionsPanel.DefaultHeight));
            var position = TextView.PointToScreen(new Point(RemarkOptionsPanel.LeftMargin, 0));

            remarkOptionsPanelWindow_ = new OptionsPanelHostWindow(new RemarkOptionsPanel(),
                                                                   position, width, height, this);
            remarkOptionsPanelWindow_.PanelClosed += RemarkOptionsPanel_PanelClosed;
            remarkOptionsPanelWindow_.PanelReset += RemarkOptionsPanel_PanelReset;
            remarkOptionsPanelWindow_.SettingsChanged += RemarkOptionsPanel_SettingsChanged;
            remarkOptionsPanelWindow_.Settings = remarkSettings_.Clone();
            remarkOptionsPanelWindow_.IsOpen = true;
            remarkOptionsPanelVisible_ = true;
        }

        private async Task CloseRemarkOptionsPanel() {
            if (!remarkOptionsPanelVisible_) {
                return;
            }

            remarkOptionsPanelWindow_.IsOpen = false;
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

            await ApplyRemarkSettings(newSettings);
        }

        private async Task ApplyRemarkSettings(RemarkSettings newSettings) {
            // If only the remark filters changed, don't recompute the list of remarks.
            bool rebuildRemarkList = remarkList_ == null ||
                                    (newSettings.ShowPreviousSections &&
                                    (newSettings.StopAtSectionBoundaries != remarkSettings_.StopAtSectionBoundaries ||
                                     newSettings.SectionHistoryDepth != remarkSettings_.SectionHistoryDepth));
            App.Settings.RemarkSettings = newSettings;
            remarkSettings_ = newSettings;
            remarksButtonState_.Settings = newSettings;

            if (rebuildRemarkList) {
                Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Find and load remarks");
                await ReloadRemarks();
            }
            else {
                Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Load remarks");
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

        private async void TextView_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            HideActionPanel();
            DetachRemarkPanel(true);
            ScrollChanged?.Invoke(this, e);
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

        public async Task LoadDiffedFunction(DiffMarkingResult diffResult, IRTextSection newSection) {
            await TextView.LoadDiffedFunction(diffResult, newSection);
            await ReloadRemarks();
        }

        private void QueryMenuItem_SubmenuOpened(object sender, RoutedEventArgs e) {
            var defaultItems = SaveDefaultMenuItems(QueryMenuItem);
            QueryMenuItem.Items.Clear();

            // Append the available queries.
            var queries = Session.CompilerInfo.BuiltinQueries;

            foreach (var query in queries) {
                var item = new MenuItem() {
                    Header = query.Name,
                    ToolTip = query.Description,
                    Tag = query
                };

                item.Click += QueryMenuItem_Click;
                QueryMenuItem.Items.Add(item);
            }

            // Add back the default menu items.
            RestoreDefaultMenuItems(QueryMenuItem, defaultItems);
        }

        private List<object> SaveDefaultMenuItems(MenuItem menu) {
            // Save the menu items that are always present, they are either
            // separators or menu items without an object tag.
            var defaultItems = new List<object>();

            foreach (var item in menu.Items) {
                if (item is MenuItem menuItem) {
                    if (menuItem.Tag == null) {
                        defaultItems.Add(item);
                    }
                }
                else if (item is Separator) {
                    defaultItems.Add(item);
                }
            }

            return defaultItems;
        }

        private void RestoreDefaultMenuItems(MenuItem menu, List<object> defaultItems) {
            defaultItems.ForEach(item => menu.Items.Add(item));
        }

        private void QueryMenuItem_Click(object sender, System.Windows.RoutedEventArgs e) {
            var menuItem = (MenuItem)sender;
            var query = (QueryDefinition)menuItem.Tag;
            var queryPanel = CreateQueryPanel();
            queryPanel.AddQuery(query);

            CreateQueryActionButtons(query.Data);
        }


        private QueryPanel CreateQueryPanel() {
            //? TODO: Create panel over the document
            var documentHost = this;
            var position = new Point();

            if (documentHost != null) {
                var left = documentHost.ActualWidth - QueryPanel.DefaultWidth - 32;
                var top = documentHost.ActualHeight - QueryPanel.DefaultHeight - 32;
                position = documentHost.PointToScreen(new Point(left, top));
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
                var currentPanel = activeQueryPanels_[^1];

                if (currentPanel != panel) {
                    currentPanel.IsActivePanel = false;
                    SetActiveQueryPanel(panel);
                }
            }
            else {
                SetActiveQueryPanel(panel);
            }
        }

        private void SetActiveQueryPanel(QueryPanel panel) {
            activeQueryPanels_.Remove(panel); // Bring to end of list.
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

        private void TaskMenuItem_SubmenuOpened(object sender, RoutedEventArgs e) {
            var defaultItems = SaveDefaultMenuItems(TaskMenuItem);
            TaskMenuItem.Items.Clear();

            foreach (var action in Session.CompilerInfo.BuiltinFunctionTasks) {
                AddFunctionTaskDefinitionMenuItem(action);
            }

            foreach (var action in Session.CompilerInfo.ScriptFunctionTasks) {
                AddFunctionTaskDefinitionMenuItem(action);
            }

            RestoreDefaultMenuItems(TaskMenuItem, defaultItems);
        }

        private void AddFunctionTaskDefinitionMenuItem(FunctionTaskDefinition action) {
            var item = new MenuItem() {
                Header = action.TaskInfo.Name,
                ToolTip = action.TaskInfo.Description,
                Tag = action
            };

            item.Click += TaskActionMenuItem_Click;
            TaskMenuItem.Items.Add(item);
        }

        private async void TaskActionMenuItem_Click(object sender, System.Windows.RoutedEventArgs e) {
            var menuItem = (MenuItem)sender;
            var task = (FunctionTaskDefinition)menuItem.Tag;

            if (!await LoadDocumentTask(task)) {
                //? TODO: Error handling, message box
            }
        }

        class DummyQuery : IElementQuery {
            public ISession Session { get; }

            public bool Initialize(ISession session) {
                return true;
            }

            public bool Execute(QueryData data) {
                return true;
            }
        }

        private QueryPanel CreateFunctionTaskQueryPanel() {
            var documentHost = this;
            var position = new Point();

            if (documentHost != null) {
                var left = documentHost.ActualWidth - QueryPanel.DefaultWidth - 32;
                var top = documentHost.ActualHeight - QueryPanel.DefaultHeight - 32;
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
                await ExecuteFunctionTask(taskInstance, optionsData, queryPanel);
            });

            optionsData.AddButton("Reset", (sender, value) => {
                taskInstance.ResetOptions();
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
                MessageBox.Show($"Failed to create function task instance for {task.TaskInfo.Name}", "IR Explorer",
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

                    if(actionButtonIndex == 1) {
                        ActionPanel.ActionButtonClicked += ActionPanel_ActionButtonClicked;
                    }

                    actionButtonIndex++;
                }
            }
        }

        private void RemoveQueryActionButtons() {
            ActionPanel.ClearActionButtons();
        }

        private void ActionPanel_ActionButtonClicked(object sender, ActionPanelButton e) {
            var inputValue = (QueryValue)e.Tag;

            if (hoveredElement_ != null) {
                inputValue.Value = hoveredElement_;
            }
            if (selectedElement_ != null) {
                inputValue.Value = selectedElement_;
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
            while(activeQueryPanels_.Count > 0) {
                CloseQueryPanel(activeQueryPanels_[0]);
            }
        }
    }
}
