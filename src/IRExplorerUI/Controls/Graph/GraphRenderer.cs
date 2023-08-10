// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IRExplorerCore.Graph;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public class GraphNode {
        private const double DefaultTextSize = 0.225;
        private const double DefaultLabelTextSize = 0.190;

        public Node NodeInfo { get; set; }
        public GraphSettings Settings{ get; set; }
        public DrawingVisual Visual { get; set; }
        public HighlightingStyle Style { get; set; }
        public Typeface TextFont { get; set; }
        public Brush TextColor { get; set; }

        public void Draw() {
            using var dc = Visual.RenderOpen();
            var graphTag = NodeInfo.Data?.GetTag<GraphNodeTag>();

            // GraphNodeTag may override the colors.
            var backColor = Style.BackColor;
            var textColor = TextColor;

            if (graphTag != null) {
                // if (graphTag.BackgroundColor.HasValue) {
                //     backColor = graphTag.BackgroundColor.Value.AsBrush();
                // }
            }

            var region = new Rect(NodeInfo.CenterX - NodeInfo.Width / 2,
                NodeInfo.CenterY - NodeInfo.Height / 2, NodeInfo.Width, NodeInfo.Height);

            // Force pixel-snapping to get sharper edges.
            double halfPenWidth = Style.Border.Thickness / 2;
            var guidelines = new GuidelineSet();
            guidelines.GuidelinesX.Add(region.Left + halfPenWidth);
            guidelines.GuidelinesX.Add(region.Right + halfPenWidth);
            guidelines.GuidelinesY.Add(region.Top + halfPenWidth);
            guidelines.GuidelinesY.Add(region.Bottom + halfPenWidth);
            dc.PushGuidelineSet(guidelines);

            // Draw node and text.
            dc.DrawRectangle(backColor, Style.Border, region);

            if (!string.IsNullOrEmpty(NodeInfo.Label)) {
                var text = new FormattedText(NodeInfo.Label, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, TextFont, DefaultTextSize, textColor,
                    VisualTreeHelper.GetDpi(Visual).PixelsPerDip);

                dc.DrawText(text, new Point(NodeInfo.CenterX - text.Width / 2,
                    NodeInfo.CenterY - text.Height / 2));
            }

            // Display the label under the node if there is a tag.
            if(graphTag != null && !string.IsNullOrEmpty(graphTag.Label)) {
                var labelColor = graphTag.LabelFontColor.HasValue ? ColorBrushes.GetBrush(graphTag.LabelFontColor.Value) : textColor;
                var labelText = new FormattedText(graphTag.Label, CultureInfo.InvariantCulture,
                                                  FlowDirection.LeftToRight, TextFont, DefaultLabelTextSize, labelColor,
                                                  VisualTreeHelper.GetDpi(Visual).PixelsPerDip);
                // var textBackground = ColorBrushes.GetBrush(Settings.BackgroundColor);
                // dc.DrawRectangle(textBackground, null, new Rect(NodeInfo.CenterX - labelText.Width / 2,
                //                                   region.Bottom + labelText.Height / 4,
                //                                   labelText.Width, labelText.Height));

                //? TODO: Use LabelPlacement
                switch (graphTag.LabelPlacement) {
                    case GraphNodeTag.LabelPlacementKind.Top: {
                        dc.DrawText(labelText, new Point(NodeInfo.CenterX - labelText.Width / 2,
                            region.Top - labelText.Height - graphTag.LabelMargin));
                        break;
                    }
                    case GraphNodeTag.LabelPlacementKind.Bottom: {
                        dc.DrawText(labelText, new Point(NodeInfo.CenterX - labelText.Width / 2,
                            region.Bottom + graphTag.LabelMargin));
                        break;
                    }
                    case GraphNodeTag.LabelPlacementKind.Left: {
                        dc.DrawText(labelText, new Point(region.Left - labelText.Width - graphTag.LabelMargin,
                            region.Top + graphTag.LabelMargin));
                        break;
                    }
                    case GraphNodeTag.LabelPlacementKind.Right: {
                        dc.DrawText(labelText, new Point(region.Right + graphTag.LabelMargin,
                            region.Top + graphTag.LabelMargin));
                        break;
                    }
                }
            }
        }
    }

    public enum GraphEdgeKind {
        Default,
        Loop,
        Branch,
        Return,
        ImmediateDominator,
        ImmediatePostDominator
    }

    public interface IGraphStyleProvider {
        Brush GetDefaultTextColor();
        HighlightingStyle GetDefaultNodeStyle();
        public HighlightingStyle GetNodeStyle(Node node);
        public Pen GetEdgeStyle(GraphEdgeKind kind);
        public GraphEdgeKind GetEdgeKind(Edge edge);
        bool ShouldRenderEdges(GraphEdgeKind kind);
        bool ShouldUsePolylines();
        Brush GetDefaultNodeBackground();
    }

    public class GraphRenderer {
        private const double DefaultEdgeThickness = 0.025;
        private const double GroupBoundingBoxMargin = 0.20;
        private const double GroupBoundingBoxTextMargin = 0.07;
        private Typeface defaultNodeFont_;
        private Typeface edgeFont_;
        private Graph graph_;
        private IGraphStyleProvider graphStyle_;
        private ICompilerInfoProvider compilerInfo_;
        private GraphSettings settings_;
        private DrawingVisual visual_;

        public GraphRenderer(Graph graph, GraphSettings settings,
                             ICompilerInfoProvider compilerInfo) {
            settings_ = settings;
            graph_ = graph;
            compilerInfo_ = compilerInfo;
            edgeFont_ = new Typeface("Verdana");
            defaultNodeFont_ = new Typeface("Verdana");
            graphStyle_ = compilerInfo.CreateGraphStyleProvider(graph, settings);
        }

        public DrawingVisual Render() {
            visual_ = new DrawingVisual();

            if (graph_.DataNodeGroupsMap != null) {
                DrawNodeBoundingBoxes();
            }

            DrawNodes();
            DrawEdges();

            // Can be null if the CFG is not available.
            visual_.Drawing?.Freeze();
            return visual_;
        }

        private void DrawNodeBoundingBoxes() {
            var pen = ColorPens.GetPen(Colors.Gray, DefaultEdgeThickness);

            foreach (var group in graph_.DataNodeGroupsMap) {
                var boundingBox = ComputeBoundingBox(group.Value);
                boundingBox.Inflate(GroupBoundingBoxMargin, GroupBoundingBoxMargin);
                var groupVisual = new DrawingVisual();

                string label = "";
                GraphNodeTag.LabelPlacementKind labelPlacement = GraphNodeTag.LabelPlacementKind.Right;
                Brush boxColor = Brushes.Transparent;

                if (group.Key is BlockIR block) {
                    label = $"B{((BlockIR)group.Key).Number}";
                }
                else if (group.Key is RegionIR region) {
                    if (region.ParentRegion == null) {
                        continue; // Don't draw the root region.
                    }

                    if (region.HasChildRegions) {
                        labelPlacement = GraphNodeTag.LabelPlacementKind.Top;
                    }
                    else {
                        labelPlacement = GraphNodeTag.LabelPlacementKind.Bottom;
                    }

                    if (region.Owner is InstructionIR instr) {
                        label = instr.OpcodeText.ToString();

                        if (label.Contains("for")) {
                            boxColor = Brushes.LightBlue;
                        }
                    }
                    else {
                        label = "region";
                    }
                }

                var graphTag = group.Key.GetOrAddTag<GraphNodeTag>();
                graphTag.Label = label;
                graphTag.LabelFontColor = Colors.DimGray;
                graphTag.LabelPlacement = labelPlacement;
                graphTag.LabelMargin = GroupBoundingBoxTextMargin;

                var node = new Node() {
                    Data = group.Key,
                    CenterX = boundingBox.Left + boundingBox.Width / 2,
                    CenterY = boundingBox.Top + boundingBox.Height / 2,
                    Width = boundingBox.Width,
                    Height = boundingBox.Height,

                };

                var nodeVisual = new DrawingVisual();
                var graphNode = new GraphNode {
                    NodeInfo = node,
                    Settings = settings_,
                    Visual = nodeVisual,
                    TextFont = defaultNodeFont_,
                    TextColor = Brushes.Black,
                    Style = GetBoundingBoxNodeStyle(node)
                };

                graphNode.Draw();
                node.Tag = graphNode;
                nodeVisual.SetValue(FrameworkElement.TagProperty, graphNode);
                visual_.Children.Add(nodeVisual);
            }
        }

        private HighlightingStyle GetBoundingBoxNodeStyle(Node node) {
            var border = ColorPens.GetDashedPen(Colors.Gray, DashStyles.Dot, DefaultEdgeThickness);
            var fillColor = ColorBrushes.GetTransparentBrush(Colors.LightGray, 10);

            if (node.Data is RegionIR region) {
                if (region.Owner is InstructionIR instr) {
                    var label = instr.OpcodeText.ToString();

                    if (label.Contains("scf")) {
                        fillColor = ColorBrushes.GetTransparentBrush(Colors.SkyBlue, 20);
                    }
                    else if (label.Contains("linalg")) {
                        fillColor = ColorBrushes.GetTransparentBrush(Colors.Orchid, 20);
                    }
                    else if (label.Contains("affine")) {
                        fillColor = ColorBrushes.GetTransparentBrush(Colors.PaleGreen, 20);
                    }
                }
            }

            return new HighlightingStyle(fillColor, border);
        }

        private Rect ComputeBoundingBox(List<TaggedObject> nodeElements) {
            double xMin = double.MaxValue;
            double yMin = double.MaxValue;
            double xMax = double.MinValue;
            double yMax = double.MinValue;

            foreach (var element in nodeElements) {
                if(!graph_.DataNodeMap.ContainsKey(element)) {
                    Trace.TraceError($"ComputeBoundingBox element not in node map: {element}");
                    continue;
                }

                var node = graph_.DataNodeMap[element];
                xMin = Math.Min(xMin, node.CenterX - node.Width / 2);
                yMin = Math.Min(yMin, node.CenterY - node.Height / 2);
                xMax = Math.Max(xMax, node.CenterX + node.Width / 2);
                yMax = Math.Max(yMax, node.CenterY + node.Height / 2);
            }

            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        public HighlightingStyle GetDefaultNodeStyle() {
            return graphStyle_.GetDefaultNodeStyle();
        }

        public HighlightingStyle GetDefaultNodeStyle(GraphNode node) {
            return graphStyle_.GetNodeStyle(node.NodeInfo);
        }

        public HighlightingStyle GetDefaultNodeStyle(Node node) {
            return graphStyle_.GetNodeStyle(node);
        }

        private void DrawNodes() {
            var textColor = graphStyle_.GetDefaultTextColor();

            foreach (var node in graph_.Nodes) {
                if (node == null) {
                    Trace.TraceError($"DrawNodes element null node");
                    continue; //? TODO: Investigate
                }

                var nodeVisual = new DrawingVisual();

                if (node.Label.Contains("\\n")) {
                    node.Label = node.Label.Replace("\\n", "\n");
                }

                var graphNode = new GraphNode {
                    NodeInfo = node,
                    Settings = settings_,
                    Visual = nodeVisual,
                    TextFont = defaultNodeFont_,
                    TextColor = textColor,
                    Style = GetDefaultNodeStyle(node)
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

        private void DrawEdges() {
            var pen = graphStyle_.GetEdgeStyle(GraphEdgeKind.Default);
            var loopPen = graphStyle_.GetEdgeStyle(GraphEdgeKind.Loop);
            var branchPen = graphStyle_.GetEdgeStyle(GraphEdgeKind.Branch);
            var returnPen = graphStyle_.GetEdgeStyle(GraphEdgeKind.Return);
            var immDomPen = graphStyle_.GetEdgeStyle(GraphEdgeKind.ImmediateDominator);

            var lineVisual = new DrawingVisual();
            var dc = lineVisual.RenderOpen();
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
            bool usePolyLine = graphStyle_.ShouldUsePolylines();

            foreach (var edge in graph_.Edges) {
                var points = edge.LinePoints;
                var edgeType = graphStyle_.GetEdgeKind(edge);

                var sc = edgeType switch
                {
                    GraphEdgeKind.Default => defaultSC,
                    GraphEdgeKind.Branch => branchSC,
                    GraphEdgeKind.Loop => loopSC,
                    GraphEdgeKind.Return => returnSC,
                    GraphEdgeKind.ImmediateDominator => immDomSC,
                    GraphEdgeKind.ImmediatePostDominator => immDomSC,
                    _ => defaultSC
                };

                //? TODO: Avoid making copies at all
                sc.BeginFigure(ToPoint(points[0]), false, false);
                var tempPoints = new Point[points.Length - 1];

                for (int i = 1; i < points.Length; i++) {
                    tempPoints[i - 1] = ToPoint(points[i]);
                }

                if (usePolyLine) {
                    sc.PolyLineTo(tempPoints, true, false);
                }
                else {
                    sc.PolyBezierTo(tempPoints, true, false);
                }

                // Draw arrow head with a slope matching the line,
                // but only if the target node is visible.
                DrawEdgeArrow(edge, tempPoints, sc);
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
            dc.DrawGeometry(pen.Brush, pen, defaultEdgeGeometry);
            dc.DrawGeometry(loopPen.Brush, loopPen, loopEdgeGeometry);
            dc.DrawGeometry(branchPen.Brush, branchPen, branchEdgeGeometry);
            dc.DrawGeometry(returnPen.Brush, returnPen, returnEdgeGeometry);

            if (graphStyle_.ShouldRenderEdges(GraphEdgeKind.ImmediateDominator)) {
                dc.DrawGeometry(immDomPen.Brush, immDomPen, immDomEdgeGeometry);
            }

            dc.Close();
            visual_.Children.Add(lineVisual);
        }

        private void DrawEdgeArrow(Edge edge, Point[] tempPoints, StreamGeometryContext sc) {
            // Draw arrow head with a slope matching the line,
            // this uses the last two points to find the angle.
            Point start;
            Vector v = FindArrowOrientation(tempPoints, out start);

            sc.BeginFigure(start + v * 0.1, true, true);
            double t = v.X;
            v.X = v.Y;
            v.Y = -t; // Rotate 90
            sc.LineTo(start + v * 0.075, true, true);
            sc.LineTo(start + v * -0.075, true, true);
        }

        private Vector FindArrowOrientation(Point[] tempPoints, out Point start) {
            for(int i = tempPoints.Length - 1; i > 0; i--) {
                start = tempPoints[i];
                var v = start - tempPoints[i - 1];

                if (v.LengthSquared != 0) {
                    v.Normalize();
                    return v;
                }
            }

            return new Vector(0, 0);
        }
    }
}