// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Core.Graph;
using Core.GraphViz;
using Core.IR;

namespace Client {
    public sealed class GraphNode {
        public Node NodeInfo { get; set; }
        public DrawingVisual Visual { get; set; }
        public HighlightingStyle Style { get; set; }
        public Typeface TextFont { get; set; }
        public Brush TextColor { get; set; }

        public void Draw() {
            using (var dc = Visual.RenderOpen()) {
                dc.DrawRectangle(Style.BackColor,
                                 Style.Border,
                    new Rect(NodeInfo.CenterX - NodeInfo.Width / 2,
                             NodeInfo.CenterY - NodeInfo.Height / 2,
                             NodeInfo.Width, NodeInfo.Height));

                var text = new FormattedText(NodeInfo.Label.ToString(),
                                             CultureInfo.InvariantCulture,
                                             FlowDirection.LeftToRight,
                                             TextFont, 0.25, TextColor,
                                             VisualTreeHelper.GetDpi(Visual).PixelsPerDip);

                dc.DrawText(text, new Point(NodeInfo.CenterX - text.Width / 2,
                                            NodeInfo.CenterY - text.Height / 2));
            }
        }
    }

    public sealed class GraphRenderer {
        private const double DefaultEdgeThickness = 0.025;
        private const double BoldEdgeThickness = 0.05;
        private const double DashedEdgeThickness = 0.035;
        private const int PolylineEdgeThreshold = 100;

        private FlowGraphSettings options_;
        private LayoutGraph graph_;
        private DrawingVisual visual_;
        private Typeface defaultNodeFont_;
        private Typeface edgeFont_;

        private Brush defaultTextColor_;
        private Brush defaultNodeBackground_;
        private HighlightingStyle defaultBlockStyle_;
        private HighlightingStyle emptyBlockStyle_;
        private HighlightingStyle branchBlockStyle_;
        private HighlightingStyle switchBlockStyle_;
        private HighlightingStyle loopBackedgeBlockStyle_;
        private HighlightingStyle returnBlockStyle_;
        private List<HighlightingStyle> loopBlockStyles_;

        public HighlightingStyle DefaultNodeStyle { get; set; }

        public GraphRenderer(LayoutGraph graph, FlowGraphSettings options) {
            options_ = options;
            graph_ = graph;
            edgeFont_ = new Typeface("Verdana");
            defaultNodeFont_ = new Typeface("Verdana");

            defaultTextColor_ = ColorBrushes.GetBrush(options.TextColor);
            defaultNodeBackground_ = ColorBrushes.GetBrush(options.NodeColor);
            DefaultNodeStyle = new HighlightingStyle(defaultNodeBackground_,
                                                     Pens.GetPen(options.NodeBorderColor, DefaultEdgeThickness)); ;
            defaultBlockStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.NodeBorderColor, DefaultEdgeThickness));
            branchBlockStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.BranchNodeBorderColor, 0.035));
            switchBlockStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.SwitchNodeBorderColor, 0.035));
            loopBackedgeBlockStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.LoopNodeBorderColor, BoldEdgeThickness));
            returnBlockStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.ReturnNodeBorderColor, BoldEdgeThickness));
            emptyBlockStyle_ = new HighlightingStyle(Colors.Gainsboro, Pens.GetPen(options.NodeBorderColor, DefaultEdgeThickness));

            if (options.MarkLoopBlocks) {
                loopBlockStyles_ = new List<HighlightingStyle>();

                foreach (var color in options.LoopNodeColors) {
                    loopBlockStyles_.Add(new HighlightingStyle(color, Pens.GetPen(options.NodeBorderColor, DefaultEdgeThickness)));
                }
            }
        }

        public DrawingVisual Render() {
            visual_ = new DrawingVisual();
            DrawNodes();
            DrawEdges();

            // Can be null if the CFG is not available.
            if (visual_.Drawing != null) {
                visual_.Drawing.Freeze();
            }

            return visual_;
        }

        public HighlightingStyle GetDefaultBlockStyle(BlockIR block) {
            if (block == null) {
                return defaultBlockStyle_;
            }

            var loopTag = block.GetTag<LoopBlockTag>();

            if (loopTag != null && options_.MarkLoopBlocks) {
                if (loopTag.NestingLevel < loopBlockStyles_.Count - 1) {
                    return loopBlockStyles_[loopTag.NestingLevel];
                }
                else return loopBlockStyles_[loopBlockStyles_.Count - 1];
            }

            if (options_.ColorizeNodes) {
                if (block.HasLoopBackedge) return loopBackedgeBlockStyle_;
                else if (block.IsBranchBlock) return branchBlockStyle_;
                else if (block.IsSwitchBlock) return switchBlockStyle_;
                else if (block.IsReturnBlock) return returnBlockStyle_;
                else if (block.IsEmpty) return emptyBlockStyle_;
            }

            return defaultBlockStyle_;
        }

        public HighlightingStyle GetDefaultNodeStyle() {
            return defaultBlockStyle_;
        }

        public HighlightingStyle GetDefaultNodeStyle(GraphNode node) {
            return GetDefaultBlockStyle(node.NodeInfo.Block);
        }

        private void DrawNodes() {
            foreach (var node in graph_.Nodes) {
                var nodeVisual = new DrawingVisual();
                var graphNode = new GraphNode() {
                    NodeInfo = node,
                    Visual = nodeVisual,
                    TextFont = defaultNodeFont_,
                    TextColor = defaultTextColor_,
                    Style = GetDefaultBlockStyle(node.Block)
                };

                graphNode.Draw();
                node.Tag = graphNode;
                nodeVisual.SetValue(FrameworkElement.TagProperty, graphNode);
                visual_.Children.Add(nodeVisual);
            }
        }

        private Point ToPoint(Tuple<double, double> value) {
            return new Point(value.Item1, value.Item2);
        }

        enum EdgeKind {
            Default,
            Loop,
            Branch,
            Return,
            ImmediateDominator,
            ImmediatePostDominator
        }

        EdgeKind GetEdgeType(Edge edge) {
            if (!options_.ColorizeEdges) {
                return EdgeKind.Default;
            }

            if (edge.Style == Edge.EdgeKind.Dotted) {
                return EdgeKind.ImmediateDominator;
            }

            if (graph_.GraphKind != GraphKind.FlowGraph) {
                return EdgeKind.Default;
            }

            var fromBlock = edge.NodeFrom?.Block;
            var toBlock = edge.NodeTo?.Block;

            if (fromBlock != null && toBlock != null) {
                if (toBlock.Number <= fromBlock.Number) {
                    return EdgeKind.Loop;
                }
                else if (toBlock.IsReturnBlock) {
                    return EdgeKind.Return;
                }
                else if (fromBlock.Successors.Count == 2) {
                    var targetBlock = fromBlock.BranchTargetBlock;

                    if (targetBlock == toBlock) {
                        return EdgeKind.Branch;
                    }
                }
            }

            return EdgeKind.Default;
        }

        private void DrawEdges() {
            var pen = Pens.GetPen(Brushes.Black, DefaultEdgeThickness);
            var loopPen = Pens.GetPen(loopBackedgeBlockStyle_.Border.Brush, BoldEdgeThickness);
            var branchPen = Pens.GetPen(branchBlockStyle_.Border.Brush, DefaultEdgeThickness);
            var returnPen = Pens.GetPen(returnBlockStyle_.Border.Brush, DefaultEdgeThickness);
            var immDomPen = Pens.GetDashedPen(Colors.Blue, DashStyles.Dot, DashedEdgeThickness);
            var dc = visual_.RenderOpen();

            var defaultEdgeGeometry = new StreamGeometry();
            var loopEdgeGeometry = new StreamGeometry();
            var branchEdgeGeometry = new StreamGeometry();
            var returnEdgeGeometry = new StreamGeometry();
            var immDomEdgeGeometry = new StreamGeometry();

            var defaultSC = defaultEdgeGeometry.Open();
            var loopSC = loopEdgeGeometry.Open();
            var branchSC = branchEdgeGeometry.Open();
            var returnSC = returnEdgeGeometry.Open();
            var immDomSC = immDomEdgeGeometry.Open();

            // If there are many in-edges, to avoid terrible performance due to WPF edge drawing
            // use polylines instead. Performance is still not good, but the graph becomes usable.
            bool usePolyLine = graph_.Nodes.Find((node) => (node.InEdges != null) && 
                                                           (node.InEdges.Count > PolylineEdgeThreshold)) != null;
            foreach (var edge in graph_.Edges) {
                var points = edge.LinePoints;
                var edgeType = GetEdgeType(edge);
                var sc = edgeType switch {
                    EdgeKind.Default => defaultSC,
                    EdgeKind.Branch => branchSC,
                    EdgeKind.Loop => loopSC,
                    EdgeKind.Return => returnSC,
                    EdgeKind.ImmediateDominator => immDomSC
                };

                //? TODO: Avoid making copies at all
                sc.BeginFigure(ToPoint(points[0]), false, false);
                Point[] tempPoints = new Point[points.Count - 1];

                for (int i = 1; i < points.Count; i++) {
                    tempPoints[i - 1] = ToPoint(points[i]);
                }

                if(usePolyLine) {
                    sc.PolyLineTo(tempPoints, true, false);

                }
                else {
                    sc.PolyBezierTo(tempPoints, true, false);
                }

                // Draw arrow head with a slope matching the line.
                Point start = tempPoints[tempPoints.Length - 1];
                Vector v = start - tempPoints[tempPoints.Length - 2];
                v.Normalize();
                sc.BeginFigure(start + v * 0.1, true, true);

                double t = v.X; v.X = v.Y; v.Y = -t;  // Rotate 90
                sc.LineTo(start + v * 0.075, true, true);
                sc.LineTo(start + v * -0.075, true, true);
            }

            defaultSC.Close();
            loopSC.Close();
            branchSC.Close();
            returnSC.Close();
            immDomSC.Close();

            defaultEdgeGeometry.Freeze();
            loopEdgeGeometry.Freeze();
            branchEdgeGeometry.Freeze();
            returnEdgeGeometry.Freeze();
            immDomEdgeGeometry.Freeze();
            dc.DrawGeometry(Brushes.Black, pen, defaultEdgeGeometry);
            dc.DrawGeometry(loopPen.Brush, loopPen, loopEdgeGeometry);
            dc.DrawGeometry(branchPen.Brush, branchPen, branchEdgeGeometry);
            dc.DrawGeometry(returnPen.Brush, returnPen, returnEdgeGeometry);

            if (options_.ShowImmDominatorEdges) {
                dc.DrawGeometry(immDomPen.Brush, immDomPen, immDomEdgeGeometry);
            }

            dc.Close();
        }
    }
}
