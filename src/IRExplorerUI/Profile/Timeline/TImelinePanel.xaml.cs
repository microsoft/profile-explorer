using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerCore.Utilities;

namespace IRExplorerUI.Profile;

public partial class TimelinePanel : ToolPanelControl, IFunctionProfileInfoProvider, INotifyPropertyChanged {
    private const double TimePerFrame = 1000.0 / 60; // ~16.6ms per frame at 60Hz.
    private const double ZoomAmount = 500;
    private const double ScrollWheelZoomAmount = 300;
    private const double FastPanOffset = 1000;
    private const double DefaultPanOffset = 100;
    private const double ZoomAnimationDuration = TimePerFrame * 10;
    private const double EnlargeAnimationDuration = TimePerFrame * 12;
    private const double ScrollWheelZoomAnimationDuration = TimePerFrame * 8;
    
    private FlameGraphSettings settings_;
    private bool panelVisible_;
    private ProfileCallTree callTree_;
    private ProfileCallTree pendingCallTree_; // Tree to show when panel becomes visible.
    private List<FlameGraphNode> searchResultNodes_;
    private int searchResultIndex_;
    private List<ActivityTimelineView> threadActivityViews_;
    private Dictionary<int, ActivityTimelineView> threadActivityViewsMap_;
    private Dictionary<int, DraggablePopupHoverPreview> threadHoverPreviewMap_;

    private DoubleAnimation widthAnimation_;
    private DoubleAnimation zoomAnimation_;

    public TimelinePanel() {
        InitializeComponent();
        settings_ = App.Settings.FlameGraphSettings;
        SetupEvents();
        DataContext = this;
    }

    public override ToolPanelKind PanelKind => ToolPanelKind.Timeline;
    
    public override ISession Session {
        get => base.Session;
        set {
            base.Session = value;
            NodeDetailsPanel.Initialize(value, this);
        }
    }

    public bool PrependModuleToFunction {
        get => settings_.PrependModuleToFunction;
        set {
            if (value != settings_.PrependModuleToFunction) {
                settings_.PrependModuleToFunction = value;
                //? GraphViewer.SettingsUpdated(settings_);
                OnPropertyChanged();
            }
        }
    }

    public bool SyncSourceFile {
        get => settings_.SyncSourceFile;
        set {
            if (value != settings_.SyncSourceFile) {
                settings_.SyncSourceFile = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseCompactMode {
        get => settings_.UseCompactMode;
        set {
            if (value != settings_.UseCompactMode) {
                settings_.UseCompactMode = value;
                //? GraphViewer.SettingsUpdated(settings_);
                OnPropertyChanged();
            }
        }
    }

    private bool showSearchSection_;
    public bool ShowSearchSection {
        get => showSearchSection_;
        set {
            if (showSearchSection_ != value) {
                showSearchSection_ = value;
                OnPropertyChanged();
            }
        }
    }

    private string searchResultText_;
    public string SearchResultText {
        get => searchResultText_;
        set {
            if (searchResultText_ != value) {
                searchResultText_ = value;
                OnPropertyChanged();
            }
        }
    }

    public override void OnShowPanel() {
        base.OnShowPanel();
        panelVisible_ = true;
        InitializePendingCallTree();
    }

    private void SchedulePendingCallTree(ProfileCallTree callTree) {
        // Display flame graph once the panel is visible and visible area is valid.
        if (pendingCallTree_ == null) {
            pendingCallTree_ = callTree;
            InitializePendingCallTree();
        }
    }

    private void InitializePendingCallTree() {
        if (pendingCallTree_ != null && panelVisible_) {
            InitializeCallTree(pendingCallTree_);
            pendingCallTree_ = null;
        }
    }

    private void InitializeCallTree(ProfileCallTree callTree) {
        callTree_ = callTree;

        Dispatcher.BeginInvoke(async () => {
                if (threadActivityViews_ != null) {
                    return;
                }

                var activityArea = new Rect(0, 0, ActivityView.ActualWidth, ActivityView.ActualHeight);
                var threads = Session.ProfileData.SortedThreadWeights;
                await ActivityView.Initialize(Session.ProfileData, activityArea);
                ActivityView.IsTimeBarVisible = true;
                ActivityView.SampleBorderColor = ColorPens.GetPen(Colors.DimGray);
                ActivityView.SamplesBackColor = Brushes.DeepSkyBlue;

                var threadActivityArea = new Rect(0, 0, ActivityView.ActualWidth, 30);
                threadActivityViews_ = new List<ActivityTimelineView>();
                threadActivityViewsMap_ = new Dictionary<int, ActivityTimelineView>();
                int limit = 0;

                foreach (var thread in threads) {
                    if (limit++ > 30)
                        break;

                    var threadView = new ActivityTimelineView();
                    SetupActivityViewEvents(threadView.ActivityHost);
                    threadView.ActivityHost.BackColor = Brushes.WhiteSmoke;
                    threadView.ActivityHost.SampleBorderColor = ColorPens.GetPen(Colors.DimGray);
                    threadView.ActivityHost.SamplesBackColor = ColorBrushes.GetBrush(ColorUtils.GeneratePastelColor((uint)thread.ThreadId));

                     var threadInfo = Session.ProfileData.FindThread(thread.ThreadId);
                    
                     if (threadInfo != null && threadInfo.HasName) {
                         var backColor = ColorUtils.GeneratePastelColor((uint)threadInfo.Name.GetHashCode());
                         threadView.Margin.Background = ColorBrushes.GetBrush(backColor);
                    }

                    threadActivityViews_.Add(threadView);
                    threadActivityViewsMap_[thread.ThreadId] = threadView;
                    await threadView.ActivityHost.Initialize(Session.ProfileData, threadActivityArea, thread.ThreadId);
                    SetupActivityHoverPreview(threadView.ActivityHost);

                //threadView.TimelineHost.Session = Session;
                //threadView.TimelineHost.InitializeTimeline(callTree, thread.ThreadId);
                //SetupTimelineViewEvents(threadView.TimelineHost);
                }
                
            ActivityViewList.ItemsSource = new CollectionView(threadActivityViews_);
        }, DispatcherPriority.Background);
    }

    public override void OnSessionStart() {
        base.OnSessionStart();
        InitializePendingCallTree();
    }

    private void SetupEvents() {
        // Setup events for the node details view.
        NodeDetailsPanel.NodeInstanceChanged += NodeDetailsPanel_NodeInstanceChanged;
        NodeDetailsPanel.BacktraceNodeClick += NodeDetailsPanel_NodeClick;
        NodeDetailsPanel.BacktraceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
        NodeDetailsPanel.InstanceNodeClick += NodeDetailsPanel_NodeClick;
        NodeDetailsPanel.InstanceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
        NodeDetailsPanel.FunctionNodeClick += NodeDetailsPanel_NodeClick;
        NodeDetailsPanel.FunctionNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
        NodeDetailsPanel.NodesSelected += NodeDetailsPanel_NodesSelected;

        SetupActivityViewEvents(ActivityView);
        SetupActivityHoverPreview(ActivityView);
    }

    private void SetupActivityHoverPreview(ActivityView view) {
        var preview = new DraggablePopupHoverPreview(view,
            CallTreeNodePopup.PopupHoverLongDuration,
            (mousePoint, previewPoint) => {
                var timePoint = view.CurrentTimePoint;
                ProfileCallTreeNode callNode = null;

                if (timePoint.SampleIndex < Session.ProfileData.Samples.Count) {
                    var filter = new ProfileSampleFilter() {
                        TimeRange = new SampleTimeRangeInfo(timePoint.Time, timePoint.Time,
                            Math.Max(0, timePoint.SampleIndex - 20),
                            Math.Min(timePoint.SampleIndex + 20, Session.ProfileData.Samples.Count), timePoint.ThreadId),
                        ThreadIds = timePoint.ThreadId != -1 ? new List<int>() { timePoint.ThreadId } : null
                    };

                    var rangeProfile = Session.ProfileData.ComputeFunctionProfile(Session.ProfileData, filter, 1);
                    var funcs = rangeProfile.GetSortedFunctions();

                    if (funcs.Count > 0) {
                        var nodes = rangeProfile.CallTree.GetCallTreeNodes(funcs[0].Item1);
                        callNode = nodes[0];
                    }
                }

                if (callNode != null) {
                    // If popup already opened for this node reuse the instance.
                    if (threadHoverPreviewMap_.TryGetValue(view.ThreadId, out var hoverPreview) &&
                        hoverPreview.PreviewPopup is CallTreeNodePopup popup) {
                        popup.UpdatePosition(previewPoint, view);
                        popup.UpdateNode(callNode);
                        return popup;
                    }

                    return new CallTreeNodePopup(callNode, this, previewPoint, 350, 68, view, Session);
                }

                return null;
            },
            (mousePoint, popup) => {
                if (popup is CallTreeNodePopup previewPopup) {
                    return true;
                }

                return true;
            },
            popup => {
                Session.RegisterDetachedPanel(popup);
            });
        threadHoverPreviewMap_ ??= new Dictionary<int, DraggablePopupHoverPreview>();
        threadHoverPreviewMap_[view.ThreadId] = preview;
    }

    private void SetupActivityViewEvents(ActivityView view) {
        view.SelectedTimeRange += ActivityView_OnSelectedTimeRange;
        view.SelectingTimeRange += ActivityView_OnSelectingTimeRange;
        view.FilteredTimeRange += ActivityView_FilteredTimeRange;
        view.ClearedSelectedTimeRange += ActivityView_ClearedSelectedTimeRange;
        view.ClearedFilteredTimeRange += ActivityView_ClearedFilteredTimeRange;
        view.ThreadIncludedChanged += View_ThreadIncludedChanged;
    }

    bool changingThreadFiltering_;

    private async void View_ThreadIncludedChanged(object sender, bool included) {
        var view = sender as ActivityView;
        
        if (included) {
            if (ActivityView.HasFilter) {
                view.FilterTimeRange(ActivityView.FilteredRange);
            }
            else {
                view.ClearTimeRangeFilter();
            }
        }
        else {
            view.FilterAllOut();
        }

        if (!changingThreadFiltering_) {
            SampleTimeRangeInfo timeRange = ActivityView.HasFilter ? ActivityView.FilteredRange : null;
            var filter = ConstructSampleFilter(timeRange);
            await Session.FilterProfileSamples(filter);
        }
    }

    private void SetupTimelineViewEvents(FlameGraphHost view) {
        view.MaxWidthChanged += TimelineView_MaxWidthChanged;
        view.HorizontalOffsetChanged += TimelineView_HorizontalOffsetChanged;
    }

    private void TimelineView_HorizontalOffsetChanged(object sender, double offset) {
        ActivityScrollBar.ScrollToHorizontalOffset(offset);
    }

    private void TimelineView_MaxWidthChanged(object sender, double width) {
        SetMaxWidth(width, sender);
    }

    private async void ActivityView_ClearedFilteredTimeRange(object sender, EventArgs e) {
        var view = sender as ActivityView;
        changingThreadFiltering_ = true;

        if (view.IsSingleThreadView) {
            ActivityView.ClearTimeRangeFilter();
        }

        if (threadActivityViews_ != null) {
            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost != view) {
                    threadView.ActivityHost.ClearTimeRangeFilter();
                }
            }
        }
        
        changingThreadFiltering_ = false;
        await Session.RemoveProfileSamplesFilter();
    }

    private async void ActivityView_FilteredTimeRange(object sender, SampleTimeRangeInfo range) {
        var view = sender as ActivityView;
        changingThreadFiltering_ = true;
        
        if (view.IsSingleThreadView) {
            ActivityView.FilterTimeRange(range);

            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost != view) {
                    threadView.ActivityHost.FilterAllOut();
                }
            }
        }
        else {
            if (threadActivityViews_ != null) {
                foreach (var threadView in threadActivityViews_) {
                    threadView.ActivityHost.FilterTimeRange(range);
                }
            }
        }

        var filter = ConstructSampleFilter(range);
        changingThreadFiltering_ = false;
        await Session.FilterProfileSamples(filter);
    }

    private ProfileSampleFilter ConstructSampleFilter(SampleTimeRangeInfo timeRange) {
        var filter = new ProfileSampleFilter();
        filter.TimeRange = timeRange;
        bool hasExludedThreads = false;

        foreach (var threadView in threadActivityViews_) {
            if (!threadView.ActivityHost.IsThreadIncluded) {
                hasExludedThreads = true;
                break;
            }
        }

        if (hasExludedThreads) {
            filter.ThreadIds = new List<int>();

            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost.IsThreadIncluded) {
                    filter.ThreadIds.Add(threadView.ActivityHost.ThreadId);
                }
            }
        }

        return filter;
    }

    private async void ActivityView_ClearedSelectedTimeRange(object sender, EventArgs e) {
        var view = sender as ActivityView;

        if (view.IsSingleThreadView) {
            ActivityView.ClearSelectedTimeRange();
        }

        if (threadActivityViews_ != null) {
            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost != view) {
                    threadView.ActivityHost.ClearSelectedTimeRange();
                }
            }
        }
        
        await Session.ProfileSampleRangeDeselected();
    }

    private void ActivityView_OnSelectedTimeRange(object sender, SampleTimeRangeInfo range) {
        Trace.WriteLine($"Selected {range.StartTime} / {range.EndTime}, range {range.StartSampleIndex}-{range.EndSampleIndex}");

        var view = sender as ActivityView;

        if (view.IsSingleThreadView) {
            ActivityView.SelectTimeRange(range);

            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost != view) {
                    threadView.ActivityHost.ClearSelectedTimeRange();
                }
            }
        }
        else {
            if (threadActivityViews_ != null) {
                foreach (var threadView in threadActivityViews_) {
                    threadView.ActivityHost.SelectTimeRange(range);
                }
            }
        }

        Session.ProfileSampleRangeSelected(range);
    }

    private void ActivityView_OnSelectingTimeRange(object sender, SampleTimeRangeInfo range) {
        var view = sender as ActivityView;

        if (view.IsSingleThreadView) {
            ActivityView.SelectTimeRange(range);

            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost != view) {
                    threadView.ActivityHost.ClearSelectedTimeRange();
                }
            }
        }
        else {
            if (threadActivityViews_ != null) {
                foreach (var threadView in threadActivityViews_) {
                    threadView.ActivityHost.SelectTimeRange(range);
                }
            }
        }
    }

    private void NodeDetailsPanel_NodesSelected(object sender, List<ProfileCallTreeNode> e) {
        //var nodes = GraphViewer.SelectNodes(e);
        //if (nodes.Count > 0) {
        //    BringNodeIntoView(nodes[0], false);
        //}
    }

    private async void NodeDetailsPanel_NodeInstanceChanged(object sender, ProfileCallTreeNode e) {
        //var node = GraphViewer.SelectNode(e);
        //BringNodeIntoView(node);
    }

    private async void NodeDetailsPanel_NodeClick(object sender, ProfileCallTreeNode e) {
        //var nodes = GraphViewer.SelectNodes(e);
        //if (nodes.Count > 0) {
        //    BringNodeIntoView(nodes[0], false);
        //}
    }

    private async void NodeDetailsPanel_NodeDoubleClick(object sender, ProfileCallTreeNode e) {
        await OpenFunction(e);
    }
    
    private async Task OpenFunction(ProfileCallTreeNode node) {
        if (node != null && node.Function.HasSections) {
            var openMode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTabDockRight : OpenSectionKind.ReplaceCurrent;
            var args = new OpenSectionEventArgs(node.Function.Sections[0], openMode);
            await Session.SwitchDocumentSectionAsync(args);
        }
    }
    
    private bool showNodePanel_;
        public bool ShowNodePanel {
        get => showNodePanel_;
        set => SetField(ref showNodePanel_, value);
    }
    
    private void CancelWidthAnimation() {
        if(widthAnimation_ != null) {
            widthAnimation_.BeginTime = null;
            //BeginAnimation(FlameGraphWidthProperty, widthAnimation_);
            widthAnimation_ = null;
        }
    }

    private void CancelZoomAnimation() {
        if (zoomAnimation_ != null) {
            zoomAnimation_.BeginTime = null;
            //BeginAnimation(FlameGraphWidthProperty, zoomAnimation_);
            zoomAnimation_ = null;
        }
    }
    
    private void ExecuteGraphResetWidth(object sender, ExecutedRoutedEventArgs e) {
        //? TODO: Buttons should be disabled
    }
    
    private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
        AdjustMaxWidth(FlameGraphHost.ZoomAmount);
    }

    private void AdjustMaxWidth(double amount) {
        var newWidth = ActivityView.MaxWidth + amount;
        SetMaxWidth(newWidth);
    }

    private void SetMaxWidth(double newWidth, object source = null) {
        Trace.WriteLine($"New width {newWidth}");
        ActivityView.SetMaxWidth(newWidth);
        ScrollElement.Width = newWidth + ActivityViewHeader.ActualWidth;
        
        if (threadActivityViews_ != null)
        {
            foreach (var threadView in threadActivityViews_)
            {
                threadView.ActivityHost.SetMaxWidth(newWidth);

                if (source != threadView.TimelineHost) {
                    threadView.TimelineHost.SetMaxWidth(newWidth, false);
                }
            }
        }
    }

    private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
        AdjustMaxWidth(-FlameGraphHost.ZoomAmount);
    }
    
    private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
        throw new NotImplementedException();
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
        Utils.PatchToolbarStyle(sender as ToolBar);
    }

    private void GraphHost_OnScrollChanged(object sender, ScrollChangedEventArgs e) {
        double offset = ActivityScrollBar.HorizontalOffset;
        ActivityView.SetHorizontalOffset(offset);

        if (threadActivityViews_ != null) {
            foreach (var view in threadActivityViews_) {
                view.ActivityHost.SetHorizontalOffset(offset);
                view.TimelineHost.SetHorizontalOffset(offset);
            }
        }
    }

    private async void UndoButtoon_Click(object sender, RoutedEventArgs e) {
        //await RestorePreviousState();
    }
    
    public async Task DisplayFlameGraph() {
        var callTree = Session.ProfileData.CallTree;
        SchedulePendingCallTree(callTree);
    }

    public override void OnSessionEnd() {
        base.OnSessionEnd();
        callTree_ = null;
        pendingCallTree_ = null;
    }

    public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
        return callTree_.GetBacktrace(node);
    }

    public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
        return callTree_.GetTopModules(node);
    }

    public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node) {
        return callTree_.GetTopFunctions(node);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private async void FunctionFilter_OnTextChanged(object sender, TextChangedEventArgs e) {
        var text = FunctionFilter.Text.Trim();
        await SearchFlameGraph(text);
    }

    private async Task SearchFlameGraph(string text) {
        //var prevSearchResultNodes = searchResultNodes_;
        //searchResultNodes_ = null;

        //if (prevSearchResultNodes != null) {
        //    bool redraw = text.Length <= 1; // Prevent flicker by redrawing once when search is done.
        //    GraphViewer.ResetSearchResultNodes(prevSearchResultNodes, redraw);
        //}

        //if (text.Length > 1) {
        //    searchResultNodes_ = await Task.Run(() => GraphViewer.FlameGraph.SearchNodes(text));
        //    GraphViewer.MarkSearchResultNodes(searchResultNodes_);

        //    searchResultIndex_ = -1;
        //    SelectNextSearchResult();
        //    ShowSearchSection = true;
        //}
        //else {
        //    ShowSearchSection = false;
        //}
    }

    private void UpdateSearchResultText() {
        SearchResultText = searchResultNodes_ is { Count: > 0 } ? $"{searchResultIndex_ + 1} / {searchResultNodes_.Count}" : "Not found";
    }

    private void SelectPreviousSearchResult() {
        if (searchResultNodes_ != null && searchResultIndex_ > 0) {
            searchResultIndex_--;
            UpdateSearchResultText();
            //BringNodeIntoView(searchResultNodes_[searchResultIndex_]);
        }
    }

    private void SelectNextSearchResult() {
        if (searchResultNodes_ != null && searchResultIndex_ < searchResultNodes_.Count - 1) {
            searchResultIndex_++;
            UpdateSearchResultText();
            //BringNodeIntoView(searchResultNodes_[searchResultIndex_]);
        }
    }

    private void PreviousSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
        SelectPreviousSearchResult();
    }

    private void NextSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
        SelectNextSearchResult();
    }

    public void MarkFunctionSamples(Dictionary<int, List<SampleIndex>> threadSamples) {
        ClearMarkedFunctionSamples();

        foreach (var (threadId, sampleList) in threadSamples) {
            if (threadId == -1) {
                ActivityView.MarkSamples(sampleList);
            }
            else if (threadActivityViewsMap_.TryGetValue(threadId, out var threadView)) {
                threadView.ActivityHost.MarkSamples(sampleList);
            }
        }

    }

    public void ClearMarkedFunctionSamples() {
        ActivityView.ClearMarkedSamples();
        threadActivityViews_.ForEach(threadView => threadView.ActivityHost.ClearMarkedSamples());
    }
}