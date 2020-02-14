// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Core;
using Core.Analysis;
using Core.GraphViz;
using Core.IR;
using ICSharpCode.AvalonEdit.Rendering;
using ProtoBuf;

namespace Client {
    // ScrollViewer that ignores click events.
    public class ScrollViewerClickable : ScrollViewer {
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { }
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) { }
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { }
    }

    [ProtoContract]
    public class GraphPanelState {
        [ProtoMember(1)]
        public double ZoomLevel;
        [ProtoMember(2)]
        public double VerticalOffset;
        [ProtoMember(3)]
        public double HorizontalOffset;

        public GraphPanelState() { }
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
            var tempBlockName = Block1Name;
            Block1 = Block2;
            Block1Name = Block2Name;
            Block2 = tempBlock;
            Block2Name = tempBlockName;
        }
    }

    public partial class GraphPanel : ToolPanelControl {
        static readonly double FastPanOffset = 80;
        static readonly double FastZoomFactor = 4;
        static readonly double MaxZoomLevel = 1.0;
        static readonly double MinZoomLevel = 0.05;
        static readonly double PanOffset = 20;
        static readonly double ZoomAdjustment = 0.05;
        static readonly double HorizontalViewMargin = 50;
        static readonly double VerticalViewMargin = 100;

        private FlowGraphSettings settings_;
        private IRDocument document_;
        private IRTextSection section_;
        private LayoutGraph graph_;
        private CancelableTaskInfo loadTask_;
        private bool delayFitSize_;

        private bool dragging_;
        private Point draggingStart_;
        private Point draggingViewStart_;
        private ToolTip nodeToolTip_;
        private GraphNode hoveredNode_;

        private bool ignoreNextHover_;
        private bool restoredState_;
        private bool delayRestoreState_;
        private bool optionsPanelVisible_;

        private GraphQueryInfo queryInfo_;
        private bool queryPanelVisible_;

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

            OptionsPanel.PanelClosed += OptionsPanel_PanelClosed;
            OptionsPanel.PanelReset += OptionsPanel_PanelReset;

            SetupCommands();
        }

        private void OptionsPanel_PanelReset(object sender, EventArgs e) {
            var newOptions = new FlowGraphSettings();
            newOptions.Reset();
            OptionsPanel.DataContext = newOptions;
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

        public IRElement Element {
            get { return GraphViewer.SelectedElement; }
            set {
                GraphViewer.SelectElement(value);

                if (!HasPinnedContent && Settings.BringNodesIntoView) {
                    BringIntoView(value);
                }
            }
        }

        public void BringIntoView(IRElement element) {
            var node = GraphViewer.FindElementNode(element);

            if (node == null) {
                return;
            }
            var position = GraphViewer.GetNodePosition(node);
            var offsetX = GraphHost.HorizontalOffset;
            var offsetY = GraphHost.VerticalOffset;

            if (position.X < offsetX ||
                position.X > offsetX + GraphHost.ActualWidth) {
                GraphHost.ScrollToHorizontalOffset(Math.Max(0, position.X - HorizontalViewMargin));
            }

            if (position.Y < offsetY ||
                position.Y > offsetY + GraphHost.ActualHeight) {
                GraphHost.ScrollToVerticalOffset(Math.Max(0, position.Y - VerticalViewMargin));
            }
        }

        public void DisplayGraph(LayoutGraph graph) {
            graph_ = graph;
            GraphViewer.ShowGraph(graph);
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

            if (viewBounds.Width == 0 || viewBounds.Height == 0) {
                // Panel is not visible, set the graph size when it becomes visible.
                delayFitSize_ = true;
            }
            else if (!restoredState_) {
                GraphViewer.FitWidthToSize(viewBounds);
                delayFitSize_ = false;
            }
        }

        Size GetGraphBounds() {
            return new Size(Math.Max(0, RenderSize.Width - SystemParameters.VerticalScrollBarWidth),
                            RenderSize.Height);
        }

        public void Highlight(IRHighlightingEventArgs info) {
            if (!Settings.SyncMarkedNodes &&
                info.Type == HighlighingType.Marked) {
                return;
            }

            GraphViewer.Highlight(info);
        }

        public void InitializeFromDocument(IRDocument doc) {
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

            DoubleAnimation animation = new DoubleAnimation(0.25, TimeSpan.FromSeconds(0.5));
            animation.BeginTime = TimeSpan.FromSeconds(1);
            GraphViewer.BeginAnimation(GraphViewer.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);

            LongOperationView.Opacity = 0.0;
            LongOperationView.Visibility = Visibility.Visible;
            DoubleAnimation animation2 = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.5));
            animation2.BeginTime = TimeSpan.FromSeconds(2);
            LongOperationView.BeginAnimation(Grid.OpacityProperty, animation2, HandoffBehavior.SnapshotAndReplace);

            return CreateGraphLoadTask();
        }

        public void OnGenerateGraphDone(CancelableTaskInfo task, bool failed = false) {
            if (!failed) {
                Utils.EnableControl(GraphViewer);
                GraphHost.ScrollToHorizontalOffset(0);
                GraphHost.ScrollToVerticalOffset(0);

                DoubleAnimation animation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.2));
                GraphViewer.BeginAnimation(GraphViewer.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }

            DoubleAnimation animation2 = new DoubleAnimation(0, TimeSpan.FromSeconds(0.2));
            animation2.Completed += Animation2_Completed;
            LongOperationView.BeginAnimation(Grid.OpacityProperty, animation2, HandoffBehavior.SnapshotAndReplace);

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

        void AdjustZoom(double value) {
            if (Utils.IsShiftModifierActive()) {
                value *= FastZoomFactor;
            }

            SetZoom(GraphViewer.ZoomLevel + value);
        }

        private void Animation2_Completed(object sender, EventArgs e) {
            LongOperationView.Visibility = Visibility.Collapsed;
        }

        private Size AvailableGraphSize() {
            return new Size(GraphHost.RenderSize.Width -
                            SystemParameters.VerticalScrollBarWidth -
                            SystemParameters.BorderWidth * 2,
                            GraphHost.RenderSize.Height);
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
            if (Utils.IsKeyboardModifierActive()) {
                return FastPanOffset;
            }

            return PanOffset;
        }

        private HighlightingStyle GetSelectedColorStyle(ExecutedRoutedEventArgs e) {
            return Utils.GetSelectedColorStyle(e.Parameter as ColorEventArgs,
                                               GraphViewer.DefaultBoldPen);
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
            Point point = e.GetPosition(GraphHost);
            Focus();

            if (point.X < 0 || point.Y < 0 ||
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
                Point offset = draggingViewStart_ - (e.GetPosition(GraphHost) - draggingStart_);
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

            var zoom = GraphViewer.ZoomLevel;
            zoom = zoom * (1 + e.Delta / 1000.0);
            SetZoom(zoom);
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

            if (Settings.ShowPreviewPopupWithModifier &&
                !Utils.IsShiftModifierActive()) {
                return;
            }

            if (ignoreNextHover_) {
                // Don't show the block preview if the user jumped to it.
                ignoreNextHover_ = false;
                return;
            }

            var node = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

            if (node != null &&
                node.NodeInfo.Block != null) {
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

        private void SelectQueryBlock1Executed(object sender, ExecutedRoutedEventArgs e) {
            if (hoveredNode_ != null) {
                SetQueryBlock1(hoveredNode_.NodeInfo.Block);
            }
        }

        private void SelectQueryBlock2Executed(object sender, ExecutedRoutedEventArgs e) {
            if (hoveredNode_ != null) {
                SetQueryBlock2(hoveredNode_.NodeInfo.Block);
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
                var pathBlocks = (await cache.GetReachabilityAsync()).FindPath(queryInfo_.Block1, queryInfo_.Block2);

                foreach (var block in pathBlocks) {
                    GraphViewer.Mark(block, Colors.Gold);
                }
            }
        }

        private void NodeToolTip__Loaded(object sender, RoutedEventArgs e) {
            var node = nodeToolTip_.Tag as GraphNode;
            var previewer = Utils.FindChild<IRPreview>(nodeToolTip_, "IRPreviewer");
            previewer.InitializeFromDocument(document_);
            var block = node.NodeInfo.Block;
            previewer.PreviewedElement = block;
            nodeToolTip_.DataContext = new BlockTooltipInfo(block);

            int lines = Math.Max(1, Math.Min(block.Tuples.Count + 1, 20));
            nodeToolTip_.Height = previewer.ResizeForLines(lines);
            previewer.UpdateView(false);
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

        async void SetQueryBlock1(BlockIR block) {
            ShowQueryPanel();

            if (queryInfo_.Block1 != block) {
                queryInfo_.Block1 = block;
                queryInfo_.Block1Name = Utils.MakeBlockDescription(block);
                await UpdateQueryResult();
            }
        }

        async void SetQueryBlock2(BlockIR block) {
            ShowQueryPanel();

            if (queryInfo_.Block2 != block) {
                queryInfo_.Block2 = block;
                queryInfo_.Block2Name = Utils.MakeBlockDescription(block);
                await UpdateQueryResult();
            }
        }


        private async Task UpdateQueryResult() {
            QueryPanel.DataContext = null;

            if (queryInfo_.Block1 != null &&
                queryInfo_.Block2 != null) {
                var cache = FunctionAnalysisCache.Get(document_.Function);
                await cache.CacheAllAsync();
                queryInfo_.Dominates = (await cache.GetDominatorsAsync()).Dominates(queryInfo_.Block1, queryInfo_.Block2);
                queryInfo_.PostDominates = (await cache.GetPostDominatorsAsync()).Dominates(queryInfo_.Block1, queryInfo_.Block2);
                queryInfo_.Reaches = (await cache.GetReachabilityAsync()).Reaches(queryInfo_.Block1, queryInfo_.Block2);
            }

            QueryPanel.DataContext = queryInfo_;
        }

        void ShowQueryPanel() {
            if (queryPanelVisible_) {
                return;
            }

            queryInfo_ = new GraphQueryInfo();
            QueryPanel.DataContext = queryInfo_;
            QueryPanel.Visibility = Visibility.Visible;
            queryPanelVisible_ = true;
        }

        void HideQueryPanel() {
            if (!queryPanelVisible_) {
                return;
            }

            QueryPanel.DataContext = null;
            QueryPanel.Visibility = Visibility.Collapsed;
            queryPanelVisible_ = false;
        }

        void ShowOptionsPanel() {
            OptionsPanel.DataContext = Settings.Clone();
            OptionsPanel.Visibility = Visibility.Visible;
            OptionsPanel.Focus();
            optionsPanelVisible_ = true;
        }

        void CloseOptionsPanel() {
            if (!optionsPanelVisible_) {
                return;
            }

            OptionsPanel.Visibility = Visibility.Collapsed;
            var newSettings = (FlowGraphSettings)OptionsPanel.DataContext;
            OptionsPanel.DataContext = null;
            optionsPanelVisible_ = false;

            if (newSettings.HasChanges(Settings)) {
                Settings = newSettings;
                App.Settings.FlowGraphSettings = newSettings;
                App.SaveApplicationSettings();
            }
        }

        private void SetupCommands() {
            AddCommand(GraphCommand.MarkBlock, MarkBlockExecuted);
            AddCommand(GraphCommand.MarkPredecessors, MarkPredecessorsExecuted);
            AddCommand(GraphCommand.MarkSuccessors, MarkSuccessorsExecuted);
            AddCommand(GraphCommand.MarkPredecessors, MarkPredecessorsExecuted);
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

        void SetZoom(double value) {
            double currentZoom = GraphViewer.ZoomLevel;
            double centerX = GraphHost.ViewportWidth / 2;
            double centerY = GraphHost.ViewportHeight / 2;
            double offsetX = (GraphHost.HorizontalOffset + centerX) / currentZoom;
            double offsetY = (GraphHost.VerticalOffset + centerY) / currentZoom;

            var zoom = value;
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
                else HideTooltip();
            }

            if (node == null) {
                return;
            }

            nodeToolTip_ = new ToolTip();
            nodeToolTip_.Style = Application.Current.FindResource("BlockIRPreviewTooltip") as Style;
            nodeToolTip_.Width = 600;
            nodeToolTip_.Height = 100;
            nodeToolTip_.Tag = node;
            nodeToolTip_.Loaded += NodeToolTip__Loaded;
            nodeToolTip_.IsOpen = true;

        }
        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        internal void RemoveAllHighlighting(HighlighingType type) {
            GraphViewer.ResetHighlightedNodes(type);
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
        public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection |
                                                          HandledEventKind.ElementHighlighting;

        public FlowGraphSettings Settings {
            get => settings_;
            set {
                settings_ = value;
                ReloadSettings();
            }
        }

        public override void OnRegisterPanel() {
            IsPanelEnabled = false;
            Settings = App.Settings.FlowGraphSettings;
        }

        void ReloadSettings() {
            GraphViewer.Settings = Settings;
            GraphHost.Background = ColorBrushes.GetBrush(Settings.BackgroundColor);
        }

        public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            InitializeFromDocument(document);

            if (document.DuringSectionLoading) {
                Trace.TraceInformation($"Graph panel {ObjectTracker.Track(this)}: Ignore graph reload during section switch");
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

            var data = StateSerializer.Serialize<GraphPanelState>(state, document.Function);
            Session.SavePanelState(data, this, section);
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
            var graphReader = new Core.GraphViz.GraphvizReader(sourceGraphPanel.graph_.GraphKind,
                                                               sourceGraphPanel.graph_.SourceText,
                                                               sourceGraphPanel.graph_.BlockNameMap);
            var newGraph = graphReader.ReadGraph();
            DisplayGraph(newGraph);
        }
        #endregion

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            if (loadTask_ != null) {
                loadTask_.Cancel();
            }
        }

        private void QueryPanel_MouseEnter(object sender, MouseEventArgs e) {
            DoubleAnimation animation = new DoubleAnimation(1, TimeSpan.FromSeconds(0.1));
            QueryPanel.BeginAnimation(Grid.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private void QueryPanel_MouseLeave(object sender, MouseEventArgs e) {
            DoubleAnimation animation = new DoubleAnimation(0.5, TimeSpan.FromSeconds(0.3));
            QueryPanel.BeginAnimation(Grid.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
