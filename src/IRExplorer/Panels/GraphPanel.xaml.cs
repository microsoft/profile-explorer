// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorer.OptionsPanels;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.GraphViz;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorer {
    // ScrollViewer that ignores click events.
    public class ScrollViewerClickable : ScrollViewer {
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { }
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) { }
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { }
    }

    [ProtoContract]
    public class GraphPanelState {
        [ProtoMember(3)]
        public double HorizontalOffset;
        [ProtoMember(2)]
        public double VerticalOffset;
        [ProtoMember(1)]
        public double ZoomLevel;
    }

    class GraphQueryInfo {
        public BlockIR Block1 { get; set; }
        public BlockIR Block2 { get; set; }
        public string Block1Name { get; set; }
        public string Block2Name { get; set; }
        public bool Dominates { get; set; }
        public bool PostDominates { get; set; }
        public bool Reaches { get; set; }
        public bool ControlDependent { get; set; }
        public bool OnDomFrontier { get; set; }

        public void SwapBlocks() {
            var tempBlock = Block1;
            string tempBlockName = Block1Name;
            Block1 = Block2;
            Block1Name = Block2Name;
            Block2 = tempBlock;
            Block2Name = tempBlockName;
        }
    }

    public partial class GraphPanel : ToolPanelControl {
        private static readonly double FastPanOffset = 80;
        private static readonly double FastZoomFactor = 4;
        private static readonly double MaxZoomLevel = 1.0;
        private static readonly double MinZoomLevel = 0.05;
        private static readonly double PanOffset = 20;
        private static readonly double ZoomAdjustment = 0.05;
        private static readonly double HorizontalViewMargin = 50;
        private static readonly double VerticalViewMargin = 100;
        private bool delayFitSize_;
        private bool delayRestoreState_;

        private IRDocument document_;

        private bool dragging_;
        private Point draggingStart_;
        private Point draggingViewStart_;
        private LayoutGraph graph_;
        private GraphNode hoveredNode_;

        private bool ignoreNextHover_;
        private CancelableTaskInfo loadTask_;
        private ToolTip nodeToolTip_;
        private bool optionsPanelVisible_;

        private GraphQueryInfo queryInfo_;
        private bool queryPanelVisible_;
        private bool restoredState_;
        private IRTextSection section_;

        public GraphPanel() {
            InitializeComponent();
            GraphViewer.HostPanel = this;
            GraphViewer.MaxZoomLevel = MaxZoomLevel;
            GraphViewer.BlockSelected += GraphViewer_BlockSelected;
            PreviewMouseWheel += GraphPanel_PreviewMouseWheel;
            PreviewMouseLeftButtonDown += GraphPanel_PreviewMouseLeftButtonDown;
            PreviewMouseRightButtonDown += GraphPanel_PreviewMouseRightButtonDown;
            MouseLeftButtonDown += GraphPanel_MouseLeftButtonDown;
            MouseLeftButtonUp += GraphPanel_MouseLeftButtonUp;
            MouseMove += GraphPanel_MouseMove;
            MouseLeave += GraphPanel_MouseLeave;
            PreviewKeyDown += GraphPanel_PreviewKeyDown;
            GraphHost.ScrollChanged += GraphHost_ScrollChanged;
            var hover = new MouseHoverLogic(this);
            hover.MouseHover += Hover_MouseHover;
            hover.MouseHoverStopped += Hover_MouseHoverStopped;

            //? TODO: No context menu for expr graph yet, don't show CFG one.
            if (PanelKind == ToolPanelKind.ExpressionGraph) {
                GraphViewer.ContextMenu = null;
            }

            SetupCommands();
        }

        public IRElement Element {
            get => GraphViewer.SelectedElement;
            set {
                GraphViewer.SelectElement(value);

                if (!HasPinnedContent && Settings.BringNodesIntoView) {
                    BringIntoView(value);
                }
            }
        }

        private void OptionsPanel_PanelReset(object sender, EventArgs e) {
            Settings.Reset();
            graphOptionsPanel_.Settings = null;
            graphOptionsPanel_.Settings = Settings.Clone();
        }

        private void OptionsPanel_PanelClosed(object sender, EventArgs e) {
            CloseOptionsPanel();
        }

        private void GraphPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            var position = e.GetPosition(GraphViewer);
            hoveredNode_ = GraphViewer.FindPointedNode(position);

            if (hoveredNode_ != null) {
                // Select A/B nodes in the query panel.
                if (Keyboard.IsKeyDown(Key.A) ||
                    Keyboard.IsKeyDown(Key.NumPad1) ||
                    Keyboard.IsKeyDown(Key.D1)) {
                    SelectQueryBlock1Executed(this, null);
                    e.Handled = true;
                    return;
                }
                else if (Keyboard.IsKeyDown(Key.B) ||
                         Keyboard.IsKeyDown(Key.NumPad2) ||
                         Keyboard.IsKeyDown(Key.D2)) {
                    SelectQueryBlock2Executed(this, null);
                    e.Handled = true;
                    return;
                }

                Focus();
            }
        }

        private void GraphPanel_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            hoveredNode_ = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

            if (hoveredNode_ != null) {
                Focus();
            }
        }

        public void BringIntoView(IRElement element) {
            var node = GraphViewer.FindElementNode(element);

            if (node == null) {
                return;
            }

            var position = GraphViewer.GetNodePosition(node);
            double offsetX = GraphHost.HorizontalOffset;
            double offsetY = GraphHost.VerticalOffset;

            if (position.X < offsetX || position.X > offsetX + GraphHost.ActualWidth) {
                GraphHost.ScrollToHorizontalOffset(Math.Max(0, position.X - HorizontalViewMargin));
            }

            if (position.Y < offsetY || position.Y > offsetY + GraphHost.ActualHeight) {
                GraphHost.ScrollToVerticalOffset(Math.Max(0, position.Y - VerticalViewMargin));
            }
        }

        public void DisplayGraph(LayoutGraph graph) {
            graph_ = graph;
            GraphViewer.ShowGraph(graph, Session.CompilerInfo);
            FitGraphIntoView();

            if (delayRestoreState_) {
                LoadSavedState();
            }

            IsPanelEnabled = true;
            HasPinnedContent = false;
            Utils.EnableControl(GraphHost);
        }

        public void HideGraph() {
            if (graph_ == null) {
                return;
            }

            GraphViewer.HideGraph();
            graph_ = null;
            document_ = null;
            section_ = null;
            hoveredNode_ = null;
            Utils.DisableControl(GraphViewer);
            IsPanelEnabled = false;
        }

        private void FitGraphIntoView() {
            var viewBounds = GetGraphBounds();

            if (Math.Abs(viewBounds.Width) < double.Epsilon || Math.Abs(viewBounds.Height) < double.Epsilon) {
                // Panel is not visible, set the graph size when it becomes visible.
                delayFitSize_ = true;
            }
            else if (!restoredState_) {
                GraphViewer.FitWidthToSize(viewBounds);
                delayFitSize_ = false;
            }
        }

        private Size GetGraphBounds() {
            return new Size(Math.Max(0, RenderSize.Width - SystemParameters.VerticalScrollBarWidth),
                            RenderSize.Height);
        }

        public void Highlight(IRHighlightingEventArgs info) {
            if (!Settings.SyncMarkedNodes && info.Type == HighlighingType.Marked) {
                return;
            }

            GraphViewer.Highlight(info);
        }

        public void InitializeFromDocument(IRDocument doc) {
            Trace.TraceInformation($"Graph panel {ObjectTracker.Track(this)}: initialize with doc {ObjectTracker.Track(doc)}");
            document_ = doc;
        }

        private CancelableTaskInfo CreateGraphLoadTask() {
            lock (this) {
                if (loadTask_ != null) {
                    CancelGraphLoadTask();
                }

                loadTask_ = new CancelableTaskInfo();
                return loadTask_;
            }
        }

        private void CancelGraphLoadTask() {
            Session.SessionState.UnregisterCancelableTask(loadTask_);
            loadTask_.Cancel();
            loadTask_.WaitToComplete(TimeSpan.FromSeconds(5));
            loadTask_.Dispose();
            loadTask_ = null;
        }

        private void CompleteGraphLoadTask(CancelableTaskInfo task) {
            lock (this) {
                if (task != loadTask_) {
                    return; // A canceled task.
                }

                if (loadTask_ != null) {
                    Session.SessionState.UnregisterCancelableTask(loadTask_);
                    loadTask_.Completed();
                    loadTask_.Dispose();
                    loadTask_ = null;
                }
            }
        }

        public CancelableTaskInfo OnGenerateGraphStart(IRTextSection section) {
            section_ = section;
            Utils.DisableControl(GraphViewer);
            var animation = new DoubleAnimation(0.25, TimeSpan.FromSeconds(0.5));
            animation.BeginTime = TimeSpan.FromSeconds(1);
            GraphViewer.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            LongOperationView.Opacity = 0.0;
            LongOperationView.Visibility = Visibility.Visible;
            var animation2 = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.5));
            animation2.BeginTime = TimeSpan.FromSeconds(2);
            LongOperationView.BeginAnimation(OpacityProperty, animation2, HandoffBehavior.SnapshotAndReplace);
            return CreateGraphLoadTask();
        }

        public void OnGenerateGraphDone(CancelableTaskInfo task, bool failed = false) {
            if (!failed) {
                Utils.EnableControl(GraphViewer);
                GraphHost.ScrollToHorizontalOffset(0);
                GraphHost.ScrollToVerticalOffset(0);
                var animation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.2));
                GraphViewer.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }

            var animation2 = new DoubleAnimation(0, TimeSpan.FromSeconds(0.2));
            animation2.Completed += Animation2_Completed;
            LongOperationView.BeginAnimation(OpacityProperty, animation2, HandoffBehavior.SnapshotAndReplace);
            CompleteGraphLoadTask(task);
        }

        public void RemoveHighlighting(IRElement element) {
            var node = GraphViewer.FindElementNode(element);

            if (node != null) {
                GraphViewer.ResetMarkedNode(node);
            }
        }

        private void AddCommand(RoutedCommand command, ExecutedRoutedEventHandler handler) {
            var binding = new CommandBinding(command);
            binding.Executed += handler;
            CommandBindings.Add(binding);
        }

        private void AdjustZoom(double value) {
            if (Utils.IsShiftModifierActive()) {
                value *= FastZoomFactor;
            }

            SetZoom(GraphViewer.ZoomLevel + value);
        }

        private void Animation2_Completed(object sender, EventArgs e) {
            LongOperationView.Visibility = Visibility.Collapsed;
        }

        private Size AvailableGraphSize() {
            return new Size(
                GraphHost.RenderSize.Width -
                SystemParameters.VerticalScrollBarWidth -
                SystemParameters.BorderWidth * 2, GraphHost.RenderSize.Height);
        }

        private void Button_MouseDown(object sender, MouseButtonEventArgs e) {
            AdjustZoom(-ZoomAdjustment);
        }

        private void Button_MouseDown_1(object sender, MouseButtonEventArgs e) {
            AdjustZoom(ZoomAdjustment);
        }

        private void ClearAllMarkedExecuted(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.ResetAllMarkedNodes();
        }

        private void ClearMarkedExecuted(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.ResetSelectedNode();
        }

        private void ExecuteGraphFitAll(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.FitToSize(AvailableGraphSize());
        }

        private void ExecuteGraphFitWidth(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.FitWidthToSize(AvailableGraphSize());
        }

        private void ExecuteGraphResetWidth(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.ZoomLevel = 1;
        }

        private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
            AdjustZoom(ZoomAdjustment);
        }

        private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
            AdjustZoom(-ZoomAdjustment);
        }

        private double GetPanOffset() {
            return Utils.IsKeyboardModifierActive() ? FastPanOffset : PanOffset;
        }

        private HighlightingStyle GetSelectedColorStyle(ExecutedRoutedEventArgs e) {
            return Utils.GetSelectedColorStyle(e.Parameter as ColorEventArgs, GraphViewer.DefaultBoldPen);
        }

        private void GraphHost_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            HideTooltip();
        }

        private void GraphPanel_MouseLeave(object sender, MouseEventArgs e) {
            HideTooltip();
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

            //? TODO: DOn't handle if over query panel
            var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

            if (pointedNode != null) {
                return;
            }

            dragging_ = true;
            draggingStart_ = e.GetPosition(GraphHost);
            draggingViewStart_ = new Point(GraphHost.HorizontalOffset, GraphHost.VerticalOffset);
            CaptureMouse();
            HideTooltip();
            e.Handled = true;
        }

        private void GraphPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            if (dragging_) {
                dragging_ = false;
                ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void GraphPanel_MouseMove(object sender, MouseEventArgs e) {
            if (dragging_) {
                var offset = draggingViewStart_ - (e.GetPosition(GraphHost) - draggingStart_);
                GraphHost.ScrollToHorizontalOffset(offset.X);
                GraphHost.ScrollToVerticalOffset(offset.Y);
                e.Handled = true;
            }
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
                        AdjustZoom(ZoomAdjustment);
                        e.Handled = true;
                    }

                    return;
                }
                case Key.OemMinus:
                case Key.Subtract: {
                    if (Utils.IsControlModifierActive()) {
                        e.Handled = true;
                        AdjustZoom(-ZoomAdjustment);
                    }

                    return;
                }
            }

            if (e.Handled) {
                HideTooltip();
                GraphHost.ScrollToHorizontalOffset(offsetX);
                GraphHost.ScrollToVerticalOffset(offsetY);
            }
        }

        private void GraphPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if (!Utils.IsKeyboardModifierActive()) {
                return;
            }

            SetZoom(GraphViewer.ZoomLevel * (1 + e.Delta / 1000.0));
            e.Handled = true;
        }

        private void GraphViewer_BlockSelected(object sender, IRElementEventArgs e) {
            ignoreNextHover_ = true;
        }

        private void HideTooltip() {
            if (nodeToolTip_ != null) {
                nodeToolTip_.IsOpen = false;
                nodeToolTip_ = null;
            }
        }

        private void Hover_MouseHover(object sender, MouseEventArgs e) {
            if (!Settings.ShowPreviewPopup) {
                return;
            }

            if (Settings.ShowPreviewPopupWithModifier && !Utils.IsShiftModifierActive()) {
                return;
            }

            if (ignoreNextHover_) {
                // Don't show the block preview if the user jumped to it.
                ignoreNextHover_ = false;
                return;
            }

            var node = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

            if (node?.NodeInfo.Element != null) {
                ShowTooltipForNode(node);
                hoveredNode_ = node;
            }
        }

        private void Hover_MouseHoverStopped(object sender, MouseEventArgs e) {
            HideTooltip();
            ignoreNextHover_ = false;
        }

        private void MarkBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.MarkSelectedNode(GetSelectedColorStyle(e));
        }

        private void MarkGroupExecuted(object sender, ExecutedRoutedEventArgs e) {
            var style = GetSelectedColorStyle(e);
            GraphViewer.MarkSelectedNode(style);
            GraphViewer.MarkSelectedNodeSuccessors(style);
            GraphViewer.MarkSelectedNodePredecessors(style);
        }

        private void MarkLoopExecuted(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.MarkSelectedNodeLoop(GetSelectedColorStyle(e));
        }

        private void MarkLoopNestExecuted(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.MarkSelectedNodeLoopNest(GetSelectedColorStyle(e));
        }

        private void MarkPredecessorsExecuted(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.MarkSelectedNodePredecessors(GetSelectedColorStyle(e));
        }

        private void MarkSuccessorsExecuted(object sender, ExecutedRoutedEventArgs e) {
            GraphViewer.MarkSelectedNodeSuccessors(GetSelectedColorStyle(e));
        }

        [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "event handler")]
        private async void MarkDominatorsExecuted(object sender, ExecutedRoutedEventArgs e) {
            await GraphViewer.MarkSelectedNodeDominatorsAsync(GetSelectedColorStyle(e)).ConfigureAwait(true);
        }

        [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "event handler")]
        private async void MarkPostDominatorsExecuted(object sender, ExecutedRoutedEventArgs e) {
            await GraphViewer.MarkSelectedNodePostDominatorsAsync(GetSelectedColorStyle(e)).ConfigureAwait(true);
        }

        [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "event handler")]
        private async void MarkDominanceFrontierExecuted(object sender, ExecutedRoutedEventArgs e) {
            await GraphViewer.MarkSelectedNodeDominanceFrontierAsync(GetSelectedColorStyle(e)).ConfigureAwait(true);
        }

        [SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "event handler")]
        private async void MarkPostDominanceFrontierExecuted(object sender, ExecutedRoutedEventArgs e) {
            await GraphViewer.MarkSelectedNodePostDominanceFrontierAsync(GetSelectedColorStyle(e)).ConfigureAwait(true);
        }

        private void SelectQueryBlock1Executed(object sender, ExecutedRoutedEventArgs e) {
            if (hoveredNode_ != null) {
                if (hoveredNode_.NodeInfo.Element is BlockIR block) {
                    SetQueryBlock1(block);
                }
            }
        }

        private void SelectQueryBlock2Executed(object sender, ExecutedRoutedEventArgs e) {
            if (hoveredNode_ != null) {
                if (hoveredNode_.NodeInfo.Element is BlockIR block) {
                    SetQueryBlock2(block);
                }
            }
        }

        private async void SwapQueryBlocksExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!queryPanelVisible_) {
                return;
            }

            queryInfo_.SwapBlocks();
            await UpdateQueryResult();
        }

        private void CloseQueryPanelExecuted(object sender, ExecutedRoutedEventArgs e) {
            HideQueryPanel();
        }

        private async void ShowReachablePathExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (queryPanelVisible_ && queryInfo_.Reaches) {
                var cache = FunctionAnalysisCache.Get(document_.Function);

                var pathBlocks =
                    (await cache.GetReachabilityAsync()).FindPath(queryInfo_.Block1, queryInfo_.Block2);

                foreach (var block in pathBlocks) {
                    GraphViewer.Mark(block, Colors.Gold);
                }
            }
        }

        private void NodeToolTip__Loaded(object sender, RoutedEventArgs e) {
            var node = nodeToolTip_.Tag as GraphNode;
            var previewer = Utils.FindChild<IRPreview>(nodeToolTip_, "IRPreviewer");
            previewer.InitializeFromDocument(document_);

            if (node.NodeInfo.Element is BlockIR block) {
                previewer.PreviewedElement = block;
                nodeToolTip_.DataContext = new BlockTooltipInfo(block);

                // 5 lines are needed to not truncate the block info labels.
                int lines = Math.Max(5, Math.Min(block.Tuples.Count + 1, 20));
                nodeToolTip_.Height = previewer.ResizeForLines(lines);
                previewer.UpdateView(false);
            }
            else {
                var element = node.NodeInfo.Element;
                previewer.PreviewedElement = element;
                nodeToolTip_.DataContext = new IRPreviewToolTip(600, 100, document_, element);
                int lines = Math.Max(1, Math.Min(element.ParentBlock.Tuples.Count + 1, 20));
                nodeToolTip_.Height = previewer.ResizeForLines(lines);
                previewer.UpdateView();
            }
        }

        private void PanelToolbarTray_DuplicateClicked(object sender, DuplicateEventArgs e) {
            Session.DuplicatePanel(this, e.Kind);
        }

        private void PanelToolbarTray_PinnedChanged(object sender, PinEventArgs e) {
            HasPinnedContent = e.IsPinned;
        }

        private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
            if (optionsPanelVisible_) {
                CloseOptionsPanel();
            }
            else {
                ShowOptionsPanel();
            }
        }

        private async void SetQueryBlock1(BlockIR block) {
            ShowQueryPanel();

            if (queryInfo_.Block1 != block) {
                queryInfo_.Block1 = block;
                queryInfo_.Block1Name = Utils.MakeBlockDescription(block);
                await UpdateQueryResult();
            }
        }

        private async void SetQueryBlock2(BlockIR block) {
            ShowQueryPanel();

            if (queryInfo_.Block2 != block) {
                queryInfo_.Block2 = block;
                queryInfo_.Block2Name = Utils.MakeBlockDescription(block);
                await UpdateQueryResult();
            }
        }

        private async Task UpdateQueryResult() {
            QueryPanel.DataContext = null;

            if (queryInfo_.Block1 != null && queryInfo_.Block2 != null) {
                var cache = FunctionAnalysisCache.Get(document_.Function);
                await cache.CacheAllAsync();

                queryInfo_.Dominates =
                    (await cache.GetDominatorsAsync()).Dominates(queryInfo_.Block1, queryInfo_.Block2);

                queryInfo_.PostDominates =
                    (await cache.GetPostDominatorsAsync()).Dominates(queryInfo_.Block1, queryInfo_.Block2);

                queryInfo_.Reaches =
                    (await cache.GetReachabilityAsync()).Reaches(queryInfo_.Block1, queryInfo_.Block2);
            }

            QueryPanel.DataContext = queryInfo_;
        }

        private void ShowQueryPanel() {
            if (queryPanelVisible_) {
                return;
            }

            queryInfo_ = new GraphQueryInfo();
            QueryPanel.DataContext = queryInfo_;
            QueryPanel.Visibility = Visibility.Visible;
            queryPanelVisible_ = true;
        }

        private void HideQueryPanel() {
            if (!queryPanelVisible_) {
                return;
            }

            QueryPanel.DataContext = null;
            QueryPanel.Visibility = Visibility.Collapsed;
            queryPanelVisible_ = false;
        }

        private OptionsPanelHostWindow graphOptionsPanel_;

        //? TODO: Should be a virtual overriden in the expr panel
        private void ShowOptionsPanel() {
            if (optionsPanelVisible_) {
                return;
            }

            if (PanelKind == ToolPanelKind.ExpressionGraph) {
                var width = Math.Max(ExpressionGraphOptionsPanel.MinimumWidth,
                    Math.Min(GraphHost.ActualWidth, ExpressionGraphOptionsPanel.DefaultWidth));
                var height = Math.Max(ExpressionGraphOptionsPanel.MinimumHeight,
                    Math.Min(GraphHost.ActualHeight, ExpressionGraphOptionsPanel.DefaultHeight));
                var position = GraphHost.PointToScreen(new Point(GraphHost.ActualWidth - width, 0));
                graphOptionsPanel_ = new OptionsPanelHostWindow(new ExpressionGraphOptionsPanel(),
                                                                  position, width, height, this);
            }
            else {
                var width = Math.Max(GraphOptionsPanel.MinimumWidth,
                    Math.Min(GraphHost.ActualWidth, GraphOptionsPanel.DefaultWidth));
                var height = Math.Max(GraphOptionsPanel.MinimumHeight,
                    Math.Min(GraphHost.ActualHeight, GraphOptionsPanel.DefaultHeight));
                var position = GraphHost.PointToScreen(new Point(GraphHost.ActualWidth - width, 0));
                graphOptionsPanel_ = new OptionsPanelHostWindow(new GraphOptionsPanel(),
                                                                  position, width, height, this);
            }

            graphOptionsPanel_.PanelClosed += OptionsPanel_PanelClosed;
            graphOptionsPanel_.PanelReset += OptionsPanel_PanelReset;
            graphOptionsPanel_.Settings = Settings.Clone();
            graphOptionsPanel_.IsOpen = true;
            ;
            optionsPanelVisible_ = true;
        }

        private void CloseOptionsPanel() {
            if (!optionsPanelVisible_) {
                return;
            }

            graphOptionsPanel_.IsOpen = false;
            graphOptionsPanel_.PanelClosed -= OptionsPanel_PanelClosed;
            graphOptionsPanel_.PanelReset -= OptionsPanel_PanelReset;

            if (PanelKind == ToolPanelKind.ExpressionGraph) {
                var newSettings = (ExpressionGraphSettings)graphOptionsPanel_.Settings;

                if (newSettings.HasChanges(Settings)) {
                    App.Settings.ExpressionGraphSettings = newSettings;
                    App.SaveApplicationSettings();
                    ReloadSettings();
                }
            }
            else {
                var newSettings = (FlowGraphSettings)graphOptionsPanel_.Settings;

                if (newSettings.HasChanges(Settings)) {
                    App.Settings.FlowGraphSettings = newSettings;
                    App.SaveApplicationSettings();
                    ReloadSettings();
                }
            }

            graphOptionsPanel_ = null;
            optionsPanelVisible_ = false;
        }

        private void SetupCommands() {
            AddCommand(GraphCommand.MarkBlock, MarkBlockExecuted);
            AddCommand(GraphCommand.MarkPredecessors, MarkPredecessorsExecuted);
            AddCommand(GraphCommand.MarkSuccessors, MarkSuccessorsExecuted);
            AddCommand(GraphCommand.MarkDominators, MarkDominatorsExecuted);
            AddCommand(GraphCommand.MarkPostDominators, MarkPostDominatorsExecuted);
            AddCommand(GraphCommand.MarkDominanceFrontier, MarkDominanceFrontierExecuted);
            AddCommand(GraphCommand.MarkPostDominanceFrontier, MarkPostDominanceFrontierExecuted);
            AddCommand(GraphCommand.MarkGroup, MarkGroupExecuted);
            AddCommand(GraphCommand.MarkLoop, MarkLoopExecuted);
            AddCommand(GraphCommand.MarkLoopNest, MarkLoopNestExecuted);
            AddCommand(GraphCommand.ClearMarked, ClearMarkedExecuted);
            AddCommand(GraphCommand.ClearAllMarked, ClearAllMarkedExecuted);
            AddCommand(GraphCommand.SelectQueryBlock1, SelectQueryBlock1Executed);
            AddCommand(GraphCommand.SelectQueryBlock2, SelectQueryBlock2Executed);
            AddCommand(GraphCommand.SwapQueryBlocks, SwapQueryBlocksExecuted);
            AddCommand(GraphCommand.CloseQueryPanel, CloseQueryPanelExecuted);
            AddCommand(GraphCommand.ShowReachablePath, ShowReachablePathExecuted);
        }

        private void SetZoom(double value) {
            double currentZoom = GraphViewer.ZoomLevel;
            double centerX = GraphHost.ViewportWidth / 2;
            double centerY = GraphHost.ViewportHeight / 2;
            double offsetX = (GraphHost.HorizontalOffset + centerX) / currentZoom;
            double offsetY = (GraphHost.VerticalOffset + centerY) / currentZoom;
            double zoom = value;
            zoom = Math.Min(Math.Max(MinZoomLevel, zoom), MaxZoomLevel);
            GraphViewer.ZoomLevel = zoom;
            UpdateLayout();
            GraphHost.ScrollToHorizontalOffset(offsetX * zoom - centerX);
            GraphHost.ScrollToVerticalOffset(offsetY * zoom - centerY);
        }

        private void ShowTooltipForNode(GraphNode node) {
            if (nodeToolTip_ != null) {
                if (hoveredNode_ == node) {
                    return;
                }
                else {
                    HideTooltip();
                }
            }

            if (node == null) {
                return;
            }

            nodeToolTip_ = new ToolTip();

            string previewControl = PanelKind == ToolPanelKind.ExpressionGraph
                ? "ExpressionIRPreviewTooltip"
                : "BlockIRPreviewTooltip";

            nodeToolTip_.Style = Application.Current.FindResource(previewControl) as Style;
            nodeToolTip_.Width = 600;
            nodeToolTip_.Height = 100;
            nodeToolTip_.Tag = node;
            nodeToolTip_.Loaded += NodeToolTip__Loaded;
            nodeToolTip_.IsOpen = true;
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        public void RemoveAllHighlighting(HighlighingType type) {
            GraphViewer.ResetHighlightedNodes(type);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            lock (this) {
                loadTask_?.Cancel();
            }
        }

        private void QueryPanel_MouseEnter(object sender, MouseEventArgs e) {
            var animation = new DoubleAnimation(1, TimeSpan.FromSeconds(0.1));
            QueryPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private void QueryPanel_MouseLeave(object sender, MouseEventArgs e) {
            var animation = new DoubleAnimation(0.5, TimeSpan.FromSeconds(0.3));
            QueryPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private class BlockTooltipInfo {
            public BlockTooltipInfo(BlockIR block) {
                PredecessorCount = block.Predecessors.Count;
                SuccessorCount = block.Successors.Count;
                var loopTag = block.GetTag<LoopBlockTag>();

                if (loopTag != null) {
                    InLoop = true;
                    LoopNesting = loopTag.NestingLevel;
                    LoopBlocks = loopTag.Loop.Blocks.Count;
                    NestedLoops = loopTag.Loop.NestedLoops.Count;
                }
            }

            public bool InLoop { get; set; }
            public int LoopBlocks { get; set; }
            public int LoopNesting { get; set; }
            public int NestedLoops { get; set; }
            public int PredecessorCount { get; set; }
            public int SuccessorCount { get; set; }
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.FlowGraph;

        public override HandledEventKind HandledEvents =>
            HandledEventKind.ElementSelection | HandledEventKind.ElementHighlighting;

        public GraphSettings Settings =>
            PanelKind == ToolPanelKind.ExpressionGraph
                ? App.Settings.ExpressionGraphSettings
                : (GraphSettings)App.Settings.FlowGraphSettings;

        public override void OnRegisterPanel() {
            IsPanelEnabled = false;
            ReloadSettings();
        }

        private void ReloadSettings() {
            GraphViewer.Settings = Settings;
            GraphHost.Background = ColorBrushes.GetBrush(Settings.BackgroundColor);
        }

        public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            InitializeFromDocument(document);

            if (document.DuringSectionLoading) {
                Trace.TraceInformation(
                    $"Graph panel {ObjectTracker.Track(this)}: Ignore graph reload during section switch");

                return;
            }

            //? TODO: Implement switching for expressions
            if (PanelKind == ToolPanelKind.ExpressionGraph) {
                HideGraph();
                return;
            }

            if (section != null && section != section_) {
                // User switched between two sections, reload the proper graph.
                await Session.SwitchGraphsAsync(this, section, document);
            }

            if (!restoredState_) {
                delayRestoreState_ = true;
            }
            else {
                LoadSavedState();
            }
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            HideGraph();
        }

        private void LoadSavedState() {
            //? TODO: This can happen for the expression graph, which does not support switching.
            if(document_ == null) {
                return;
            }

            var data = Session.LoadPanelState(this, document_.Section) as byte[];
            var state = StateSerializer.Deserialize<GraphPanelState>(data, document_.Function);

            if (state != null) {
                SetZoom(state.ZoomLevel);
                GraphHost.ScrollToHorizontalOffset(state.HorizontalOffset);
                GraphHost.ScrollToVerticalOffset(state.VerticalOffset);
                restoredState_ = true;
            }
            else {
                GraphHost.ScrollToHorizontalOffset(0);
                GraphHost.ScrollToVerticalOffset(0);
                restoredState_ = false;
            }

            delayRestoreState_ = false;
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            HideTooltip();
            HideQueryPanel();
            Utils.DisableControl(GraphHost);
            restoredState_ = false;

            var state = new GraphPanelState();
            state.ZoomLevel = GraphViewer.ZoomLevel;
            state.HorizontalOffset = GraphHost.HorizontalOffset;
            state.VerticalOffset = GraphHost.VerticalOffset;
            var data = StateSerializer.Serialize(state, document.Function);
            Session.SavePanelState(data, this, section);

            // Clear references to IR objects that would keep the previous function alive.
            Trace.TraceInformation($"Graph panel {ObjectTracker.Track(this)}: unloaded doc {ObjectTracker.Track(document_)}");
            document_ = null;
            section_ = null;
            graph_ = null;
            hoveredNode_ = null;
        }

        public override void OnElementSelected(IRElementEventArgs e) {
            if (!Settings.SyncSelectedNodes) {
                return;
            }

            if (e.Element is BlockIR block) {
                Element = block;
            }
        }

        public override void OnElementHighlighted(IRHighlightingEventArgs e) {
            if (!Settings.SyncSelectedNodes) {
                return;
            }

            if (e.Action == HighlightingEventAction.ReplaceHighlighting ||
                e.Action == HighlightingEventAction.AppendHighlighting) {
                Highlight(e);
            }
            else if (e.Action == HighlightingEventAction.RemoveHighlighting) {
                if (e.Element != null) {
                    RemoveHighlighting(e.Element);
                }
                else {
                    RemoveAllHighlighting(e.Type);
                }
            }
        }

        public override void OnActivatePanel() {
            if (delayFitSize_ && GraphViewer.IsGraphLoaded) {
                FitGraphIntoView();
            }
        }

        public override void ClonePanel(IToolPanel sourcePanel) {
            //? TODO: This shouldn't be done here
            var sourceGraphPanel = sourcePanel as GraphPanel;
            InitializeFromDocument(sourceGraphPanel.document_);

            // Rebuild the graph, otherwise the visual nodes
            // point to the same instance.
            //? TODO: This is not efficient regarding memory, SourceText and BlockNameMap
            //? waste a lot of space for large graphs
            //var graphReader = new GraphvizReader(sourceGraphPanel.graph_.GraphKind,
            //                                     sourceGraphPanel.graph_.SourceText,
            //                                     sourceGraphPanel.graph_.BlockNameMap);

            //var newGraph = graphReader.ReadGraph();
            //DisplayGraph(newGraph);
        }

        #endregion
    }
}
