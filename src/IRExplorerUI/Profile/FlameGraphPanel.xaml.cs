using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DocumentFormat.OpenXml.Wordprocessing;
using Google.Protobuf.WellKnownTypes;
using IRExplorerCore.Graph;
using IRExplorerCore.Utilities;
using IRExplorerUI.Controls;
using Microsoft.Diagnostics.Tracing.Stacks;


namespace IRExplorerUI.Profile;

public partial class FlameGraphPanel : ToolPanelControl, IFunctionProfileInfoProvider, INotifyPropertyChanged {
    public static DependencyProperty FlameGraphWidthProperty =
        DependencyProperty.Register(nameof(FlameGraphWidth), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphWeightChanged));

    public static DependencyProperty FlameGraphOffsetProperty =
        DependencyProperty.Register(nameof(FlameGraphOffset), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphOffsetChanged));


    public static DependencyProperty FlameGraphNodeOffsetProperty =
        DependencyProperty.Register(nameof(FlameGraphNodeOffsetProperty), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphOffsetChanged));

    public static DependencyProperty FlameGraphVerticalOffsetProperty =
        DependencyProperty.Register(nameof(FlameGraphVerticalOffset), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphVerticalOffsetChanged));

    private static void FlameGraphWeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is FlameGraphPanel panel) {
            panel.FlameGraphWidth = (double)e.NewValue;
        }
    }

    private static void FlameGraphOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is FlameGraphPanel panel) {
            if (e.Property == FlameGraphOffsetProperty) {
                panel.AdjustZoomPointOffset((double)e.NewValue);
            }
            else {
                panel.AdjustEnlargeNodeOffset((double)e.NewValue);
            }
        }
    }

    private static void FlameGraphVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is FlameGraphPanel panel) {
            panel.GraphHost.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    public double FlameGraphWidth {
        get {
            return GraphViewer.MaxGraphWidth;
        }
        set {
            GraphViewer.UpdateMaxWidth(value);
        }
    }

    public double FlameGraphOffset {
        get => 0;
        set {}
    }

    public double FlameGraphVerticalOffset {
        get => 0;
        set {}
    }

    private const double TimePerFrame = 1000.0 / 60; // ~16.6ms per frame at 60Hz.
    private const double ZoomAmount = 500;
    private const double ScrollWheelZoomAmount = 300;
    private const double FastPanOffset = 1000;
    private const double DefaultPanOffset = 100;
    private const double ZoomAnimationDuration = TimePerFrame * 10;
    private const double EnlargeAnimationDuration = TimePerFrame * 20;
    private const double ScrollWheelZoomAnimationDuration = TimePerFrame * 8;

    private bool dragging_;
    private Point draggingStart_;
    private Point draggingViewStart_;
    private Stack<FlameGraphState> stateStack_;
    private bool panelVisible_;
    private ProfileCallTree pendingCallTree_; // Tree to show when panel becomes visible.
    private FlameGraphNode enlargedNode_;
    private DateTime lastWheelZoomTime_;
    private DraggablePopupHoverPreview stackHoverPreview_;

    public FlameGraphPanel() {
        InitializeComponent();
        stateStack_ = new Stack<FlameGraphState>();
        stackHoverPreview_ = new DraggablePopupHoverPreview(GraphViewer, CreatePreviewPopup);
        SetupEvents();
        DataContext = this;

        //? TODO: Settings
        ShowNodePanel = true;
    }

    public override ToolPanelKind PanelKind => ToolPanelKind.FlameGraph;
    private double GraphAreaWidth => Math.Max(0, GraphHost.ViewportWidth - 1);
    private double GraphAreaHeight=> GraphHost.ViewportHeight;
    private Rect GraphArea => new Rect(0, 0, GraphAreaWidth, GraphAreaHeight);
    private Rect GraphVisibleArea => new Rect(GraphHost.HorizontalOffset,
                                              GraphHost.VerticalOffset,
                                              GraphAreaWidth, GraphAreaHeight);
    private double GraphZoomRatio => GraphViewer.MaxGraphWidth / GraphAreaWidth;
    private double GraphZoomRatioLog => Math.Pow(Math.Log2(GraphZoomRatio + 1), 2);
    private double CenterZoomPointX => GraphHost.HorizontalOffset + GraphAreaWidth / 2;

    public override void OnShowPanel() {
        base.OnShowPanel();
        panelVisible_ = true;
        InitializePendingCallTree();
    }

    private void SchedulePendingCallTree(ProfileCallTree callTree) {
        // Display flame graph once the panel is visible and visible area is valid.
        if (pendingCallTree_ == null) {
            pendingCallTree_ = callTree;
        }
    }

    private void InitializePendingCallTree() {
        if (pendingCallTree_ != null && panelVisible_) {
            InitializeCallTree(pendingCallTree_);
            pendingCallTree_ = null;
        }
    }

    private void InitializeCallTree(ProfileCallTree callTree) {
        Dispatcher.BeginInvoke(async () => {
            if (callTree != null && !GraphViewer.IsInitialized) {
                await GraphViewer.Initialize(callTree, GraphArea);
            }
        }, DispatcherPriority.Background);
    }

    public override void OnSessionStart() {
        base.OnSessionStart();
        NodeDetailsPanel.Initialize(Session, this);
        InitializePendingCallTree();
    }

    private void SetupEvents() {
        GraphHost.SizeChanged += (sender, args) => UpdateGraphWidth(args.NewSize.Width);

        GraphHost.PreviewMouseWheel += OnPreviewMouseWheel;
        GraphHost.PreviewMouseDown += OnPreviewMouseDown;

        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseDoubleClick += OnMouseDoubleClick;
        MouseMove += OnMouseMove;
        MouseDown += OnMouseDown;
    }

    private async void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) {
        HidePreviewPopup();

        var point = e.GetPosition(GraphHost);
        var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

        if (pointedNode == null) {
            GraphViewer.ClearSelection(); // Click outside graph is captured here.
        }
        else if (pointedNode.CallTreeNode != null) {
            await NodeDetailsPanel.Show(pointedNode.CallTreeNode);
        }
    }

    private void HidePreviewPopup() {
        stackHoverPreview_.Hide();
    }

    private async void OnMouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.XButton1) {
            await RestorePreviousState();
        }
    }

    private async void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) {
        if (IsMouseOutsideViewport(e)) {
            return;
        }

        var point = e.GetPosition(GraphViewer);
        var pointedNode = GraphViewer.FindPointedNode(point);

        if (pointedNode != null) {
            await EnlargeNode(pointedNode);
        }
        else {
            double zoomPointX = e.GetPosition(GraphViewer).X;

            if (Utils.IsShiftModifierActive()) {
                ZoomOut(zoomPointX);
            }
            else {
                ZoomIn(zoomPointX);
            }
        }
    }

    private double zoomPointX_;
    private double initialWidth_;
    private double initialOffsetX_;

    private double endOffsetX_;
    private FlameGraphNode offsetNode_;


    private async Task EnlargeNode(FlameGraphNode node, bool saveState = true, double verticalOffset = double.NaN) {
        if (Utils.IsAltModifierActive()) {
            await ChangeRootNode(node, true);
            return;
        }

        // Update the undo stack.
        if (saveState) {
            SaveCurrentState(FlameGraphStateKind.EnlargeNode);
        }

        enlargedNode_ = node;

        // How wide the entire graph needs to be so that the node fils the view.
        double ratio = GraphViewer.FlameGraph.InverseScaleWeight(node.Weight);
        double newMaxWidth = GraphAreaWidth * ratio;

        if (true) { //? TODO: Check if animations enabled
            double prevRatio = GraphViewer.MaxGraphWidth / GraphAreaWidth;
            double offsetRatio = prevRatio > 0 ? ratio / prevRatio : ratio;

            initialWidth_ = newMaxWidth;
            initialOffsetX_ = GraphHost.HorizontalOffset;
            endOffsetX_ = GraphViewer.ComputeNodePosition(node).X * offsetRatio;

            var horizontalAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(EnlargeAnimationDuration));

            horizontalAnim.Completed += async (sender, args) => {
                await Dispatcher.BeginInvoke(() => {
                    double newNodeX = GraphViewer.ComputeNodePosition(node).X;
                    double offset = Math.Min(newNodeX, newMaxWidth);
                    GraphHost.ScrollToHorizontalOffset(offset);

                    if (!double.IsNaN(verticalOffset)) {
                        GraphHost.ScrollToVerticalOffset(verticalOffset);
                    }
                }, DispatcherPriority.Background);
            };

            SetMaxWidth(newMaxWidth, true, EnlargeAnimationDuration);
            BeginAnimation(FlameGraphNodeOffsetProperty, horizontalAnim, HandoffBehavior.SnapshotAndReplace);

            if (!double.IsNaN(verticalOffset)) {
                var verticalAnim = new DoubleAnimation(GraphHost.VerticalOffset, verticalOffset, TimeSpan.FromMilliseconds(EnlargeAnimationDuration));
                BeginAnimation(FlameGraphVerticalOffsetProperty, verticalAnim, HandoffBehavior.SnapshotAndReplace);

            }
        }
    }

    public async Task ChangeRootNode(FlameGraphNode node, bool saveState = true) {
        // Update the undo stack.
        if (saveState) {
            SaveCurrentState(FlameGraphStateKind.ChangeRootNode);
        }

        ResetHighlightedNodes();
        await GraphViewer.Initialize(GraphViewer.FlameGraph.CallTree, node.CallTreeNode,
            new Rect(0, 0, GraphHost.ActualWidth, GraphHost.ActualHeight));
    }

    enum FlameGraphStateKind {
        Default,
        EnlargeNode,
        ChangeRootNode
    }

    class FlameGraphState {
        public FlameGraphStateKind Kind { get; set; }
        public FlameGraphNode Node { get; set; }
        public double MaxGraphWidth { get; set; }
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
    }

    private void SaveCurrentState(FlameGraphStateKind changeKind) {
        var state = new FlameGraphState() {
            Kind = changeKind,
            MaxGraphWidth = GraphViewer.MaxGraphWidth,
            HorizontalOffset = GraphHost.HorizontalOffset,
            VerticalOffset = GraphHost.VerticalOffset,
        };

        switch (changeKind) {
            case FlameGraphStateKind.EnlargeNode: {
                state.Node = enlargedNode_;
                break;
            }
            case FlameGraphStateKind.ChangeRootNode: {
                state.Node = GraphViewer.FlameGraph.RootNode;
                break;
            }
        }

        stateStack_.Push(state);
    }

    async Task RestorePreviousState() {
        if (!stateStack_.TryPop(out var state)) {
            return;
        }

        switch (state.Kind) {
            case FlameGraphStateKind.EnlargeNode: {
                // IF there is no node the root node should be selected.
                var node = state.Node ?? GraphViewer.FlameGraph.RootNode;
                await EnlargeNode(node, false, state.VerticalOffset);
                break;
            }
            case FlameGraphStateKind.ChangeRootNode: {
                await ChangeRootNode(state.Node);
                break;
            }
            default: {
                SetMaxWidth(state.MaxGraphWidth, false);
                GraphHost.ScrollToHorizontalOffset(state.HorizontalOffset);
                GraphHost.ScrollToVerticalOffset(state.VerticalOffset);
                break;
            }
        }
    }

    void ResetHighlightedNodes() {
        GraphViewer.ResetNodeHighlighting();
    }

    private void OnMouseMove(object sender, MouseEventArgs e) {
        if (dragging_) {
            var offset = draggingViewStart_ - (e.GetPosition(GraphHost) - draggingStart_);
            GraphHost.ScrollToHorizontalOffset(offset.X);
            GraphHost.ScrollToVerticalOffset(offset.Y);
            e.Handled = true;
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (dragging_) {
            dragging_ = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private async void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Start dragging the graph only if the click starts inside the scroll area,
        // excluding the scroll bars, and it in an empty spot.
        if (IsMouseOutsideViewport(e)) {
            return;
        }

        var point = e.GetPosition(GraphHost);
        var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

        if (pointedNode == null) {
            GraphViewer.ClearSelection(); // Click outside graph is captured here.
        }
        else if(pointedNode.CallTreeNode != null) {
            await NodeDetailsPanel.Show(pointedNode.CallTreeNode);
        }

        dragging_ = true;
        draggingStart_ = point;
        draggingViewStart_ = new Point(GraphHost.HorizontalOffset, GraphHost.VerticalOffset); CaptureMouse();
        Focus();
        CaptureMouse();
        e.Handled = true;
    }

    private bool IsMouseOutsideViewport(MouseButtonEventArgs e) {
        var point = e.GetPosition(GraphHost);
        return point.X < 0 ||
               point.Y < 0 ||
               point.X >= GraphHost.ViewportWidth ||
               point.Y >= GraphHost.ViewportHeight;
    }

    private async void OnKeyDown(object sender, KeyEventArgs e) {
        HidePreviewPopup();

        switch (e.Key) {
            case Key.Left: {
                ScrollToRelativeOffsets(-PanOffset, 0);
                e.Handled = true;
                break;
            }
            case Key.Right: {
                ScrollToRelativeOffsets(PanOffset, 0);
                e.Handled = true;
                break;
            }
            case Key.Up: {
                if (Utils.IsControlModifierActive()) {
                    await NavigateToParentNode();
                }
                else {
                    ScrollToRelativeOffsets(0, -PanOffset);
                }

                e.Handled = true;
                break;
            }
            case Key.Down: {
                if (Utils.IsControlModifierActive()) {
                    await NavigateToChildNode();
                }
                else {
                    ScrollToOffsets(0, PanOffset);
                }

                e.Handled = true;
                break;
            }
            case Key.OemPlus:
            case Key.Add: {
                if (Utils.IsControlModifierActive()) {
                    ZoomIn(CenterZoomPointX);
                    e.Handled = true;
                }

                return;
            }
            case Key.OemMinus:
            case Key.Subtract: {
                if (Utils.IsControlModifierActive()) {
                    ZoomOut(CenterZoomPointX);
                    e.Handled = true;
                }

                return;
            }
            case Key.Back: {
                await RestorePreviousState();
                e.Handled = true;
                return;
            }
        }
    }

    private double PanOffset => Utils.IsKeyboardModifierActive() ?
                                 FastPanOffset : DefaultPanOffset;

    private bool showNodePanel_;
    public bool ShowNodePanel {
        get => showNodePanel_;
        set => SetField(ref showNodePanel_, value);
    }

    private void ScrollToRelativeOffsets(double adjustmentX, double adjustmentY) {
        double offsetX = GraphHost.HorizontalOffset;
        double offsetY = GraphHost.VerticalOffset;
        ScrollToOffsets(offsetX + adjustmentX, offsetY + adjustmentY);
    }

    private void ScrollToOffsets(double offsetX, double offsetY) {
        GraphHost.ScrollToHorizontalOffset(offsetX);
        GraphHost.ScrollToVerticalOffset(offsetY);
    }

    private void SetMaxWidth(double maxWidth, bool animate = true, double duration = ZoomAnimationDuration) {
        if (animate) {
            var animation = new DoubleAnimation(GraphViewer.MaxGraphWidth, maxWidth, TimeSpan.FromMilliseconds(duration));
            BeginAnimation(FlameGraphWidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        else {
            GraphViewer.UpdateMaxWidth(maxWidth);
        }
    }

    private void AdjustMaxWidth(double amount, bool animate = true, double duration = ZoomAnimationDuration) {
        double newWidth = Math.Max(GraphAreaWidth, GraphViewer.MaxGraphWidth + amount);
        SetMaxWidth(newWidth, animate, duration);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        HidePreviewPopup();

        // Zoom when Ctrl/Alt/Shift or left mouse button are presesed.
        if (!(Utils.IsKeyboardModifierActive() ||
            e.LeftButton == MouseButtonState.Pressed)) {
            return;
        }

        // Disable animation if scrolling without interruption.
        var time = DateTime.UtcNow;
        var timeElapsed = time - lastWheelZoomTime_;
        bool animate = timeElapsed.TotalMilliseconds > ScrollWheelZoomAnimationDuration;

        double amount = ScrollWheelZoomAmount * GraphZoomRatioLog; // Keep step consistent.
        double step = amount * Math.CopySign(1 + e.Delta / 1000.0, e.Delta);
        double zoomPointX = e.GetPosition(GraphViewer).X;
        AdjustZoom(step, zoomPointX, animate, ScrollWheelZoomAnimationDuration);

        lastWheelZoomTime_ = time;
        e.Handled = true;
    }

    private void AdjustZoom(double step, double zoomPointX, bool animate = false, double duration = 0.0) {
        double initialWidth = GraphViewer.MaxGraphWidth;
        double initialOffsetX = GraphHost.HorizontalOffset;
        AdjustMaxWidth(step, animate, duration);

        // Maintain the zoom point under the mouse by adjusting the horizontal offset.
        if (animate) {
            zoomPointX_ = zoomPointX;
            initialWidth_ = initialWidth;
            initialOffsetX_ = initialOffsetX;
            var animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(duration));
            BeginAnimation(FlameGraphOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        else {
            AdjustGraphOffset(zoomPointX, initialWidth, initialOffsetX);
        }
    }

    private void AdjustGraphOffset(double zoomPointX, double initialWidth, double initialOffsetX) {
        double zoom = GraphViewer.MaxGraphWidth / initialWidth;
        double offsetAdjustment = (initialOffsetX / zoom + zoomPointX);
        GraphHost.ScrollToHorizontalOffset(offsetAdjustment * zoom - zoomPointX);
    }

    static double Lerp(double start, double end, double progress)  {
        return (start + (end - start) * progress);
    }

    private void AdjustEnlargeNodeOffset(double progress) {
        double offset = Lerp(initialOffsetX_, endOffsetX_, progress);
        GraphHost.ScrollToHorizontalOffset(offset);
    }

    private void AdjustZoomPointOffset(double progress) {
    double zoom = GraphViewer.MaxGraphWidth / initialWidth_;
        double offsetAdjustment = (initialOffsetX_ / zoom + zoomPointX_);
        GraphHost.ScrollToHorizontalOffset(offsetAdjustment * zoom - zoomPointX_);
    }

    private void ExecuteGraphResetWidth(object sender, ExecutedRoutedEventArgs e) {
        //? TODO: Buttons should be disabled
        if (!GraphViewer.IsInitialized) {
            return;
        }

        SetMaxWidth(GraphAreaWidth);
        ResetHighlightedNodes();
    }

    private void ExecuteGraphFitAll(object sender, ExecutedRoutedEventArgs e) {
        SetMaxWidth(GraphAreaWidth);
    }

    private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
        ZoomIn(CenterZoomPointX);
    }

    private void ZoomIn(double zoomPointX)     {
        AdjustZoom(ZoomAmount * GraphZoomRatioLog, zoomPointX, true, ZoomAnimationDuration);
    }

    private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
        ZoomOut(CenterZoomPointX);
    }

    private void ZoomOut(double zoomPointX)     {
        AdjustZoom(-ZoomAmount * GraphZoomRatioLog, zoomPointX, true, ZoomAnimationDuration);
    }

    private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
        throw new NotImplementedException();
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
        Utils.PatchToolbarStyle(sender as ToolBar);
    }

    private void GraphHost_OnScrollChanged(object sender, ScrollChangedEventArgs e) {
        if (!GraphViewer.IsInitialized) {
            return;
        }

        GraphViewer.UpdateVisibleArea(GraphVisibleArea);
    }

    private async void UndoButtoon_Click(object sender, RoutedEventArgs e) {
        await RestorePreviousState();
    }

    private async void SelectParent_Click(object sender, RoutedEventArgs e) {
        await NavigateToParentNode();
    }

    private async Task NavigateToParentNode() {
        if (enlargedNode_ != null && enlargedNode_.Parent != null) {
            GraphViewer.SelectNode(enlargedNode_.Parent);
            await EnlargeNode(enlargedNode_.Parent);
        }
    }

    private async Task NavigateToChildNode() {
        if (enlargedNode_ != null && enlargedNode_.HasChildren) {
            Trace.WriteLine($"Go to child");

            GraphViewer.SelectNode(enlargedNode_.Children[0]);
            await EnlargeNode(enlargedNode_.Children[0]);
        }
    }

    public async Task DisplayFlameGraph() {
        var callTree = Session.ProfileData.CallTree;
        SchedulePendingCallTree(callTree);
    }

    public override void OnSessionEnd() {
        base.OnSessionEnd();
        pendingCallTree_ = null;

        if (!GraphViewer.IsInitialized) {
            return;
        }

        ResetHighlightedNodes();
        GraphViewer.Reset();
    }

    private void UpdateGraphWidth(double width) {
        if (GraphViewer.IsInitialized && !GraphViewer.IsZoomed) {
            SetMaxWidth(width, false);
        }
    }

    private DraggablePopup CreatePreviewPopup(Point mousePoint, Point previewPoint) {
        var pointedNode = GraphViewer.FindPointedNode(mousePoint);
        var callNode = pointedNode?.CallTreeNode;

        if (callNode != null) {
            // If popup already opened for this node reuse the instance.
            if (stackHoverPreview_.PreviewPopup is CallTreeNodePopup popup &&
                popup.CallTreeNode == callNode) {
                return popup;
            }

            return new CallTreeNodePopup(callNode, this, previewPoint, 350, 68, GraphViewer, Session);
        }

        return null;
    }

    public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
        var list = new List<ProfileCallTreeNode>();
        var fgNode = GraphViewer.FlameGraph.GetNode(node);

        while (fgNode?.CallTreeNode != null) {
            list.Add(fgNode.CallTreeNode);
            fgNode = fgNode.Parent;
        }

        return list;
    }

    public List<IFunctionProfileInfoProvider.ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
        var moduleMap = new Dictionary<string, IFunctionProfileInfoProvider.ModuleProfileInfo>();
        CollectModules(node, moduleMap);
        var moduleList = new List<IFunctionProfileInfoProvider.ModuleProfileInfo>(moduleMap.Count);

        foreach (var module in moduleMap.Values) {
            module.Percentage = node.ScaleWeight(module.Weight);
            moduleList.Add(module);
        }

        moduleList.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        return moduleList;
    }

    public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node) {
        var funcMap = new Dictionary<string, ProfileCallTreeNode>();
        CollectFunctions(node, funcMap);
        var funcList = new List<ProfileCallTreeNode>(funcMap.Count);

        foreach (var func in funcMap.Values) {
            funcList.Add(func);
        }

        funcList.Sort((a, b) => b.ExclusiveWeight.CompareTo(a.ExclusiveWeight));
        return funcList;
    }

    public void CollectModules(ProfileCallTreeNode node, Dictionary<string, IFunctionProfileInfoProvider.ModuleProfileInfo> moduleMap) {
        var entry = moduleMap.GetOrAddValue(node.ModuleName,
            () => new IFunctionProfileInfoProvider.ModuleProfileInfo(node.ModuleName));
        entry.Weight += node.ExclusiveWeight;

        if (node.HasChildren) {
            foreach (var childNode in node.Children) {
                CollectModules(childNode, moduleMap);
            }
        }
    }

    public void CollectFunctions(ProfileCallTreeNode node, Dictionary<string, ProfileCallTreeNode> funcMap) {
        var entry = funcMap.GetOrAddValue(node.FunctionName,
            () => new ProfileCallTreeNode(node.FunctionDebugInfo, node.Function));
        entry.Weight += node.Weight;
        entry.ExclusiveWeight = node.ExclusiveWeight;

        if (node.HasChildren) {
            foreach (var childNode in node.Children) {
                CollectFunctions(childNode, funcMap);
            }
        }
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
}