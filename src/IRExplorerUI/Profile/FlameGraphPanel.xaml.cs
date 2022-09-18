using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DocumentFormat.OpenXml.Wordprocessing;
using Google.Protobuf.WellKnownTypes;
using IRExplorerCore.Graph;

namespace IRExplorerUI.Profile;

public partial class FlameGraphPanel : ToolPanelControl {
    public static DependencyProperty FlameGraphWidthProperty =
        DependencyProperty.Register(nameof(FlameGraphWidth), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphWeightChanged));

    public double FlameGraphWidth
    {
        get {
            return GraphViewer.MaxGraphWidth;
        }
        set {
            GraphViewer.UpdateMaxWidth(value);
        }
    }

    private static void FlameGraphWeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is FlameGraphPanel panel) {
            panel.FlameGraphWidth = (double)e.NewValue;
        }
    }

    public static DependencyProperty FlameGraphOffsetProperty =
        DependencyProperty.Register(nameof(FlameGraphOffset), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphOffsetChanged));

    public static DependencyProperty FlameGraphVerticalOffsetProperty =
        DependencyProperty.Register(nameof(FlameGraphVerticalOffset), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphVerticalOffsetChanged));


    public double FlameGraphOffset
    {
        get {
            return 0;
        }
        set {
            //GraphViewer.UpdateMaxWidth(value);
        }
    }

    public double FlameGraphVerticalOffset
    {
        get {
            return 0;
        }
        set {
            //GraphViewer.UpdateMaxWidth(value);
        }
    }

    private static void FlameGraphOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is FlameGraphPanel panel) {

            panel.AdjustGraphOffset2((double)e.NewValue);
        }
    }

    private static void FlameGraphVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is FlameGraphPanel panel) {

            panel.GraphHost.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    private const double TimePerFrame = 1000.0 / 60; // ~16.6ms per frame at 60Hz.
    private const double ZoomAmount = 500;
    private const double ScrollWheelZoomAmount = 300;
    private const double FastPanOffset = 1000;
    private const double PanOffset = 100;
    private const double ZoomAnimationDuration = TimePerFrame * 10;
    private const double EnlargeAnimationDuration = TimePerFrame * 15;
    private const double ScrollWheelZoomAAnimationDuration = TimePerFrame * 5;

    private bool dragging_;
    private Point draggingStart_;
    private Point draggingViewStart_;
    private Stack<FlameGraphState> stateStack_;
    private bool panelVisible_;
    private ProfileCallTree pendingCallTree_; // Tree to show when panel becomes visible.
    private FlameGraphNode enlargedNode_;

    public FlameGraphPanel() {
        InitializeComponent();
        stateStack_ = new Stack<FlameGraphState>();

        SetupEvents();
    }

    public override ToolPanelKind PanelKind => ToolPanelKind.FlameGraph;
    private double GraphAreaWidth => GraphHost.ViewportWidth - 1;
    private double GraphAreaHeight=> GraphHost.ViewportHeight;
    private Rect GraphArea => new Rect(0, 0, GraphAreaWidth, GraphAreaHeight);
    private double GraphZoomRatio => GraphViewer.MaxGraphWidth / GraphAreaWidth;

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
        if (pendingCallTree_ != null) {
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
        InitializePendingCallTree();
    }

    public async Task Initialize(ProfileCallTree callTree) {
        await GraphViewer.Initialize(callTree, GraphArea);
    }

    private void SetupEvents() {
        PreviewMouseWheel += GraphPanel_PreviewMouseWheel;
        KeyDown += GraphPanel_PreviewKeyDown;
        MouseLeftButtonDown += GraphPanel_MouseLeftButtonDown;
        MouseLeftButtonUp += GraphPanel_MouseLeftButtonUp;
        MouseDoubleClick += OnMouseDoubleClick;
        MouseMove += GraphPanel_MouseMove;
        MouseDown += OnMouseDown;
    }

    private async void OnMouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.XButton1) {
            await RestorePreviousState();
        }
    }

    private async void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) {
        var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

        if (pointedNode != null) {
            await EnlargeNode(pointedNode);
        }
    }

    private double zoomPointX_;
    private double initialWidth_;
    private double initialOffsetX_;

    private double endOffsetX_;
    private FlameGraphNode offsetNode_;


    private async Task EnlargeNode(FlameGraphNode node, bool saveState = true, double verticalOffset = double.NaN) {
        if (Utils.IsControlModifierActive()) {
            await ChangeRootNode(node, true);
            return;
        }

        if (saveState) {
            SaveCurrentState(FlameGraphStateKind.EnlargeNode);
        }

        enlargedNode_ = node;

        // How wide the entire graph needs to be so that the node fils the view.
        double newMaxWidth = GraphAreaWidth * ((double)GraphViewer.FlameGraph.RootWeight.Ticks / node.Weight.Ticks);

        // nmw = area * * ratio
        // ration = nmw / area
        // cr = maxwidth/area

        if (true) {
            double ratio = ((double)GraphViewer.FlameGraph.RootWeight.Ticks / node.Weight.Ticks);
            double prevRatio = GraphViewer.MaxGraphWidth / GraphAreaWidth;
            double offsetRatio = prevRatio > 0 ? ratio / prevRatio : ratio;

            offsetNode_ = node;
            initialWidth_ = newMaxWidth;
            initialOffsetX_ = GraphHost.HorizontalOffset;
            endOffsetX_ = node.Bounds.Left * offsetRatio;

            var animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(EnlargeAnimationDuration));

            animation.Completed += (sender, args) => {
                offsetNode_ = null;
                initialOffsetX_ = 0;
                initialWidth_ = 0;
                endOffsetX_ = 0;
                Dispatcher.BeginInvoke(() => {
                    double newNodeX = node.Bounds.Left;
                    double offset = Math.Min(newNodeX, newMaxWidth);
                    GraphHost.ScrollToHorizontalOffset(offset);

                    if (!double.IsNaN(verticalOffset)) {
                        GraphHost.ScrollToVerticalOffset(verticalOffset);
                    }
                }, DispatcherPriority.Background);
            };

            SetMaxWidth(newMaxWidth, true, EnlargeAnimationDuration);
            BeginAnimation(FlameGraphOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);

            if (!double.IsNaN(verticalOffset)) {
                var animation2 = new DoubleAnimation(GraphHost.VerticalOffset, verticalOffset, TimeSpan.FromMilliseconds(EnlargeAnimationDuration));
                BeginAnimation(FlameGraphVerticalOffsetProperty, animation2, HandoffBehavior.SnapshotAndReplace);

            }
        }
        else {
            SetMaxWidth(newMaxWidth, false);
            double newNodeX = node.Bounds.Left;

            // Updating scroll viewer offset must be delayed until
            // it adjusts to the change in size of the graph...
            //! TOOD: Needed for other places?
            Dispatcher.BeginInvoke(() => {
                double offset = Math.Min(newNodeX, newMaxWidth);
                GraphHost.ScrollToHorizontalOffset(offset);

            });
        }
    }

    public async Task ChangeRootNode(FlameGraphNode node, bool saveState = true) {
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

    private void GraphPanel_MouseMove(object sender, MouseEventArgs e) {
        if (dragging_) {
            var offset = draggingViewStart_ - (e.GetPosition(GraphHost) - draggingStart_);
            GraphHost.ScrollToHorizontalOffset(offset.X);
            GraphHost.ScrollToVerticalOffset(offset.Y);
            e.Handled = true;
        }
    }

    private void GraphPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (dragging_) {
            dragging_ = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void GraphPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Start dragging the graph only if the click starts inside the scroll area,
        // excluding the scroll bars, and it in an empty spot.
        var point = e.GetPosition(GraphHost);
        Focus();

        if (point.X < 0 ||
            point.Y < 0 ||
            point.X >= GraphHost.ViewportWidth ||
            point.Y >= GraphHost.ViewportHeight) {
            return;
        }

        var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

        if (pointedNode != null) {
            return;
        }

        dragging_ = true;
        draggingStart_ = point;
        draggingViewStart_ = new Point(GraphHost.HorizontalOffset, GraphHost.VerticalOffset); CaptureMouse();
        CaptureMouse();
        e.Handled = true;
    }

    private void GraphPanel_PreviewKeyDown(object sender, KeyEventArgs e) {
        double offsetX = GraphHost.HorizontalOffset;
        double offsetY = GraphHost.VerticalOffset;

        switch (e.Key) {
            case Key.Right: {
                offsetX += GetPanOffset();
                e.Handled = true;
                break;
            }
            case Key.Left: {
                offsetX -= GetPanOffset();
                e.Handled = true;
                break;
            }
            case Key.Up: {
                offsetY -= GetPanOffset();
                e.Handled = true;
                break;
            }
            case Key.Down: {
                offsetY += GetPanOffset();
                e.Handled = true;
                break;
            }
            case Key.OemPlus:
            case Key.Add: {
                if (Utils.IsControlModifierActive()) {
                    ZoomIn();
                    e.Handled = true;
                }

                return;
            }
            case Key.OemMinus:
            case Key.Subtract: {
                if (Utils.IsControlModifierActive()) {
                    ZoomOut();
                    e.Handled = true;
                }

                return;
            }
        }

        if (e.Handled) {
            GraphHost.ScrollToHorizontalOffset(offsetX);
            GraphHost.ScrollToVerticalOffset(offsetY);
        }
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

    private void GraphPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        if (!Utils.IsKeyboardModifierActive() &&
            !(e.LeftButton == MouseButtonState.Pressed)) {
            return;
        }

        double amount = ScrollWheelZoomAmount * GraphZoomRatio; // Keep step consistent.
        double step = amount * Math.CopySign(1 + e.Delta / 1000.0, e.Delta);
        double zoomPointX = e.GetPosition(GraphViewer).X;
        AdjustZoom(step, zoomPointX, true, ScrollWheelZoomAAnimationDuration);
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

    public static double EaseIn(double t)
    {
        return t * t;
    }

    static double Flip(double x)
    {
        return 1 - x;
    }

    public static double EaseOut(double t)
    {
        return Flip(EaseIn(Flip(t)));
    }

    static double Lerp(double start_value, double end_value, double pct)
    {
        return (start_value + (end_value - start_value) * pct);
    }

    private void AdjustGraphOffset2(double progress)
    {
        if (offsetNode_ != null) {
            double newNodeX = offsetNode_.Bounds.Left;
            double offset = Math.Min(newNodeX, initialWidth_);

            //offset = initialOffsetX_ + (offset - initialOffsetX_) * progress;

            offset = Lerp(initialOffsetX_, endOffsetX_, progress);

            //Trace.WriteLine($"{offset}, {Math.Min(newNodeX, initialWidth_)}");
            //offset = Math.Min(offset, initialOffsetX_);
            GraphHost.ScrollToHorizontalOffset(offset);
            return;
        }

        double zoom = GraphViewer.MaxGraphWidth / initialWidth_;
        double offsetAdjustment = (initialOffsetX_ / zoom + zoomPointX_);
        GraphHost.ScrollToHorizontalOffset(offsetAdjustment * zoom - zoomPointX_);
    }

    private double GetPanOffset() {
        return Utils.IsKeyboardModifierActive() ? FastPanOffset : PanOffset;
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
        ZoomIn();
    }

    private void ZoomIn()     {
        double zoomPointX = GraphAreaWidth / 2;
        AdjustZoom(ZoomAmount * GraphZoomRatio, zoomPointX, true, ZoomAnimationDuration);
    }

    private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
        ZoomOut();
    }

    private void ZoomOut()     {
        double zoomPointX = GraphAreaWidth / 2;
        AdjustZoom(-ZoomAmount * GraphZoomRatio, zoomPointX, true, ZoomAnimationDuration);
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

        var area = new Rect(GraphHost.HorizontalOffset, GraphHost.VerticalOffset,
            GraphAreaWidth, GraphAreaHeight);
        GraphViewer.UpdateVisibleArea(area);
    }

    private async void ButtonBase_OnClick(object sender, RoutedEventArgs e) {
        await RestorePreviousState();
    }

    private void ButtonBase_OnClick2(object sender, RoutedEventArgs e) {
        if (enlargedNode_ != null &&  enlargedNode_.Parent != null) {
            EnlargeNode(enlargedNode_.Parent);
        }
    }

    public async Task DisplayFlameGraph() {
        var callTree = Session.ProfileData.CallTree;
        SchedulePendingCallTree(callTree);
    }

    public override void OnSessionEnd() {
        base.OnSessionEnd();
        pendingCallTree_ = null;
        ResetHighlightedNodes();
        GraphViewer.Reset();
    }
}