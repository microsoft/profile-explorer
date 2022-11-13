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

                var activityArea = new Rect(0, 0, ActivityHost.ActualWidth, ActivityHost.ActualHeight);
                var threads = Session.ProfileData.SortedThreadWeights;
                await ActivityView.Initialize(Session.ProfileData, activityArea);
                ActivityView.IsTimeBarVisible = true;
                ActivityView.SampleBorderColor = ColorPens.GetPen(Colors.DimGray);
                ActivityView.SamplesBackColor = Brushes.DeepSkyBlue;

                var threadActivityArea = new Rect(0, 0, ActivityHost.ActualWidth, 32);
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
                    threadView.ActivityHost.SamplesBackColor = ColorBrushes.GetBrush(ColorUtils.GeneratePastelColor(thread.ThreadId));

                    // var threadInfo = Session.ProfileData.FindThread(thread.ThreadId);
                    //
                    // if (threadInfo != null && threadInfo.HasName) {
                    //     var backColor = ColorUtils.GeneratePastelColor(threadInfo.Name.GetHashCode());
                    //     threadView.ActivityHost.BackColor = ColorBrushes.GetBrush(backColor);
                    // }

                    threadActivityViews_.Add(threadView);
                    threadActivityViewsMap_[thread.ThreadId] = threadView;
                    await threadView.ActivityHost.Initialize(Session.ProfileData, threadActivityArea, thread.ThreadId);

                    threadView.TimelineHost.Session = Session;
                    threadView.TimelineHost.InitializeTimeline(callTree, thread.ThreadId);
                    SetupTimelineViewEvents(threadView.TimelineHost);
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

        //stackHoverPreview_ = new DraggablePopupHoverPreview(GraphViewer,
        //    CallTreeNodePopup.PopupHoverDuration,
        //    (mousePoint, previewPoint) => {
        //        var pointedNode = GraphViewer.FindPointedNode(mousePoint);
        //        var callNode = pointedNode?.CallTreeNode;

        //        if (callNode != null) {
        //            // If popup already opened for this node reuse the instance.
        //            if (stackHoverPreview_.PreviewPopup is CallTreeNodePopup popup) {
        //                popup.UpdatePosition(previewPoint, GraphViewer);
        //                popup.UpdateNode(callNode);
        //                return popup;
        //            }

        //            return new CallTreeNodePopup(callNode, this, previewPoint, 350, 68, GraphViewer, Session);
        //        }

        //        return null;
        //    },
        //    (mousePoint, popup) => {
        //        if (popup is CallTreeNodePopup previewPopup) {
        //            // Hide if not over the same node anymore.
        //            var pointedNode = GraphViewer.FindPointedNode(mousePoint);
        //            return previewPopup.CallTreeNode != pointedNode?.CallTreeNode;
        //        }

        //        return true;
        //    },
        //    popup => {
        //        Session.RegisterDetachedPanel(popup);
        //    });
    }

    private void SetupActivityViewEvents(ActivityView view) {
        view.SelectedTimeRange += ActivityView_OnSelectedTimeRange;
        view.SelectingTimeRange += ActivityView_OnSelectingTimeRange;
        view.FilteredTimeRange += ActivityView_FilteredTimeRange;
        view.ClearedSelectedTimeRange += ActivityView_ClearedSelectedTimeRange;
        view.ClearedFilteredTimeRange += ActivityView_ClearedFilteredTimeRange;
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

        await Session.RemoveProfileSamplesFilter();
    }

    private async void ActivityView_FilteredTimeRange(object sender, SampleTimeRangeInfo e) {
        var view = sender as ActivityView;

        if (view.IsSingleThreadView) {
            ActivityView.FilterTimeRange(e);

            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost != view) {
                    threadView.ActivityHost.FilterAllOut();
                }
            }
        }
        else {
            if (threadActivityViews_ != null) {
                foreach (var threadView in threadActivityViews_) {
                    threadView.ActivityHost.FilterTimeRange(e);
                }
            }
        }

        await Session.FilterProfileSamples(e);
    }

    private void ActivityView_ClearedSelectedTimeRange(object sender, EventArgs e) {
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

        //? GraphViewer.ClearSelection();
    }

    private void ActivityView_OnSelectedTimeRange(object sender, SampleTimeRangeInfo e) {
        Trace.WriteLine($"Selected {e.StartTime} / {e.EndTime}, range {e.StartSampleIndex}-{e.EndSampleIndex}");

        //? GraphViewer.ClearSelection();

        var view = sender as ActivityView;

        if (view.IsSingleThreadView) {
            ActivityView.SelectTimeRange(e);

            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost != view) {
                    threadView.ActivityHost.ClearSelectedTimeRange();
                }
            }
        }
        else {
            if (threadActivityViews_ != null) {
                foreach (var threadView in threadActivityViews_) {
                    threadView.ActivityHost.SelectTimeRange(e);
                }
            }
        }

        //if (GraphViewer.IsInitialized) {
        //    if (isTimelineView_) {
        //        var nodes = GraphViewer.FlameGraph.GetNodesInTimeRange(e.StartTime, e.EndTime);
        //        GraphViewer.SelectNodes(nodes);
        //    }
        //    else {
        //        var nodes = FindCallTreeNodesForSamples(e.StartSampleIndex, e.EndSampleIndex, e.ThreadId, Session.ProfileData);
        //        GraphViewer.SelectNodes(nodes);
        //    }
        //}
    }

    private void ActivityView_OnSelectingTimeRange(object sender, SampleTimeRangeInfo e) {
        //Trace.WriteLine($"Selecting {e.StartTime} / {e.EndTime}, range {e.StartSampleIndex}-{e.EndSampleIndex}");

        var view = sender as ActivityView;

        if (view.IsSingleThreadView) {
            ActivityView.SelectTimeRange(e);

            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost != view) {
                    threadView.ActivityHost.ClearSelectedTimeRange();
                }
            }
        }
        else {
            if (threadActivityViews_ != null) {
                foreach (var threadView in threadActivityViews_) {
                    threadView.ActivityHost.SelectTimeRange(e);
                }
            }
        }

       // GraphViewer.ClearSelection();

        //? TODO: Too slow
        // if (GraphViewer.IsInitialized) {
        //     if (isTimelineView_) {
        //         var nodes = GraphViewer.FlameGraph.GetNodesInTimeRange(e.StartTime, e.EndTime);
        //         GraphViewer.SelectNodes(nodes);
        //     }
        //     else {
        //         var nodes = FindCallTreeNodesForSamples(e.StartSampleIndex, e.EndSampleIndex, e.ThreadId, Session.ProfileData);
        //         GraphViewer.SelectNodes(nodes);
        //     }
        // }
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
    
    private async Task OpenFunction(FlameGraphNode node) {
        await OpenFunction(node.CallTreeNode);
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
        var activityArea = new Rect(ActivityScrollBar.HorizontalOffset, 0, ActivityHost.ActualWidth, ActivityHost.ActualHeight);
        ActivityView.UpdateVisibleArea(activityArea);

        if (threadActivityViews_ != null) {
            foreach (var view in threadActivityViews_) {
                view.ActivityHost.UpdateVisibleArea(activityArea);
                view.TimelineHost.SetHorizontalOffset(ActivityScrollBar.HorizontalOffset);
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

    private Dictionary<int, List<SampleIndex>>
        FindFunctionSamples(ProfileCallTreeNode node, ProfileData profile) {
        var sw = Stopwatch.StartNew();
        var allThreadsList = new List<SampleIndex>();
        var threadListMap = new Dictionary<int, List<SampleIndex>>();
        threadListMap[-1] = allThreadsList;

        if (node.Function == null) {
            return threadListMap;
        }

        int sampleStartIndex = 0;
        int sampleEndIndex = profile.Samples.Count;
        var funcProfile = profile.GetFunctionProfile(node.Function);

        if (funcProfile != null && funcProfile.SampleStartIndex != int.MaxValue) {
            sampleStartIndex = funcProfile.SampleStartIndex;
            sampleEndIndex = funcProfile.SampleEndIndex;
        }

        int index = 0;

        //? Also here - Abstract parallel run chunks to take action per sample

        for (int i = sampleStartIndex; i < sampleEndIndex; i++) {
            var (sample, stack) = profile.Samples[i];
            foreach (var stackFrame in stack.StackFrames) {
                if (stackFrame.IsUnknown) continue;

                if (stackFrame.Info.Function.Value.Equals(node.Function)) {
                    var threadList = threadListMap.GetOrAddValue(stack.Context.ThreadId);
                    threadList.Add(new SampleIndex(index, sample.Time));
                    allThreadsList.Add(new SampleIndex(index, sample.Time));

                    break;
                }
            }

            index++;
        }

        Trace.WriteLine($"FindSamples took: {sw.ElapsedMilliseconds} for {allThreadsList.Count} samples");
        return threadListMap;
    }

    private HashSet<IRTextFunction> FindFunctionsForSamples(int sampleStartIndex, int sampleEndIndex, int threadId, ProfileData profile) {
        var funcSet = new HashSet<IRTextFunction>();

        //? Abstract parallel run chunks to take action per sample (ComputeFunctionProfile)
        for (int i = sampleStartIndex; i < sampleEndIndex; i++) {
            var (sample, stack) = profile.Samples[i];

            if (threadId != -1 && stack.Context.ThreadId != threadId) {
                continue;
            }

            foreach (var stackFrame in stack.StackFrames) {
                if (stackFrame.IsUnknown)
                    continue;
                funcSet.Add(stackFrame.Info.Function);
            }
        }

        return funcSet;
    }

    private List<ProfileCallTreeNode> FindCallTreeNodesForSamples(int sampleStartIndex, int sampleEndIndex, int threadId, ProfileData profile) {
        var sw = Stopwatch.StartNew();
        var funcs = FindFunctionsForSamples(sampleStartIndex, sampleEndIndex, threadId, profile);
        var callNodes = new List<ProfileCallTreeNode>(funcs.Count);

        foreach (var func in funcs) {
            var nodes = profile.CallTree.GetCallTreeNodes(func);
            if (nodes != null) {
                callNodes.AddRange(nodes);
            }
        }

        Trace.WriteLine($"FindCallTreeNodesForSamples took: {sw.ElapsedMilliseconds} for {callNodes.Count} call nodes");
        return callNodes;
    }
}