using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Presentation;
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
    private SearchableProfileItem.FunctionNameFormatter nameFormatter_;
    private bool changingThreadFiltering_;

    private DoubleAnimation widthAnimation_;
    private DoubleAnimation zoomAnimation_;

    private double ActivityViewZoomRatio => ActivityView.MaxViewWidth / ActivityViewAreaWidth;
    private double ActivityViewAreaWidth => Math.Max(0, ActivityViewHost.ViewportWidth - ActivityViewHeader.ActualWidth - 1);
    private double CenterZoomPointX => ActivityScrollBar.HorizontalOffset + ActivityViewAreaWidth / 2;
    
    public TimelinePanel() {
        InitializeComponent();
        settings_ = App.Settings.FlameGraphSettings;
        threadActivityViews_ = new List<ActivityTimelineView>();
        threadActivityViewsMap_ = new Dictionary<int, ActivityTimelineView>();
        threadHoverPreviewMap_ = new Dictionary<int, DraggablePopupHoverPreview>();
        
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
    
    public bool SyncSelection {
        get => settings_.SyncSelection;
        set {
            if (value != settings_.SyncSelection) {
                settings_.SyncSelection = value;
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

    private bool hasThreadFilter_;
    public bool HasThreadFilter {
        get => hasThreadFilter_;
        set {
            if (hasThreadFilter_ != value) {
                hasThreadFilter_ = value;
                OnPropertyChanged();
            }
        }
    }

    private string threadFilterText_;
    public string ThreadFilterText {
        get => threadFilterText_;
        set {
            if (threadFilterText_ != value) {
                threadFilterText_ = value;
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
        if (callTree_ != null) {
            return;
        }
        
        callTree_ = callTree;
        nameFormatter_ = Session.CompilerInfo.NameProvider.FormatFunctionName;

        Dispatcher.BeginInvoke(async () => {
            var activityArea = new Rect(0, 0, ActivityViewAreaWidth, ActivityView.ActualHeight);
            var threads = Session.ProfileData.SortedThreadWeights;
            await ActivityView.Initialize(Session.ProfileData, activityArea);
            ActivityView.IsTimeBarVisible = true;
            ActivityView.SampleBorderColor = ColorPens.GetPen(Colors.DimGray);
            ActivityView.SamplesBackColor = ColorBrushes.GetBrush("#F4A0A0");

            var threadActivityArea = new Rect(0, 0, ActivityViewAreaWidth, 30);

            foreach (var thread in threads) {
                var threadView = new ActivityTimelineView();
                SetupActivityViewEvents(threadView.ActivityHost);
                threadView.ActivityHost.BackColor = Brushes.WhiteSmoke;
                threadView.ActivityHost.SampleBorderColor = ColorPens.GetPen(Colors.DimGray);
                threadView.ActivityHost.SamplesBackColor = ColorBrushes.GetBrush(ColorUtils.GeneratePastelColor((uint)thread.ThreadId));
                threadView.ThreadActivityAction += ThreadView_ThreadActivityAction;

                 var threadInfo = Session.ProfileData.FindThread(thread.ThreadId);

                 if (threadInfo != null && threadInfo.HasName) {
                     var backColor = ColorUtils.GenerateLightPastelColor((uint)threadInfo.Name.GetHashCode());
                     threadView.MarginBackColor = ColorBrushes.GetBrush(backColor);
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

    public void Reset() {
        callTree_ = null;
        threadActivityViews_.Clear();
        threadActivityViewsMap_.Clear();
        threadHoverPreviewMap_.Clear();
    }

    private async void ThreadView_ThreadActivityAction(object sender, ThreadActivityAction action) {
        var view = sender as ActivityTimelineView;
        Trace.WriteLine($"Thread action {action} for tread {view.ThreadId}");
        
        bool changed = false;
        changingThreadFiltering_ = true;

        switch (action) {
            case ThreadActivityAction.IncludeThread: {
                changed = UpdateThreadFilter(view, true);
                break;
            }
            case ThreadActivityAction.ExcludeThread: {
                changed = UpdateThreadFilter(view, false);
                break;
            }
            case ThreadActivityAction.FilterToThread: {
                changed = UpdateThreadFilter(view, true);

                foreach (var otherView in threadActivityViews_) {
                    if (otherView != view &&
                        UpdateThreadFilter(otherView, false)) {
                        changed = true;
                    }
                }
                break;
            }
            case ThreadActivityAction.IncludeSameNameThread:
            case ThreadActivityAction.ExcludeSameNameThread:
            case ThreadActivityAction.FilterToSameNameThread: {
                foreach (var otherView in threadActivityViews_) {
                    if (otherView.ThreadName.Equals(view.ThreadName, StringComparison.Ordinal)) {
                        if (UpdateThreadFilter(otherView,
                                action == ThreadActivityAction.IncludeSameNameThread ||
                                action == ThreadActivityAction.FilterToSameNameThread)) {
                            changed = true;
                        }
                    }
                    else if (action == ThreadActivityAction.FilterToSameNameThread &&
                             UpdateThreadFilter(otherView, false)) {
                        changed = true; // Exclude threads with different name.
                    }
                }
                break;
            }
        }

        changingThreadFiltering_ = false;

        if (changed) {
            await ApplyProfileFilter();
        }
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
                    }
                    else {
                        popup = new CallTreeNodePopup(callNode, this, previewPoint, 350, 68, view, Session);
                    }

                    popup.ShowBacktraceView = true;
                    popup.BacktraceText = CreateBacktraceText(callNode, 3);
                    return popup;
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
        
        threadHoverPreviewMap_[view.ThreadId] = preview;
    }

    private string CreateBacktraceText(ProfileCallTreeNode node, int maxLevel) {
        var sb = new StringBuilder();

        while (node.HasCallers && maxLevel-- > 0) {
            node = node.Caller;
            sb.AppendLine(nameFormatter_(node.FunctionName));
        }

        return sb.ToString().Trim();
    }

    private void SetupActivityViewEvents(ActivityView view) {
        view.PreviewMouseWheel += ActivityView_PreviewMouseWheel;

        view.SelectedTimeRange += ActivityView_SelectedTimeRange;
        view.SelectingTimeRange += ActivityView_SelectingTimeRange;
        view.FilteredTimeRange += ActivityView_FilteredTimeRange;
        view.ClearedSelectedTimeRange += ActivityView_ClearedSelectedTimeRange;
        view.ClearedFilteredTimeRange += ActivityView_ClearedFilteredTimeRange;
        view.ThreadIncludedChanged += ActivityView_ThreadIncludedChanged;
    }
    
    private void ActivityView_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        HidePreviewPopup();

        if (Utils.IsShiftModifierActive()) {
            // Turn vertical scrolling into horizontal scrolling.
            ActivityScrollBar.ScrollToHorizontalOffset(ActivityScrollBar.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }
        else if (!(Utils.IsKeyboardModifierActive() ||
                   e.LeftButton == MouseButtonState.Pressed)) {
            // Zoom when Ctrl/Alt/Shift or left mouse button are pressed.
            return;
        }

        bool animate = false;
        double amount = ScrollWheelZoomAmount * ActivityViewZoomRatio; // Keep step consistent.
        double step = amount * Math.CopySign(1 + e.Delta / 1000.0, e.Delta);
        double zoomPointX = e.GetPosition(ActivityView).X;
        AdjustZoom(step, zoomPointX, animate, ScrollWheelZoomAnimationDuration);
    }

    private void AdjustZoom(double step, double zoomPointX, bool animate = false, double duration = 0.0) {
        double initialWidth = ActivityView.MaxViewWidth;
        double initialOffsetX = ActivityScrollBar.HorizontalOffset;
        AdjustMaxWidth(step);
        AdjustGraphOffset(zoomPointX, initialWidth, initialOffsetX);
    }

    private void AdjustGraphOffset(double zoomPointX, double initialWidth, double initialOffsetX) {
        double zoom = ActivityView.MaxViewWidth / initialWidth;
        double offsetAdjustment = (initialOffsetX / zoom + zoomPointX);
        ActivityScrollBar.ScrollToHorizontalOffset(offsetAdjustment * zoom - zoomPointX);
    }

    private void HidePreviewPopup() {
        foreach (var popup in threadHoverPreviewMap_.Values) {
            popup.Hide();
        }
    }
    
    private async void ActivityView_ThreadIncludedChanged(object sender, bool included) {
        var view = sender as ActivityView;
        UpdateThreadFilter(view, included);

        if (!changingThreadFiltering_) {
            await ApplyProfileFilter();
        }
    }

    private void UpdateFilteredThreads() {
        int excludedCount = CountExcludedThreads();
        
        if (excludedCount == 0) {
            HasThreadFilter = false;
            ThreadFilterText = null;
            return;
        }

        var sb = new StringBuilder();
        int added = 0;

        if (excludedCount < threadActivityViews_.Count / 2) {
            sb.Append("All - ");
            
            foreach (var threadView in threadActivityViews_) {
                if (!threadView.IsThreadIncluded) {
                    sb.Append($"{threadView.ThreadId} ");
                    
                    if (added++ > 10) {
                        sb.Append("...");
                        break;
                    }
                }
            }
        }
        else {
            foreach (var threadView in threadActivityViews_) {
                if (threadView.IsThreadIncluded) {
                    sb.Append($"{threadView.ThreadId} ");
                    
                    if (added++ > 10) {
                        sb.Append("...");
                        break;
                    }
                }
            }
        }

        ThreadFilterText = sb.ToString();
        HasThreadFilter = true;
    }

    private async Task ApplyProfileFilter() {
        UpdateFilteredThreads();
        SampleTimeRangeInfo timeRange = ActivityView.HasFilter ? ActivityView.FilteredRange : null;
        var filter = ConstructProfileSampleFilter(timeRange);
        await Session.FilterProfileSamples(filter);
    }

    private bool UpdateThreadFilter(ActivityView view, bool included) {
        return UpdateThreadFilter(threadActivityViewsMap_[view.ThreadId], included);
    }

    private bool UpdateThreadFilter(ActivityTimelineView view, bool included) {
        if (included) {
            // Use the same filter as for the other threads.
            if (ActivityView.HasFilter) {
                view.ActivityHost.FilterTimeRange(ActivityView.FilteredRange);
            }
            else {
                view.ActivityHost.ClearTimeRangeFilter();
            }
        }
        else {
            view.ActivityHost.ClearTimeRangeFilter();
        }

        bool changed = view.IsThreadIncluded != included;
        view.IsThreadIncluded = included;
        return changed;
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
        await RemoveTimeRangeFilters(view);
    }

    private async Task RemoveTimeRangeFilters(ActivityView view = null) {
        changingThreadFiltering_ = true;

        if (view == null || view.IsSingleThreadView) {
            ActivityView.ClearTimeRangeFilter();
        }

        foreach (var threadView in threadActivityViews_) {
            threadView.ActivityHost.ClearTimeRangeFilter();

            if (view != null) {
                UpdateThreadFilter(threadView, threadView.ActivityHost.PreviousIsThreadIncluded);
            }
        }

        changingThreadFiltering_ = false;
        await ApplyProfileFilter();
    }

    private async Task RemoveThreadFilters() {
        changingThreadFiltering_ = true;
        
        foreach (var threadView in threadActivityViews_) {
            UpdateThreadFilter(threadView, true);
        }

        changingThreadFiltering_ = false;
        await ApplyProfileFilter();
    }

    private async void ActivityView_FilteredTimeRange(object sender, SampleTimeRangeInfo range) {
        var view = sender as ActivityView;
        changingThreadFiltering_ = true;

        if (view.IsSingleThreadView) {
            ActivityView.FilterTimeRange(range);
            UpdateThreadFilter(view, true);

            foreach (var threadView in threadActivityViews_) {
                if (threadView.ActivityHost != view) {
                    UpdateThreadFilter(threadView, false);
                }
            }
        }
        else {
            foreach (var threadView in threadActivityViews_) {
                if (threadView.IsThreadIncluded) {
                    threadView.ActivityHost.FilterTimeRange(range);
                }
            }
        }

        changingThreadFiltering_ = false;
        await ApplyProfileFilter();
    }

    private int CountExcludedThreads() {
        int count = 0;
        foreach (var threadView in threadActivityViews_) {
            if (!threadView.IsThreadIncluded) {
                count++;
            }
        }

        return count;
    }

    public bool HasExcludedThreads() {
        return CountExcludedThreads() > 0;
    }

    private ProfileSampleFilter ConstructProfileSampleFilter(SampleTimeRangeInfo timeRange) {
        var filter = new ProfileSampleFilter { TimeRange = timeRange };
        
        if (HasExcludedThreads()) {
            // Make a list of the non-excluded threads.
            filter.ThreadIds = new List<int>();

            foreach (var threadView in threadActivityViews_) {
                if (threadView.IsThreadIncluded) {
                    filter.ThreadIds.Add(threadView.ThreadId);
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

        foreach (var threadView in threadActivityViews_) {
            if (threadView.ActivityHost != view) {
                threadView.ActivityHost.ClearSelectedTimeRange();
            }
        }

        if(SyncSelection) {
            await Session.ProfileSampleRangeDeselected();
        }
    }

    private async void ActivityView_SelectedTimeRange(object sender, SampleTimeRangeInfo range) {
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
            foreach (var threadView in threadActivityViews_) {
                if (threadView.IsThreadIncluded) {
                    threadView.ActivityHost.SelectTimeRange(range);
                }
            }
        }

        if (SyncSelection) {
            await Session.ProfileSampleRangeSelected(range);
        }
    }

    private void ActivityView_SelectingTimeRange(object sender, SampleTimeRangeInfo range) {
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
            foreach (var threadView in threadActivityViews_) {
                if (threadView.IsThreadIncluded) {
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

    private async void ExecuteGraphResetWidth(object sender, ExecutedRoutedEventArgs e) {
        ActivityScrollBar.ScrollToHorizontalOffset(0);
        SetMaxWidth(ActivityViewAreaWidth);
        await RemoveTimeRangeFilters();
    }

    private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
        ZoomIn(CenterZoomPointX);
    }

    private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
        ZoomOut(CenterZoomPointX);
    }

    private void ZoomIn(double zoomPointX) {
        AdjustZoom(ZoomAmount * ActivityViewZoomRatio, zoomPointX, true, ZoomAnimationDuration);
    }

    private void ZoomOut(double zoomPointX) {
        AdjustZoom(-ZoomAmount * ActivityViewZoomRatio, zoomPointX, true, ZoomAnimationDuration);
    }

    private void AdjustMaxWidth(double amount) {
        var newWidth = Math.Max(ActivityView.MaxViewWidth + amount, ActivityViewAreaWidth);
        SetMaxWidth(newWidth);
    }

    private void SetMaxWidth(double newWidth, object source = null) {
        ActivityView.SetMaxWidth(newWidth);
        ScrollElement.Width = newWidth + ActivityViewHeader.ActualWidth;

        foreach (var threadView in threadActivityViews_) {
            threadView.ActivityHost.SetMaxWidth(newWidth);

            if (source != threadView.TimelineHost) {
                threadView.TimelineHost.SetMaxWidth(newWidth, false);
            }
        }
    }

    private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
        
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
        Utils.PatchToolbarStyle(sender as ToolBar);
    }

    private void ActivityScrollBar_OnScrollChanged(object sender, ScrollChangedEventArgs e) {
        double offset = ActivityScrollBar.HorizontalOffset;
        ActivityView.SetHorizontalOffset(offset);

        foreach (var view in threadActivityViews_) {
            view.ActivityHost.SetHorizontalOffset(offset);
            view.TimelineHost.SetHorizontalOffset(offset);
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

    private async void RemoveFiltersExecuted(object sender, ExecutedRoutedEventArgs e) {
        await RemoveTimeRangeFilters();
    }

    private async void RemoveThreadFiltersExecuted(object sender, ExecutedRoutedEventArgs e) {
        await RemoveThreadFilters();
    }

    private async void ActivityViewHeader_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.LeftButton == MouseButtonState.Pressed &&
            e.ClickCount >= 2) {
            await RemoveThreadFilters();
        }
    }
}