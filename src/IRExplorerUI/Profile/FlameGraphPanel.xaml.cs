using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IRExplorerCore.Graph;

namespace IRExplorerUI.Profile;

public partial class FlameGraphPanel : ToolPanelControl {
    public static DependencyProperty FlameGraphWeightProperty =
        DependencyProperty.Register(nameof(FlameGraphWeight), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphWeightChanged));

    public double FlameGraphWeight
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
            panel.FlameGraphWeight = (double)e.NewValue;
        }
    }

    private const double ZoomAmount = 500;
    private const double ScrollWheelZoomAmount = 300;
    private const double FastPanOffset = 1000;
    private const double PanOffset = 100;
    private const double ZoomAnimationDuration = 240;
    private const double ScrollWheelZoomAAnimationDuration = 60;

    private bool dragging_;
    private Point draggingStart_;
    private Point draggingViewStart_;
    private Stack<FlameGraphState> stateStack_;

    public FlameGraphPanel() {
        InitializeComponent();
        stateStack_ = new Stack<FlameGraphState>();

        SetupEvents();
    }

    private double GraphAreaWidth => GraphHost.ViewportWidth - 1;
    private double GraphAreaHeight=> GraphHost.ViewportHeight;

    public async Task Initialize(ProfileCallTree callTree, Rect visibleArea) {
        await GraphViewer.Initialize(callTree, visibleArea);
    }

    private void SetupEvents() {
        PreviewMouseWheel += GraphPanel_PreviewMouseWheel;
        KeyDown += GraphPanel_PreviewKeyDown;
        MouseLeftButtonDown += GraphPanel_MouseLeftButtonDown;
        MouseLeftButtonUp += GraphPanel_MouseLeftButtonUp;
        MouseDoubleClick += OnMouseDoubleClick;
        MouseMove += GraphPanel_MouseMove;
    }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) {
        var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

        if (pointedNode != null) {
            EnlargeNode(pointedNode);
        }
    }

    private FlameGraphNode enlargedNode_;

    public void EnlargeNode(FlameGraphNode node, bool saveState = true) {
        if (Utils.IsControlModifierActive()) {
            SwapNode(node, true);
            return;
        }

        if (saveState) {
            SaveCurrentState();
        }

        enlargedNode_ = node;
        double newMaxWidth = GraphAreaWidth * ((double)GraphViewer.FlameGraph.RootWeight.Ticks / node.Weight.Ticks);
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

    public async Task SwapNode(FlameGraphNode node, bool saveState = true) {
        ResetState();
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

    private void SaveCurrentState() {
        var state = new FlameGraphState() {
            MaxGraphWidth = GraphViewer.MaxGraphWidth,
            HorizontalOffset = GraphHost.HorizontalOffset,
            VerticalOffset = GraphHost.VerticalOffset
        };

        if (enlargedNode_ != null) {
            state.Kind = FlameGraphStateKind.EnlargeNode;
            state.Node = enlargedNode_;
        }

        stateStack_.Push(state);
    }

    void RestorePreviousState() {
        if (!stateStack_.TryPop(out var state)) {
            return;
        }

        switch (state.Kind) {
            case FlameGraphStateKind.EnlargeNode: {
                EnlargeNode(state.Node, false);
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

    void ResetState() {
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
                    AdjustMaxWidth(ZoomAmount);
                    e.Handled = true;
                }

                return;
            }
            case Key.OemMinus:
            case Key.Subtract: {
                if (Utils.IsControlModifierActive()) {
                    AdjustMaxWidth(-ZoomAmount);
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
            BeginAnimation(FlameGraphWeightProperty, animation);
        }
        else {
            GraphViewer.UpdateMaxWidth(maxWidth);
        }
    }

    private void AdjustMaxWidth(double amount, bool animate = true, double duration = ZoomAnimationDuration) {
        double newWidth = Math.Max(0, GraphViewer.MaxGraphWidth + amount);
        SetMaxWidth(newWidth, animate, duration);
    }

    private void GraphPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        if (!Utils.IsKeyboardModifierActive()) {
            return;
        }

        double step = ScrollWheelZoomAmount * Math.CopySign(1 + e.Delta / 1000.0, e.Delta);
        double initialWidth = GraphViewer.MaxGraphWidth;
        double initialOffsetX = GraphHost.HorizontalOffset;
        AdjustMaxWidth(step, false);

        // Maintain the zoom point under the mouse by adjusting the horizontal offset.
        double zoom = GraphViewer.MaxGraphWidth / initialWidth;
        double zoomPointX = e.GetPosition(GraphViewer).X;
        double offsetAdjustment = (initialOffsetX / zoom  + zoomPointX);

        GraphHost.ScrollToHorizontalOffset(offsetAdjustment * zoom - zoomPointX);
        e.Handled = true;
    }

    private double GetPanOffset() {
        return Utils.IsKeyboardModifierActive() ? FastPanOffset : PanOffset;
    }

    private void ExecuteGraphFitWidth(object sender, ExecutedRoutedEventArgs e) {
    }

    private void ExecuteGraphResetWidth(object sender, ExecutedRoutedEventArgs e) {
        SetMaxWidth(GraphAreaWidth);
        ResetState();
    }

    private void ExecuteGraphFitAll(object sender, ExecutedRoutedEventArgs e) {
        SetMaxWidth(GraphAreaWidth);
    }

    private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
        AdjustMaxWidth(ZoomAmount);
    }

    private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
        AdjustMaxWidth(-ZoomAmount);
    }

    private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
        throw new NotImplementedException();
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
        Utils.PatchToolbarStyle(sender as ToolBar);
    }

    private void GraphHost_OnScrollChanged(object sender, ScrollChangedEventArgs e) {
        var area = new Rect(GraphHost.HorizontalOffset, GraphHost.VerticalOffset,
            GraphAreaWidth, GraphAreaHeight);
        GraphViewer.UpdateVisibleArea(area);
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e) {
        RestorePreviousState();
    }

    private void ButtonBase_OnClick2(object sender, RoutedEventArgs e) {
        if (enlargedNode_ != null &&  enlargedNode_.Parent != null) {
            Trace.WriteLine($"Parent {enlargedNode_.Parent.CallTreeNode?.FunctionName}, w {enlargedNode_.Parent.Weight}  VS  {enlargedNode_.Weight}");
            EnlargeNode(enlargedNode_.Parent);
        }
    }

}