// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Core.GraphViz;
using Core.IR;

namespace Client {
    /// <summary>
    /// Interaction logic for GraphViewer.xaml
    /// </summary>
    public partial class GraphViewer : FrameworkElement {
        public static Pen DefaultPen = Pens.GetPen(Colors.Black, 0.025);
        public static Pen DefaultBoldPen = Pens.GetPen(Colors.Black, 0.05);

        private FlowGraphSettings settings_;
        private LayoutGraph graph_;
        private DrawingVisual graphVisual_;
        private GraphRenderer graphRenderer_;

        private HighlightingStyle defaultNodeStyle_ =
            new HighlightingStyle(Brushes.Silver, DefaultPen);

        private HighlightingStyle selectedNodeStyle =
           new HighlightingStyle(Color.FromRgb(174, 220, 244), DefaultBoldPen);

        private Pen predecessorNodeBorder_ = Pens.GetPen("#6927CC", 0.05);
        private Pen successorNodeBorder_ = Pens.GetPen("#008230", 0.05);

        private HighlightingStyleCyclingCollection nodeStyles_;
        private Dictionary<GraphNode, HighlightingStyle> markedNodes_;
        private Dictionary<GraphNode, HighlightingStyle> hoverNodes_;
        private Dictionary<GraphNode, HighlightingStyle> selectedNodes_;

        public LayoutGraph Graph => graph_;
        public bool IsGraphLoaded => graphVisual_ != null;

        void ReloadOptions() {
            defaultNodeStyle_ = new HighlightingStyle(settings_.NodeColor, DefaultPen);

            if (graph_ != null) {
                ReloadGraph(graph_);
            }
        }

        public GraphViewer() {
            InitializeComponent();
            VerticalAlignment = VerticalAlignment.Top;
            HorizontalAlignment = HorizontalAlignment.Center;
            MaxZoomLevel = double.PositiveInfinity;

            markedNodes_ = new Dictionary<GraphNode, HighlightingStyle>();
            hoverNodes_ = new Dictionary<GraphNode, HighlightingStyle>();
            selectedNodes_ = new Dictionary<GraphNode, HighlightingStyle>();

            var stylesWithBorder = HighlightingStyles.GetStyleSetWithBorder(HighlightingStyles.StyleSet, DefaultBoldPen);
            nodeStyles_ = new HighlightingStyleCyclingCollection(stylesWithBorder);

            SetupEvents();
        }

        private void SetupEvents() {
            MouseLeftButtonDown += GraphViewer_MouseLeftButtonDown;
            MouseRightButtonDown += GraphViewer_MouseRightButtonDown;
            MouseMove += GraphViewer_MouseMove;
        }

        private HighlightingStyle PickMarkerStyle() {
            return nodeStyles_.GetNext();
        }

        public GraphNode GetSelectedNode() {
            if (selectedNodes_.Count > 0) {
                var selectedEnum = selectedNodes_.GetEnumerator();
                selectedEnum.MoveNext();
                return selectedEnum.Current.Key;
            }

            return null;
        }

        private HighlightingStyle GetNodeStyle(HighlightingStyle style) {
            if (style != null) {
                return style;
            }

            return PickMarkerStyle();
        }

        private void GraphViewer_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            var point = e.GetPosition(this);
            var graphNode = FindPointedNode(point);

            if (graphNode != null) {
                SelectElement(graphNode.NodeInfo.Block);
            }

            e.Handled = graphNode != null;
        }

        private void GraphViewer_MouseMove(object sender, MouseEventArgs e) {
            var point = e.GetPosition(this);
            var graphNode = FindPointedNode(point);

            if (graphNode != null) {
                if (hoveredNode_ != graphNode) {
                    HighlightConnectedNodes(graphNode, hoverNodes_, settings_.HighlightConnectedNodesOnHover);
                    hoveredNode_ = graphNode;
                }
            }
            else {
                ResetHighlightedNodes(HighlighingType.Hovered);
            }
        }

        HighlightingStyle ApplyBorderToStyle(HighlightingStyle style, Pen border) {
            return new HighlightingStyle(style.BackColor, border);
        }

        private void HighlightConnectedNodes(GraphNode graphNode,
                                             Dictionary<GraphNode, HighlightingStyle> group,
                                             bool highlightConnectedNodes) {
            ResetHighlightedNodes(HighlighingType.Hovered);
            HighlightNode(graphNode, selectedNodeStyle, group);

            if (!highlightConnectedNodes) {
                return;
            }

            if (graphNode.NodeInfo.InEdges != null) {
                foreach (var edge in graphNode.NodeInfo.InEdges) {
                    var node = edge.NodeFrom?.Tag as GraphNode;
                    if (node == null) continue; // Part of graph is invalid, ignore.
                    HighlightNode(node, ApplyBorderToStyle(node.Style, predecessorNodeBorder_), group);
                }
            }

            if (graphNode.NodeInfo.OutEdges != null) {
                foreach (var edge in graphNode.NodeInfo.OutEdges) {
                    var node = edge.NodeTo?.Tag as GraphNode;
                    if (node == null) continue; // Part of graph is invalid, ignore.
                    HighlightNode(node, ApplyBorderToStyle(node.Style, successorNodeBorder_), group);
                }
            }
        }

        public event EventHandler<IRElementEventArgs> BlockSelected;
        public event EventHandler<IRElementMarkedEventArgs> BlockMarked;
        public event EventHandler<IRElementMarkedEventArgs> BlockUnmarked;
        public event EventHandler GraphLoaded;

        private void GraphViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            var point = e.GetPosition(this);
            var graphNode = FindPointedNode(point);

            if (graphNode != null) {
                if (graphNode.NodeInfo.Block == null) {
                    return;
                }

                SelectElement(graphNode.NodeInfo.Block);
                e.Handled = true;

                BlockSelected?.Invoke(this, new IRElementEventArgs() {
                    Element = graphNode.NodeInfo.Block
                });
            }
        }

        public void MarkSelectedNode(HighlightingStyle style) {
            MarkNode(GetSelectedNode(), GetNodeStyle(style));
        }

        public void MarkSelectedNodePredecessors(HighlightingStyle style) {
            MarkNodePredecessors(GetSelectedNode(), GetNodeStyle(style));
        }

        public void MarkSelectedNodeSuccessors(HighlightingStyle style) {
            MarkNodeSuccessors(GetSelectedNode(), GetNodeStyle(style));
        }

        public void MarkSelectedNodeLoop(HighlightingStyle style) {
            MarkNodeLoop(GetSelectedNode(), GetNodeStyle(style));
        }

        public void MarkSelectedNodeLoopNest(HighlightingStyle style) {
            MarkNodeLoopNest(GetSelectedNode(), GetNodeStyle(style));
        }

        public void Mark(BlockIR block, Color selectedColor, bool useBoldBorder = true) {
            var node = (GraphNode)graph_.BlockNodeMap[block].Tag;
            MarkNode(node, selectedColor);
        }

        public void MarkNode(GraphNode node, Color selectedColor, bool useBoldBorder = true) {
            var pen = useBoldBorder ? DefaultBoldPen : DefaultPen;
            MarkNode(node, new HighlightingStyle(selectedColor, pen));
        }

        public void MarkNode(GraphNode node, HighlightingStyle style) {
            if (node == null) {
                return;
            }

            HighlightNode(node, style, HighlighingType.Marked);

            BlockMarked?.Invoke(this, new IRElementMarkedEventArgs() {
                Element = node.NodeInfo.Block,
                Style = style
            });
        }

        public void MarkNodePredecessors(GraphNode node, HighlightingStyle style) {
            if (node == null) {
                return;
            }

            if (node.NodeInfo.InEdges != null) {
                foreach (var edge in node.NodeInfo.InEdges) {
                    var fromNode = edge.NodeFrom?.Tag as GraphNode;
                    MarkNode(fromNode, style);
                }
            }
        }

        public void MarkNodeSuccessors(GraphNode node, HighlightingStyle style) {
            if (node == null) {
                return;
            }

            if (node.NodeInfo.InEdges != null) {
                foreach (var edge in node.NodeInfo.OutEdges) {
                    var toNode = edge.NodeTo?.Tag as GraphNode;
                    MarkNode(toNode, style);
                }
            }
        }

        public void MarkNodeLoop(GraphNode node, HighlightingStyle style) {
            if (node == null) {
                return;
            }

            var loopTag = node.NodeInfo.Block.GetTag<LoopBlockTag>();

            if (loopTag == null) {
                return;
            }

            foreach (var block in loopTag.Loop.Blocks) {
                MarkNode(GetBlockNode(block), style);
            }
        }

        public void MarkNodeLoopNest(GraphNode node, HighlightingStyle style) {
            if (node == null) {
                return;
            }

            var loopTag = node.NodeInfo.Block.GetTag<LoopBlockTag>();

            if (loopTag == null) {
                return;
            }

            foreach (var block in loopTag.Loop.LoopNestRoot.Blocks) {
                MarkNode(GetBlockNode(block), style);
            }
        }

        private void HighlightNode(GraphNode node, HighlightingStyle style, HighlighingType type) {
            if (node == null) {
                return;
            }

            var group = GetHighlightedNodeGroup(HighlighingType.Marked);
            HighlightNode(node, style, group);
        }

        private void HighlightNode(GraphNode node, HighlightingStyle style,
                                   Dictionary<GraphNode, HighlightingStyle> group) {
            if (node == null) {
                return;
            }

            SetNodeStyle(node, style);
            group[node] = style;
        }

        protected override void OnMouseLeave(MouseEventArgs e) {
            base.OnMouseLeave(e);

            // Workaround to prevent flickerying when the parent host
            // handles commands with keys being kept in a pressed state.
            if (!Utils.IsKeyboardModifierActive()) {
                ResetHighlightedNodes(HighlighingType.Hovered);
            }
        }

        public GraphNode FindPointedNode(Point point) {
            var result = VisualTreeHelper.HitTest(this, point);

            if (result == null) {
                return null;
            }

            var visual = result.VisualHit as DrawingVisual;

            if (visual != null) {
                return visual.ReadLocalValue(FrameworkElement.TagProperty) as GraphNode;
            }

            return null;
        }

        public GraphNode FindElementNode(IRElement element) {
            var block = element.ParentBlock;

            if (block == null) {
                return null;
            }

            return GetBlockNode(block);
        }

        private GraphNode GetBlockNode(BlockIR block) {
            if (graph_.BlockNodeMap.TryGetValue(block, out Node node)) {
                return node.Tag as GraphNode;
            }

            return null;
        }

        private double TransformPoint(double value) {
            return value * zoomLevel_ * ScaleFactor;
        }

        public Point GetNodePosition(GraphNode node) {
            double x = node.NodeInfo.CenterX - node.NodeInfo.Width / 2;
            double y = node.NodeInfo.CenterY - node.NodeInfo.Height / 2;
            return new Point(TransformPoint(x), TransformPoint(y));
        }

        private IRElement element_;
        public IRElement SelectedElement => element_;

        public void SelectElement(IRElement element) {
            if (element_ == element) {
                return;
            }

            element_ = element;
            ResetHighlightedNodes(HighlighingType.Selected);
            ResetHighlightedNodes(HighlighingType.Hovered);
            var block = element_?.ParentBlock;

            if (block != null) {
                if (graph_.BlockNodeMap.TryGetValue(block, out Node node)) {
                    var graphNode = node.Tag as GraphNode;
                    HighlightConnectedNodes(graphNode, selectedNodes_,
                                            settings_.HighlightConnectedNodesOnSelection);
                }
            }
        }

        public void Highlight(IRHighlightingEventArgs info) {
            if (graph_ == null) {
                Debug.Assert(false, "This should not happen, events are not connected");
                return;
            }

            var group = GetHighlightedNodeGroup(info.Type);

            if (info.Type != HighlighingType.Marked &&
                info.Action != HighlightingEventAction.AppendHighlighting) {
                ResetHighlightedNodes(group);
            }

            // Reset any hovered items when selecting or marking.
            if (info.Type != HighlighingType.Hovered) {
                ResetHighlightedNodes(HighlighingType.Hovered);
            }

            if (info.Element == null ||
                info.Group == null) {
                return;
            }

            foreach (var element in info.Group.Elements) {
                var block = element.ParentBlock;

                if (block != null && graph_.BlockNodeMap.ContainsKey(block)) {
                    var node = graph_.BlockNodeMap[block];
                    var graphNode = node.Tag as GraphNode;

                    // If it's a block not being marked, highlight
                    // the entire group of the block and its pred/succ. blocks.
                    if (element is BlockIR &&
                        info.Type != HighlighingType.Marked) {
                        HighlightConnectedNodes(graphNode, group, settings_.HighlightConnectedNodesOnSelection);
                        continue;
                    }

                    // For a marker, allow it to replace the node style.
                    if (info.Type == HighlighingType.Marked ||
                        !group.ContainsKey(graphNode)) {
                        Pen border = DefaultPen;

                        if (element == info.Element) {
                            border = DefaultBoldPen;
                        }

                        var style = new HighlightingStyle(info.Group.Style.BackColor, border);
                        HighlightNode(graphNode, style, group);
                    }
                }
            }
        }

        private Dictionary<GraphNode, HighlightingStyle> GetHighlightedNodeGroup(HighlighingType type) {
            switch (type) {
                case HighlighingType.Hovered: {
                    return hoverNodes_;
                }
                case HighlighingType.Selected: {
                    return selectedNodes_;
                }
                case HighlighingType.Marked: {
                    return markedNodes_;
                }
            }

            throw new InvalidOperationException("Unsupported highlighting type");
        }

        public void ResetHighlightedNodes(HighlighingType type) {
            var group = GetHighlightedNodeGroup(type);
            ResetHighlightedNodes(group);

            if(type == HighlighingType.Hovered) {
                hoveredNode_ = null;
            }
        }

        private void ResetHighlightedNodes(Dictionary<GraphNode, HighlightingStyle> group) {
            var tempNodes = new List<GraphNode>(group.Keys);
            group.Clear();

            foreach (var node in tempNodes) {
                RestoreNodeStyle(node);
            }
        }

        public void ResetSelectedNode() {
            ResetMarkedNode(GetSelectedNode());
        }

        public void ResetMarkedNode(GraphNode node) {
            if (node == null) {
                return;
            }

            if (markedNodes_.ContainsKey(node)) {
                markedNodes_.Remove(node);
                RestoreNodeStyle(node);

                BlockUnmarked?.Invoke(this, new IRElementMarkedEventArgs() {
                    Element = node.NodeInfo.Block
                });
            }
        }

        public void ResetAllMarkedNodes() {
            if (BlockUnmarked != null) {
                foreach (var node in markedNodes_.Keys) {
                    BlockUnmarked(this, new IRElementMarkedEventArgs() {
                        Element = node.NodeInfo.Block
                    });
                }
            }

            ResetHighlightedNodes(markedNodes_);
        }

        private void RestoreNodeStyle(GraphNode node) {
            HighlightingStyle style;

            if (!hoverNodes_.TryGetValue(node, out style) &&
                !selectedNodes_.TryGetValue(node, out style) &&
                !markedNodes_.TryGetValue(node, out style)) {
                style = graphRenderer_.GetDefaultNodeStyle(node);
            }

            node.Style = style;
            node.Draw();
        }

        private void SetNodeStyle(GraphNode node, HighlightingStyle style) {
            node.Style = style;
            node.Draw();
        }

        protected override int VisualChildrenCount {
            get { return 1; }
        }

        protected override Visual GetVisualChild(int index) {
            return graphVisual_;
        }

        public void ShowGraph(LayoutGraph graph) {
            ReloadGraph(graph);
            GraphLoaded?.Invoke(this, new EventArgs());
        }

        private void ReloadGraph(LayoutGraph graph) {
            HideGraph();
            graph_ = graph;

            graphRenderer_ = new GraphRenderer(graph_, settings_);
            graphVisual_ = graphRenderer_.Render();
            AddVisualChild(graphVisual_);
            InvalidateMeasure();
        }

        public void HideGraph() {
            if (graphVisual_ != null) {
                RemoveVisualChild(graphVisual_);
                graphVisual_ = null;
                graphRenderer_ = null;
                graph_ = null;

                markedNodes_.Clear();
                hoverNodes_.Clear();
                selectedNodes_.Clear();
            }
        }

        public void FitWidthToSize(Size size) {
            Rect bounds = graphVisual_.ContentBounds;
            bounds.Union(graphVisual_.DescendantBounds);
            var requiredSpace = bounds.Width + 2 * GraphMargin;
            var zoom = (0.95 * size.Width) / (requiredSpace * ScaleFactor);
            ZoomLevel = Math.Min(zoom, 1.0);
        }

        public void FitToSize(Size size) {
            Rect bounds = graphVisual_.ContentBounds;
            bounds.Union(graphVisual_.DescendantBounds);
            var requiredWidth = bounds.Width + 2 * GraphMargin;
            var requiredHeight = bounds.Height + 2 * GraphMargin;
            var widthFraction = size.Width / requiredWidth;
            var heightFraction = size.Height / requiredHeight;

            if (widthFraction < heightFraction) {
                ZoomLevel = 0.95 * size.Width / (requiredWidth * ScaleFactor);
            }
            else {
                ZoomLevel = 0.95 * size.Height / (requiredHeight * ScaleFactor);
            }
        }

        private readonly double ScaleFactor = 50;
        private double zoomLevel_ = 0.5;
        private GraphNode hoveredNode_;
        private readonly double GraphMargin = 0.15;

        public double ZoomLevel {
            get { return zoomLevel_; }
            set {
                zoomLevel_ = Math.Min(value, MaxZoomLevel);
                InvalidateMeasure();
            }
        }

        public double MaxZoomLevel { get; set; }
        public GraphPanel HostPanel { get; set; }

        public FlowGraphSettings Settings {
            get => settings_;
            set {
                settings_ = value;
                ReloadOptions();
            }
        }

        protected override Size MeasureOverride(Size availableSize) {
            if (graphVisual_ == null) {
                return new Size(0, 0);
            }

            Rect bounds = graphVisual_.ContentBounds;
            bounds.Union(graphVisual_.DescendantBounds);
            if (bounds.IsEmpty) return new Size(0, 0);

            Matrix m = new Matrix();
            m.Translate(-bounds.Left + GraphMargin, -bounds.Top + GraphMargin);

            m.Scale(zoomLevel_ * ScaleFactor, zoomLevel_ * ScaleFactor);
            graphVisual_.Transform = new MatrixTransform(m);

            return new Size(TransformPoint(bounds.Width + 2 * GraphMargin),
                            TransformPoint(bounds.Height + 2 * GraphMargin));
        }
    }


}
