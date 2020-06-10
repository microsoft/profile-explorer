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
using Client.Document;
using Core;
using Core.IR;
using ICSharpCode.AvalonEdit.Rendering;
using ProtoBuf;

namespace Client {
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

    [ProtoContract]
    public class RemarkFilterState : BindableObject
    {
        [ProtoMember(1)]
        private bool default_;
        public bool Default
        {
            get => default_;
            set => SetAndNotify(ref default_, value);
        }

        [ProtoMember(2)]
        private bool verbose_;
        public bool Verbose
        {
            get => verbose_;
            set => SetAndNotify(ref verbose_, value);
        }

        [ProtoMember(3)]
        private bool trace_;
        public bool Trace
        {
            get => trace_;
            set => SetAndNotify(ref trace_, value);
        }

        [ProtoMember(4)]
        private bool analysis_;
        public bool Analysis
        {
            get => analysis_;
            set => SetAndNotify(ref analysis_, value);
        }

        [ProtoMember(5)]
        private bool optimization_;
        public bool Optimization
        {
            get => optimization_;
            set => SetAndNotify(ref optimization_, value);
        }

        [ProtoMember(6)]
        private bool highlightRemarks_;
        public bool HighlightRemarks
        {
            get => highlightRemarks_;
            set => SetAndNotify(ref highlightRemarks_, value);
        }

        [ProtoMember(6)]
        private bool onlyCurrentSection_;
        public bool OnlyCurrentSection
        {
            get => onlyCurrentSection_;
            set => SetAndNotify(ref onlyCurrentSection_, value);
        }

        private string searchedText_;
        public string SearchedText
        {
            get => searchedText_;
            set => SetAndNotify(ref searchedText_, value);
        }

        public RemarkFilterState()
        {
            Default = true;
            Verbose = true;
            Optimization = true;
            Analysis = true;
            HighlightRemarks = true;
        }


        public bool IsAcceptedRemark(PassRemark remark, IRTextSection section)
        {
            if (OnlyCurrentSection &&
                remark.Section != section)
            {
                return false;
            }

            if(!string.IsNullOrEmpty(SearchedText))
            {
                if(!remark.RemarkText.Contains(SearchedText))
                {
                    return false;
                }
            }

            switch (remark.Kind)
            {
                case RemarkKind.Analysis:
                    {
                        return Analysis;
                    }
                case RemarkKind.Optimization:
                    {
                        return Optimization;
                    }
                case RemarkKind.Default:
                    {
                        return Default;
                    }
                case RemarkKind.Verbose:
                    {
                        return Verbose;
                    }
                case RemarkKind.Trace:
                    {
                        return Trace;
                    }
            }

            return false;
        }
    }

    public partial class IRDocumentHost : UserControl {
        ISessionManager session_;
        DocumentSettings settings_;
        Point hoverPoint_;
        IRElement hoveredElement_;
        IRElement selectedBlock_;
        SectionSearchResult searchResult_;
        bool duringSwitchSearchResults_;
        bool searchPanelVisible_;
        bool optionsPanelVisible_;

        bool remarkPanelVisible_;
        bool actionPanelVisible_;
        private Point remarkPanelLocation_;
        bool actionPanelHovered_;
        RemarkFilterState remarkFilter_;
        List<PassRemark> remarkList_;
        RemarkPanel remarkPanel_;

        static readonly double ActionPanelInitialOpacity = 0.6;
        private readonly double AnimationDuration = 0.1;

        public event EventHandler<ScrollChangedEventArgs> ScrollChanged;

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

        public IRTextSection Section => TextView.Section;
        public FunctionIR Function => TextView.Function;
        public bool DuringSectionLoading => TextView.DuringSectionLoading;

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
            OptionsPanel.PanelClosed += OptionsPanel_PanelClosed;
            OptionsPanel.PanelReset += OptionsPanel_PanelReset;
            OptionsPanel.SettingsChanged += OptionsPanel_SettingsChanged;
            SectionPanel.OpenSection += SectionPanel_OpenSection;

            SearchPanel.SearchChanged += SearchPanel_SearchChanged;
            SearchPanel.NaviateToPreviousResult += SearchPanel_NaviateToPreviousResult;
            SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
            SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;

            var hover = new MouseHoverLogic(this);
            hover.MouseHover += Hover_MouseHover;

            remarkFilter_ = new RemarkFilterState();
            remarkFilter_.PropertyChanged += RemarkFilter__PropertyChanged;
            this.DataContext = remarkFilter_;

            this.Unloaded += IRDocumentHost_Unloaded;
        }

        private void IRDocumentHost_Unloaded(object sender, RoutedEventArgs e)
        {
            if(remarkPanel_ != null)
            {
                remarkPanel_.Close();
            }
        }

        private void RemarkFilter__PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            UpdateDocumentMarginRemarks();
        }

        private void TextView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(TextView.TextArea.TextView);

            var element = TextView.GetElementAt(point);

            if (element == null)
            {
                HideActionPanel();
                HideRemarkPanel();
            }
            else if (element != hoveredElement_ &&
                    !actionPanelHovered_)
            {
                ShowActionPanel(element, useAnimation: false);
            }
        }

        private void TextView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            var point = e.GetPosition(TextView.TextArea.TextView);

            if (!actionPanelVisible_)
            {
                return;
            }

            var element = TextView.GetElementAt(point);

            if (!remarkPanelVisible_ && !actionPanelHovered_)
            {
                if (element == null || element != hoveredElement_)
                {
                    HideActionPanel();
                    HideRemarkPanel();
                }
            }
        }

        private void Hover_MouseHover(object sender, MouseEventArgs e)
        {
            if (remarkPanelVisible_ || actionPanelHovered_)
            {
                return;
            }

            var point = e.GetPosition(TextView.TextArea.TextView);
            var element = TextView.GetElementAt(point);

            if (element != null)
            {
                ShowActionPanel(element, true);
                hoveredElement_ = element;
            }
            else
            {
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

        void ShowActionPanel(IRElement element, bool useAnimation = true) {
            if (element.GetTag<RemarkTag>() == null)
            {
                HideRemarkPanel();
                HideActionPanel();
                return;
            }

            var visualLine = TextView.TextArea.TextView.GetVisualLine(element.TextLocation.Line + 1);

            if (visualLine != null) {
                var linePos = visualLine.GetVisualPosition(0, VisualYPosition.LineBottom);

                var x = Mouse.GetPosition(this).X + ActionPanel.ActualWidth / 8;
                var y = linePos.Y + DocumentToolbar.ActualHeight - 1 -
                        TextView.TextArea.TextView.ScrollOffset.Y;

                Canvas.SetLeft(ActionPanel, x);
                Canvas.SetTop(ActionPanel, y);

                ActionPanel.Visibility = Visibility.Visible;

                if (useAnimation)
                {
                    ActionPanel.Opacity = 0.0;
                    DoubleAnimation animation2 = new DoubleAnimation(ActionPanelInitialOpacity, TimeSpan.FromSeconds(AnimationDuration));
                    ActionPanel.BeginAnimation(Grid.OpacityProperty, animation2, HandoffBehavior.SnapshotAndReplace);
                }
                else
                {
                    ActionPanel.Opacity = 1.0;
                }

                actionPanelVisible_ = true;
                remarkPanelLocation_ = PointToScreen(new Point(x, y + 20));

                if (remarkPanelVisible_) {
                    // Panel already visible, update element.
                    InitializeRemarkPanel(element);
                }
            }
        }

        void HideActionPanel() {
            if(!actionPanelVisible_)
            {
                return;
            }

            DoubleAnimation animation = new DoubleAnimation(0.0, TimeSpan.FromSeconds(AnimationDuration));
            animation.Completed += (s, e) =>
            {
                ActionPanel.Visibility = Visibility.Collapsed;
            };

            ActionPanel.BeginAnimation(Grid.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            actionPanelVisible_ = false;

        }


        void ShowRemarkPanel() {
            if(remarkPanelVisible_)
            {
                return;
            }

            var element = hoveredElement_;

            if (element == null)
            {
                element = TextView.TryGetSelectedElement();
                if (element == null)
                {
                    return;
                }
            }

            remarkPanel_ = new RemarkPanel();
            remarkPanel_.Opacity = 0.0;
            remarkPanel_.Show();

            DoubleAnimation animation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(AnimationDuration));
            remarkPanel_.BeginAnimation(Window.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            remarkPanelVisible_ = true;

            InitializeRemarkPanel(element);
        }


        private void InitializeRemarkPanel(IRElement element) {
            remarkPanel_.Session = Session;
            remarkPanel_.Function = Function;
            remarkPanel_.Section = Section;
            remarkPanel_.Element = element;

            var workingArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            var transform = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            var corner = transform.Transform(new Point(remarkPanelLocation_.X, remarkPanelLocation_.Y));
            //remarkPanel_.Left = remarkPanelLocation_.X - this.ActualWidth;
            //remarkPanel_.Top = remarkPanelLocation_.Y - this.ActualHeight;

            remarkPanel_.Left = corner.X;
            remarkPanel_.Top = corner.Y;
            remarkPanel_.Initialize();
        }

        void HideRemarkPanel() {
            if(!remarkPanelVisible_)
            {
                return;
            }

            DoubleAnimation animation = new DoubleAnimation(0.0, TimeSpan.FromSeconds(AnimationDuration));
            animation.Completed += (s, e) =>
            {
                remarkPanel_.Close();
                remarkPanel_ = null;
            };

            remarkPanel_.BeginAnimation(Window.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            remarkPanelVisible_ = false;
        }

        private void OptionsPanel_SettingsChanged(object sender, bool syntaxFileChanged) {
            if (optionsPanelVisible_) {
                var newSettings = (DocumentSettings)OptionsPanel.DataContext;

                if (newSettings != null) {
                    LoadNewSettings(newSettings, syntaxFileChanged, commit: false);
                    OptionsPanel.DataContext = null;
                    OptionsPanel.DataContext = newSettings.Clone();
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
            newOptions.Reset();
            LoadNewSettings(newOptions, force: true, commit: false);
            OptionsPanel.DataContext = null;
            OptionsPanel.DataContext = newOptions;
        }

        private void OptionsPanel_PanelClosed(object sender, bool syntaxFileChanged) {
            CloseOptionsPanel(syntaxFileChanged);
        }

        void ReloadSettings() {
            TextView.Settings = settings_;
        }

        private void TextView_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
            //CloseSectionPanel();
        }

        private void SearchPanel_CloseSearchPanel(object sender, Document.SearchInfo e) {
            HideSearchPanel();
        }

        private void SearchPanel_NavigateToNextResult(object sender, Document.SearchInfo e) {
            TextView.JumpToSearchResult(searchResult_.Results[e.CurrentResult], Colors.LightSkyBlue);
        }

        private void SearchPanel_NaviateToPreviousResult(object sender, Document.SearchInfo e) {
            TextView.JumpToSearchResult(searchResult_.Results[e.CurrentResult], Colors.LightSkyBlue);
        }

        private void IRDocumentHost_PreviewKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                CloseSectionPanel();
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

            SaveSectionState(section);
            RemoveRemarks();
        }

        private void RemoveRemarks()
        {
            if(remarkList_ != null)
            {

                remarkList_ = null;
            }
        }

        private void SaveSectionState(IRTextSection section) {
            // Annotations made in diff mode are not saved right now,
            // since the text and function IR can be different than the original function.
            if (TextView.DiffModeEnabled) {
                return;
            }

            IRDocumentHostState state = new IRDocumentHostState();
            state.DocumentState = TextView.SaveState();
            state.HorizontalOffset = TextView.HorizontalOffset;
            state.VerticalOffset = TextView.VerticalOffset;
            var data = StateSerializer.Serialize<IRDocumentHostState>(state, Function);
            Session.SaveDocumentState(data, section);
            Session.SetSectionAnnotationState(section, state.HasAnnotations);
        }

        public void OnSessionSave() {
            if (Section != null) {
                SaveSectionState(Section);
            }
        }

        public async Task SwitchSearchResultsAsync(SectionSearchResult searchResults,
                                            IRTextSection section,
                                            Document.SearchInfo searchInfo) {
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

            var time = Stopwatch.StartNew();
            var allTime = Stopwatch.StartNew();

            var sections = remarkProvider.GetSectionList(Section);
            var document = Session.SessionState.FindDocument(Section);
            var remarks = remarkProvider.ExtractAllRemarks(sections, Function, document);
            var remarkDict = new Dictionary<int, PassRemark>(remarks.Count);
            remarkList_ = new List<PassRemark>(remarks.Count);

            time.Stop();

            foreach (var remark in remarks) {
                bool found = false;
                int elementLine = 0;

                //? Combine multiple remarks on the same line somehow

                foreach (var element in remark.ReferencedElements) {
                    elementLine = element.TextLocation.Line;

                    if (remarkDict.ContainsKey(elementLine)) {
                        found = true;
                        break;
                    }
                }

                remarkList_.Add(remark);
            }

            UpdateDocumentMarginRemarks();
            AddRemarkTags(remarkList_);

            allTime.Stop();

            Trace.TraceWarning($"Load Duration {time.ElapsedMilliseconds}, all {allTime.ElapsedMilliseconds}");
            // MessageBox.Show($"Load Duration {time.ElapsedMilliseconds}, all {allTime.ElapsedMilliseconds}");
        }

        void UpdateDocumentMarginRemarks()
        {
            if(remarkList_ == null)
            {
                return;
            }

            if(!remarkFilter_.HighlightRemarks)
            {
                TextView.RemoveRemarks();
                return;
            }

            var filteredList = new List<PassRemark>(remarkList_.Count);

            foreach(var remark in remarkList_)
            {
                if(remarkFilter_.IsAcceptedRemark(remark, Section))
                {
                    filteredList.Add(remark);
                }
            }

            TextView.UpdateRemarks(filteredList);
        }


        void AddRemarkTags(List<PassRemark> remarks) {
            // Remove current tags.
            Function.ForEachElement((element) => {
                element.RemoveTag<RemarkTag>();
                return true;
            });

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

            TextView.EnterDiffMode();
        }

        public void ExitDiffMode() {
            TextView.ExitDiffMode();
        }

        private void TextView_BlockSelected(object sender, IRElementEventArgs e) {
            if (e.Element != selectedBlock_) {
                selectedBlock_ = e.Element;
                BlockSelector.SelectedItem = e.Element;
            }
        }

        private void TextView_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            var blockList = new CollectionView(TextView.Blocks);
            BlockSelector.ItemsSource = blockList;
        }

        private void TextView_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            hoverPoint_ = e.GetPosition(TextView.TextArea.TextView);
            TextView.SelectElementAt(hoverPoint_);
        }

        private void MenuItem_Click(object sender, System.Windows.RoutedEventArgs e) {
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
            if (SearchButton.IsChecked.HasValue &&
                SearchButton.IsChecked.Value) {
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

        private void ShowSearchPanel(Document.SearchInfo searchInfo = null, bool searchAll = false) {
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

            var symbolName = element.NameValue.ToString();
            var searchInfo = new Document.SearchInfo();
            searchInfo.SearchedText = symbolName;
            searchInfo.SearchAll = true;
            ShowSearchPanel(searchInfo);
        }

        private async void SearchPanel_SearchChanged(object sender, Document.SearchInfo info) {
            var searchedText = info.SearchedText.Trim();

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
            OptionsPanel.DataContext = settings_.Clone();
            OptionsPanel.Visibility = Visibility.Visible;
            optionsPanelVisible_ = true;
        }

        private void CloseOptionsPanel(bool syntaxFileChanged) {
            if (!optionsPanelVisible_) {
                return;
            }

            OptionsPanel.Visibility = Visibility.Collapsed;
            var newSettings = (DocumentSettings)OptionsPanel.DataContext;
            OptionsPanel.DataContext = null;
            optionsPanelVisible_ = false;
            LoadNewSettings(newSettings, syntaxFileChanged, commit: true);
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
            DoubleAnimation animation = new DoubleAnimation(1, TimeSpan.FromSeconds(AnimationDuration));
            ActionPanel.BeginAnimation(Grid.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            actionPanelHovered_ = true;
        }

        private void ActionPanel_MouseLeave(object sender, MouseEventArgs e) {
            if (!remarkPanelVisible_) {
                DoubleAnimation animation = new DoubleAnimation(ActionPanelInitialOpacity, TimeSpan.FromSeconds(AnimationDuration));
                ActionPanel.BeginAnimation(Grid.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }

            actionPanelHovered_ = false;
        }
    }
}
