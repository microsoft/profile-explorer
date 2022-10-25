using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
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
using IRExplorerUI.Profile;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.VisualBasic;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace IRExplorerUI.Profile;

public partial class FlameGraphPanel : ToolPanelControl, IFunctionProfileInfoProvider, INotifyPropertyChanged {
    public static DependencyProperty FlameGraphWidthProperty =
        DependencyProperty.Register(nameof(FlameGraphWidth), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphWeightChanged));

    public static DependencyProperty FlameGraphZoomProperty =
        DependencyProperty.Register(nameof(FlameGraphOffset), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphOffsetChanged));

    public static DependencyProperty FlameGraphEnlargeNodeProperty =
        DependencyProperty.Register(nameof(FlameGraphEnlargeNodeProperty), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphOffsetChanged));

    public static DependencyProperty FlameGraphHorizontalOffsetProperty =
        DependencyProperty.Register(nameof(FlameGraphHorizontalOffset), typeof(double), typeof(FlameGraphPanel),
            new PropertyMetadata(0.0, FlameGraphHorizontalOffsetChanged));

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
            if (e.Property == FlameGraphZoomProperty) {
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

    private static void FlameGraphHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is FlameGraphPanel panel) {
            panel.GraphHost.ScrollToHorizontalOffset((double)e.NewValue);
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

    public double FlameGraphHorizontalOffset {
        get => 0;
        set { }
    }

    private const double TimePerFrame = 1000.0 / 60; // ~16.6ms per frame at 60Hz.
    private const double ZoomAmount = 500;
    private const double ScrollWheelZoomAmount = 300;
    private const double FastPanOffset = 1000;
    private const double DefaultPanOffset = 100;
    private const double ZoomAnimationDuration = TimePerFrame * 10;
    private const double EnlargeAnimationDuration = TimePerFrame * 12;
    private const double ScrollWheelZoomAnimationDuration = TimePerFrame * 8;

    private FlameGraphSettings settings_;
    private Stack<FlameGraphState> stateStack_;
    private bool dragging_;
    private Point draggingStart_;
    private Point draggingViewStart_;
    private bool panelVisible_;
    private ProfileCallTree callTree_;
    private ProfileCallTree pendingCallTree_; // Tree to show when panel becomes visible.
    private FlameGraphNode enlargedNode_;
    private DateTime lastWheelZoomTime_;
    private DraggablePopupHoverPreview stackHoverPreview_;
    private List<FlameGraphNode> searchResultNodes_;

    private double zoomPointX_;
    private double initialWidth_;
    private double initialOffsetX_;
    private double endOffsetX_;


    public FlameGraphPanel() {
        InitializeComponent();
        settings_ = App.Settings.FlameGraphSettings;
        stateStack_ = new Stack<FlameGraphState>();
        SetupEvents();
        DataContext = this;
        ShowNodePanel = true;
    }

    public override ToolPanelKind PanelKind => ToolPanelKind.FlameGraph;
    private double GraphAreaWidth => Math.Max(0, GraphHost.ViewportWidth - 1);
    private double GraphAreaHeight=> GraphHost.ViewportHeight;
    private Rect GraphArea => new Rect(0, 0, GraphAreaWidth, GraphAreaHeight);
    private Rect GraphVisibleArea => new Rect(GraphHost.HorizontalOffset,
                                              GraphHost.VerticalOffset,
                                              GraphAreaWidth, GraphAreaHeight);
    private Rect GraphHostBounds => new Rect(0, 0, GraphHost.ActualWidth, GraphHost.ActualHeight);
    private double GraphZoomRatio => GraphViewer.MaxGraphWidth / GraphAreaWidth;
    private double CenterZoomPointX => GraphHost.HorizontalOffset + GraphAreaWidth / 2;

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
                GraphViewer.SettingsUpdated(settings_);
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
                GraphViewer.SettingsUpdated(settings_);
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
            if (callTree != null && !GraphViewer.IsInitialized) {
                await GraphViewer.Initialize(callTree, GraphArea, settings_);
            }
        }, DispatcherPriority.Background);
    }

    public override void OnSessionStart() {
        base.OnSessionStart();
        InitializePendingCallTree();
    }

    private void SetupEvents() {
        GraphHost.SizeChanged += (sender, args) => UpdateGraphWidth(args.NewSize.Width);
        GraphHost.PreviewMouseWheel += OnPreviewMouseWheel;
        GraphHost.PreviewMouseDown += OnPreviewMouseDown;

        // Setup events for the flame graph area.
        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseDoubleClick += OnMouseDoubleClick;
        MouseMove += OnMouseMove;
        MouseDown += OnMouseDown;

        // Setup events for the node details view.
        NodeDetailsPanel.NodeInstanceChanged += NodeDetailsPanel_NodeInstanceChanged;
        NodeDetailsPanel.BacktraceNodeClick += NodeDetailsPanel_NodeClick;
        NodeDetailsPanel.BacktraceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
        NodeDetailsPanel.InstanceNodeClick += NodeDetailsPanel_NodeClick;
        NodeDetailsPanel.InstanceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
        NodeDetailsPanel.FunctionNodeClick += NodeDetailsPanel_NodeClick;
        NodeDetailsPanel.FunctionNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
        NodeDetailsPanel.NodesSelected += NodeDetailsPanel_NodesSelected;

        stackHoverPreview_ = new DraggablePopupHoverPreview(GraphViewer,
            CallTreeNodePopup.PopupHoverDuration,
            (mousePoint, previewPoint) => {
                var pointedNode = GraphViewer.FindPointedNode(mousePoint);
                var callNode = pointedNode?.CallTreeNode;

                if (callNode != null) {
                    // If popup already opened for this node reuse the instance.
                    if (stackHoverPreview_.PreviewPopup is CallTreeNodePopup popup) {
                        popup.UpdatePosition(previewPoint, GraphViewer);
                        popup.UpdateNode(callNode);
                        return popup;
                    }

                    return new CallTreeNodePopup(callNode, this, previewPoint, 350, 68, GraphViewer, Session);
                }

                return null;
            },
            (mousePoint, popup) => {
                if (popup is CallTreeNodePopup previewPopup) {
                    // Hide if not over the same node anymore.
                    var pointedNode = GraphViewer.FindPointedNode(mousePoint);
                    return previewPopup.CallTreeNode != pointedNode?.CallTreeNode;
                }

                return true;
            },
            popup => {
                Session.RegisterDetachedPanel(popup);
            });
    }

    private void NodeDetailsPanel_NodesSelected(object sender, List<ProfileCallTreeNode> e) {
        var nodes = GraphViewer.SelectNodes(e);
        BringNodeIntoView(nodes[0], false);
    }

    private async void NodeDetailsPanel_NodeInstanceChanged(object sender, ProfileCallTreeNode e) {
        var node = GraphViewer.SelectNode(e);
        BringNodeIntoView(node);
    }

    private async void NodeDetailsPanel_NodeClick(object sender, ProfileCallTreeNode e) {
        var nodes = GraphViewer.SelectNodes(e);
        BringNodeIntoView(nodes[0], false);
    }

    private async void NodeDetailsPanel_NodeDoubleClick(object sender, ProfileCallTreeNode e) {
        await OpenFunction(e);
    }

    private async void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) {
        HidePreviewPopup();

        if (IsMouseOutsideViewport(e)) {
            return;
        }

        var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

        if (pointedNode == null) {
            GraphViewer.ClearSelection(); // Click outside graph is captured here.
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
            if (Utils.IsControlModifierActive()) {
                await OpenFunction(pointedNode);
            }
            else {
                await EnlargeNode(pointedNode);
            }
        }
        else {
            double zoomPointX = e.GetPosition(GraphViewer).X;

            if (Utils.IsShiftModifierActive()) {
                ZoomOut(zoomPointX, considerSelectedNode: false);
            }
            else {
                ZoomIn(zoomPointX, considerSelectedNode: false);
            }
        }
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

    private void BringNodeIntoView(FlameGraphNode node, bool fitSize = true) {
        var bounds = GraphViewer.ComputeNodeBounds(node);
        var graphArea = GraphVisibleArea;
        DoubleAnimation animation1 = null;
        DoubleAnimation animation2 = null;

        if (bounds.Left < graphArea.Left || bounds.Right > graphArea.Right) {
            //? TODO: If node is outside on the right, increase offset to show it all
            animation1 = ScrollToHorizontalOffset(bounds.Left);
        }

        if (bounds.Top < graphArea.Top || bounds.Bottom > graphArea.Bottom) {
            animation2 = ScrollToVerticalOffset(bounds.Top);
        }

        if (fitSize) {
            switch ((animation1 != null, animation2 != null)) {
                case (true, true):
                case (true, false): {
                    animation1.Completed += (sender, e) => BringNodeIntoViewZoom(node);
                    break;
                }
                case (false, true): {
                    animation2.Completed += (sender, e) => BringNodeIntoViewZoom(node);
                    break;
                }
                default: {
                    BringNodeIntoViewZoom(node);
                    break;
                }
            }
        }

        animation1?.BeginAnimation(FlameGraphHorizontalOffsetProperty, animation1, HandoffBehavior.SnapshotAndReplace);
        animation2?.BeginAnimation(FlameGraphVerticalOffsetProperty, animation2, HandoffBehavior.SnapshotAndReplace);
    }

    private DoubleAnimation ScrollToHorizontalOffset(double offset, bool triggerAnimation = true,
                                                     bool animate = true, double duration = ZoomAnimationDuration) {
        if (animate) {
            var animation1 = new DoubleAnimation(GraphHost.HorizontalOffset, offset, TimeSpan.FromMilliseconds(duration));
            if (triggerAnimation) {
                BeginAnimation(FlameGraphHorizontalOffsetProperty, animation1, HandoffBehavior.SnapshotAndReplace);
            }

            return animation1;
        }
        else {
            GraphHost.ScrollToHorizontalOffset(offset);
            return null;
        }
    }

    private DoubleAnimation ScrollToVerticalOffset(double offset, bool triggerAnimation = true,
                                                   bool animate = true, double duration = ZoomAnimationDuration) {
        if (animate) {
            var animation1 = new DoubleAnimation(GraphHost.VerticalOffset, offset, TimeSpan.FromMilliseconds(duration));
            if (triggerAnimation) {
                BeginAnimation(FlameGraphVerticalOffsetProperty, animation1, HandoffBehavior.SnapshotAndReplace);
            }

            return animation1;
        }
        else {
            GraphHost.ScrollToVerticalOffset(offset);
            return null;
        }
    }

    private void BringNodeIntoViewZoom(FlameGraphNode node) {
        var bounds = GraphViewer.ComputeNodeBounds(node);
        double zoomX = 0;

        if (bounds.Width < 100) {
            zoomX = 100 - bounds.Width;
        }
        else if (bounds.Width > GraphAreaWidth) {
            zoomX = GraphAreaWidth - bounds.Width;
        }
        else {
            return;
        }

        double zoomPointX = bounds.Left + bounds.Width / 2;
        double nodeRation = GraphViewer.FlameGraph.InverseScaleWeight(node.Weight);
        double zoomAmount = zoomX * nodeRation;
        AdjustZoom(zoomAmount, zoomPointX, true, ZoomAnimationDuration);
    }

    private async Task EnlargeNode(FlameGraphNode node, bool saveState = true,
                                   double verticalOffset = double.NaN,
                                   double horizontalOffset = double.NaN) {
        if (Utils.IsAltModifierActive()) {
            await ChangeRootNode(node, true);
            return;
        }

        // Update the undo stack.
        if (saveState) {
            SaveCurrentState(FlameGraphStateKind.EnlargeNode);
        }

        // How wide the entire graph needs to be so that the node fils the view.
        double ratio = GraphViewer.FlameGraph.InverseScaleWeight(node.Weight);
        double newMaxWidth = GraphAreaWidth * ratio;
        double prevRatio = GraphViewer.MaxGraphWidth / GraphAreaWidth;
        double offsetRatio = prevRatio > 0 ? ratio / prevRatio : ratio;

        enlargedNode_ = node;
        initialWidth_ = newMaxWidth;
        initialOffsetX_ = GraphHost.HorizontalOffset;
        endOffsetX_ = GraphViewer.ComputeNodeBounds(node).X * offsetRatio;

        if (!double.IsNaN(horizontalOffset)) {
            endOffsetX_ = horizontalOffset;
        }

        SetMaxWidth(newMaxWidth, true, EnlargeAnimationDuration);
        var horizontalAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(EnlargeAnimationDuration));
        BeginAnimation(FlameGraphEnlargeNodeProperty, horizontalAnim, HandoffBehavior.SnapshotAndReplace);

        if (!double.IsNaN(verticalOffset)) {
            var verticalAnim = new DoubleAnimation(GraphHost.VerticalOffset, verticalOffset, TimeSpan.FromMilliseconds(EnlargeAnimationDuration));
            BeginAnimation(FlameGraphVerticalOffsetProperty, verticalAnim, HandoffBehavior.SnapshotAndReplace);

        }
    }

    public async Task ChangeRootNode(FlameGraphNode node, bool saveState = true) {
        // Update the undo stack.
        if (saveState) {
            SaveCurrentState(FlameGraphStateKind.ChangeRootNode);
        }

        ResetHighlightedNodes();
        await GraphViewer.Initialize(GraphViewer.FlameGraph.CallTree, node.CallTreeNode,
                                     GraphHostBounds, settings_);
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
                await EnlargeNode(node, false, state.VerticalOffset, state.HorizontalOffset);
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
            CaptureMouse();
            e.Handled = true;
        }
    }

    private async void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (dragging_) {
            dragging_ = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        if (IsMouseOutsideViewport(e)) {
            return;
        }

        var point = e.GetPosition(GraphHost);
        var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

        if (pointedNode == null) {
            GraphViewer.ClearSelection(); // Click outside graph is captured here.
        }
        else if (pointedNode.HasFunction) {
            await NodeDetailsPanel.ShowWithDetailsAsync(pointedNode.CallTreeNode);

            if (settings_.SyncSourceFile) {
                // Load the source file and scroll to the hottest line.
                var panel = Session.FindAndActivatePanel(ToolPanelKind.Source) as SourceFilePanel;

                if (panel != null) {
                    await panel.LoadSourceFile(pointedNode.CallTreeNode.Function.Sections[0]);
                }
            }
        }
    }

    private async void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        // Start dragging the graph only if the click starts inside the scroll area,
        // excluding the scroll bars, and it in an empty spot.
        if (IsMouseOutsideViewport(e)) {
            return;
        }

        var point = e.GetPosition(GraphHost);
        dragging_ = true;
        draggingStart_ = point;
        draggingViewStart_ = new Point(GraphHost.HorizontalOffset, GraphHost.VerticalOffset);
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

    DoubleAnimation widthAnimation_;
    DoubleAnimation zoomAnimation_;

    private void SetMaxWidth(double maxWidth, bool animate = true, double duration = ZoomAnimationDuration) {
        if (animate) {
            var animation = new DoubleAnimation(GraphViewer.MaxGraphWidth, maxWidth, TimeSpan.FromMilliseconds(duration));
            animation.Completed += (sender, args) => {
                widthAnimation_ = null;
            };

            widthAnimation_ = animation;
            BeginAnimation(FlameGraphWidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        else {
            CancelWidthAnimation();
            GraphViewer.UpdateMaxWidth(maxWidth);
        }
    }

    private void CancelWidthAnimation() {
        if(widthAnimation_ != null) {
            widthAnimation_.BeginTime = null;
            BeginAnimation(FlameGraphWidthProperty, widthAnimation_);
            widthAnimation_ = null;
        }
    }

    private void CancelZoomAnimation() {
        if (zoomAnimation_ != null) {
            zoomAnimation_.BeginTime = null;
            BeginAnimation(FlameGraphWidthProperty, zoomAnimation_);
            zoomAnimation_ = null;
        }
    }

    private void AdjustMaxWidth(double amount, bool animate = true, double duration = ZoomAnimationDuration) {
        double newWidth = Math.Max(GraphAreaWidth, GraphViewer.MaxGraphWidth + amount);
        SetMaxWidth(newWidth, animate, duration);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
        HidePreviewPopup();

        if (Utils.IsShiftModifierActive()) {
            // Turn vertical scrolling into horizontal scrolling.
            GraphHost.ScrollToHorizontalOffset(GraphHost.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }
        else if (!(Utils.IsKeyboardModifierActive() ||
            e.LeftButton == MouseButtonState.Pressed)) {
            // Zoom when Ctrl/Alt/Shift or left mouse button are pressed.
            return;
        }

        // Cancel ongoing dragging operation.
        if (dragging_) {
            dragging_ = false;
            ReleaseMouseCapture();
        }

        // Disable animation if scrolling without interruption.
        var time = DateTime.UtcNow;
        var timeElapsed = time - lastWheelZoomTime_;
        bool animate = timeElapsed.TotalMilliseconds > ScrollWheelZoomAnimationDuration;

        double amount = ScrollWheelZoomAmount * GraphZoomRatio; // Keep step consistent.
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

            animation.Completed += (sender, args) => {
                zoomAnimation_ = null;
            };

            zoomAnimation_ = animation;
            BeginAnimation(FlameGraphZoomProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        else {
            CancelWidthAnimation();
            CancelZoomAnimation();
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
        ScrollToVerticalOffset(0);
        ResetHighlightedNodes();
    }

    private void ExecuteGraphFitAll(object sender, ExecutedRoutedEventArgs e) {
        SetMaxWidth(GraphAreaWidth);
    }

    private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
        ZoomIn(CenterZoomPointX);
    }

    private void ZoomIn(double zoomPointX, bool considerSelectedNode = true) {
        if (considerSelectedNode && GraphViewer.SelectedNode !=null) {
            var bounds = GraphViewer.ComputeNodeBounds(GraphViewer.SelectedNode);
            zoomPointX = bounds.Left + bounds.Width / 2;
        }

        AdjustZoom(ZoomAmount * GraphZoomRatio, zoomPointX, true, ZoomAnimationDuration);
    }

    private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
        ZoomOut(CenterZoomPointX);
    }

    private void ZoomOut(double zoomPointX, bool considerSelectedNode = true)     {
        if (considerSelectedNode && GraphViewer.SelectedNode != null) {
            var bounds = GraphViewer.ComputeNodeBounds(GraphViewer.SelectedNode);
            zoomPointX = bounds.Left + bounds.Width / 2;
        }

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
        if (GraphViewer.IsInitialized) {
            GraphViewer.Reset();
        }

        var callTree = Session.ProfileData.CallTree;
        SchedulePendingCallTree(callTree);
    }

    public override void OnSessionEnd() {
        base.OnSessionEnd();
        callTree_ = null;
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
        var prevSearchResultNodes = searchResultNodes_;
        searchResultNodes_ = null;

        if (prevSearchResultNodes != null) {
            bool redraw = text.Length <= 1; // Prevent flicker by redrawing once when search is done.
            GraphViewer.ResetSearchResultNodes(prevSearchResultNodes, redraw);
        }

        if (text.Length > 1) {
            searchResultNodes_ = await Task.Run(() => GraphViewer.FlameGraph.SearchNodes(text));
            GraphViewer.MarkSearchResultNodes(searchResultNodes_);

            ShowSearchSection = true;
            SearchResultText = searchResultNodes_.Count > 0 ? $"{searchResultNodes_.Count}" : "Not found";
        }
        else {
            ShowSearchSection = false;
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e) {
        var sw2 = Stopwatch.StartNew();

        //? - process timeline
        //? - per-proc thread timeline
        //? - per-proc module timeline
        //? - per-proc core timeline
        //? o time range filter
        //? o group samples by process (cache)

        var dict = new Dictionary<int, TimeSpan>();
        var start = long.MaxValue;
        var end = long.MinValue;
        var profile = Session.ProfileData;

        if (profile.Samples.Count == 0) {
            return;
        }

        start = profile.Samples[0].Sample.Time.Ticks;
        end = profile.Samples[^1].Sample.Time.Ticks;

        // 1 tick = 100 ns
        long resolution = 10;
        var timeDiffNs = (end - start) / resolution;
        var timeDiff = TimeSpan.FromTicks(timeDiffNs);
        var timePerSlice = TimeSpan.FromMilliseconds(10);
        double width = timeDiff.Ticks / timePerSlice.Ticks;
        var sliceSeriesDict = new Dictionary<int, Dictionary<int, TimeSpan>>();
        List<Tuple<int, Dictionary<int, TimeSpan>>> sliceSeriesList = null;

        var sw3 = Stopwatch.StartNew();

        foreach (var (sample, stack) in profile.Samples) {
            var point = (sample.Time.Ticks - start) / resolution;
            var slice = (int)(point / timePerSlice.Ticks);

            var sliceDict = sliceSeriesDict.GetOrAddValue(stack.Context.ThreadId);
            sliceDict.AccumulateValue(slice, sample.Weight);
        }

        sliceSeriesList = sliceSeriesDict.ToList();

        //sliceSeriesList.Sort((a, b) => {
        //    if (!dict.TryGetValue(a.Item1, out var pa) ||
        //        !dict.TryGetValue(b.Item1, out var pb)) {
        //        return 0;
        //    }

        //    return -pa.CompareTo(pb);
        //});

        Trace.WriteLine($"2_PerProcSliceTime: {sw2.ElapsedMilliseconds}, {sw2.Elapsed}");
        Trace.Flush();


        //foreach (var pair in list) {
        //    var p = profile.FindProcess(pair.Item1);
        //    Trace.WriteLine($"{p.Id}, {p?.Name}: {pair.Item2}");
        //}

        int count = 0;
        var model = new PlotModel();


        foreach (var pair in sliceSeriesList) {
            var sliceList = pair.Item2.ToList();
            sliceList.Sort((a, b) => a.Item1.CompareTo(b.Item1)); // Sort slices by index.

            //var slicePlot = new OxyPlot.Series.LineSeries();
            var slicePlot = new OxyPlot.Series.BarSeries();
            slicePlot.BarWidth = 10;
            slicePlot.IsStacked = true;
            slicePlot.StrokeThickness = 0.5;
            //slicePlot.StrokeColor = OxyColor.FromArgb(255, 50, 50, 50);
            //slicePlot.StrokeThickness = 3;
            var c = ColorUtils.GenerateRandomPastelColor();
            //slicePlot.Color = OxyColor.FromRgb(c.R, c.G, c.B);
            slicePlot.FillColor = OxyColor.FromRgb(c.R, c.G, c.B);
            slicePlot.ToolTip = $"Thread #{pair.Item1}";

            slicePlot.Title = pair.Item1.ToString();
            model.Series.Add(slicePlot);

            foreach (var slicePair in sliceList) {
                var pointTime = TimeSpan.FromTicks(slicePair.Item1 * timePerSlice.Ticks).TotalSeconds;
                var item = new BarItem(slicePair.Item2.TotalMilliseconds);
                item.CategoryIndex = slicePair.Item1;
                slicePlot.Items.Add(item);
            }

            //if (count++ > 10)
            //    break;
            //Trace.WriteLine($"{pair.Key}: {pair.Item2}");
        }

        var w = new Window();
        w.Width = 500;
        w.Height = 300;
        var plotView = new OxyPlot.SkiaSharp.Wpf.PlotView();
        model.IsLegendVisible = true;
        model.Padding = new OxyThickness(0);

        model.Axes.Add(new CategoryAxis() {
            Position = AxisPosition.Left,
            TickStyle = TickStyle.Crossing,
            MajorGridlineStyle = LineStyle.Automatic,
            MinorGridlineStyle = LineStyle.None,
            IsZoomEnabled = true,
        });
        model.Axes.Add(new LinearAxis() {
            Position = AxisPosition.Bottom,
            TickStyle = TickStyle.Crossing,
            MajorGridlineStyle = LineStyle.Automatic,
            MinorGridlineStyle = LineStyle.None,
            IsZoomEnabled = false,
        });


        plotView.Model = Transpose(model);
        w.Content = plotView;
        w.Show();

        Trace.Flush();
    }

    private const string XAXIS_KEY = "x";
    private const string YAXIS_KEY = "y";

    public static PlotModel Transpose(PlotModel model) {
        if (!string.IsNullOrEmpty(model.Title)) {
            model.Title += " (transposed)";
        }

           // Update plot to generate default axes etc.
           ((IPlotModel)model).Update(false);

        foreach (var axis in model.Axes) {
            switch (axis.Position) {
                case AxisPosition.Bottom:
                    axis.Position = AxisPosition.Left;
                    break;
                case AxisPosition.Left:
                    axis.Position = AxisPosition.Bottom;
                    break;
                case AxisPosition.Right:
                    axis.Position = AxisPosition.Top;
                    break;
                case AxisPosition.Top:
                    axis.Position = AxisPosition.Right;
                    break;
                case AxisPosition.None:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        foreach (var annotation in model.Annotations) {
            if (annotation.XAxis != null && annotation.XAxisKey == null) {
                if (annotation.XAxis.Key == null) {
                    annotation.XAxis.Key = XAXIS_KEY;
                }

                annotation.XAxisKey = annotation.XAxis.Key;
            }

            if (annotation.YAxis != null && annotation.YAxisKey == null) {
                if (annotation.YAxis.Key == null) {
                    annotation.YAxis.Key = YAXIS_KEY;
                }

                annotation.YAxisKey = annotation.YAxis.Key;
            }
        }

        foreach (var series in model.Series.OfType<XYAxisSeries>()) {
            if (series.XAxisKey == null) {
                if (series.XAxis == null) // this can happen if the series is invisible initially
                {
                    series.XAxisKey = XAXIS_KEY;
                }
                else {
                    if (series.XAxis.Key == null) {
                        series.XAxis.Key = XAXIS_KEY;
                    }

                    series.XAxisKey = series.XAxis.Key;
                }
            }

            if (series.YAxisKey == null) {
                if (series.YAxis == null) {
                    series.YAxisKey = YAXIS_KEY;
                }
                else {
                    if (series.YAxis.Key == null) {
                        series.YAxis.Key = YAXIS_KEY;
                    }

                    series.YAxisKey = series.YAxis.Key;
                }
            }
        }

        return model;
    }
}