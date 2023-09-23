// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DocumentFormat.OpenXml.Vml;
using IRExplorerCore.Graph;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public enum LabelPlacementKind {
        Top,
        TopLeft,
        TopRight,
        Bottom,
        BottomLeft,
        BottomRight,
        Left,
        Right
    }

    public sealed class GraphEdgeLabel : GraphObject {
        private const double DefaultLabelTextSize = 0.180;
        private const double LabelMargin = 0.1;
        private const double LabelToEdgeMargin = 0.15;

        public Edge EdgeInfo { get; set; }
        public override TaggedObject Data => EdgeInfo.Data;
        public override List<Edge> InEdges => new List<Edge>() { EdgeInfo };
        public override List<Edge> OutEdges => new List<Edge>() { EdgeInfo };
        public override GraphObjectKind Kind => GraphObjectKind.Label;

        public override void Draw() {
            using var dc = Visual.RenderOpen();

            var labelText = new FormattedText(EdgeInfo.Label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextFont, DefaultLabelTextSize, TextColor,
                VisualTreeHelper.GetDpi(Visual).PixelsPerDip);
            double x = EdgeInfo.LabelX - labelText.Width / 2 + LabelToEdgeMargin;
            double y = EdgeInfo.LabelY - labelText.Height / 2;

            var region = new Rect(x - LabelMargin, y - LabelMargin,
                labelText.Width + 2 * LabelMargin, labelText.Height + 2 * LabelMargin);
            dc.DrawRectangle(Style.BackColor, Style.Border, region);
            dc.DrawText(labelText, new Point(x, y));
        }
    }

    public sealed class GraphBoundingBoxLabel : GraphObject {
        private const double DefaultLabelTextSize = 0.20;
        private const double LabelMargin = 0.1;
        private const double LabelToEdgeMargin = 0.15;

        public Node RelativeNodeInfo { get; set; }
        public string Label { get; set; }
        public LabelPlacementKind LabelPlacement;
        public override TaggedObject Data => RelativeNodeInfo.Data;
        public override GraphObjectKind Kind => GraphObjectKind.Label;

        public override void Draw() {
            using var dc = Visual.RenderOpen();
            var (labelText, position, bounds) = CreateOptionalLabel(Label, TextColor,
                LabelPlacement, RelativeNodeInfo, LabelMargin);

            dc.DrawRectangle(Style.BackColor, Style.Border, bounds);
            dc.DrawText(labelText, position);
        }
    }

    public enum GraphObjectKind {
        Node,
        BoundingBox,
        Label
    }

    public class GraphObject {
        private const double DefaultLabelTextSize = 0.190;

        public DrawingVisual Visual { get; set; }
        public HighlightingStyle Style { get; set; }
        public Typeface TextFont { get; set; }
        public Brush TextColor { get; set; }
        public virtual TaggedObject Data => null;
        public virtual List<Edge> InEdges => null;
        public virtual List<Edge> OutEdges => null;
        public virtual GraphObjectKind Kind => GraphObjectKind.Node;
        public bool DataIsElement => Data is IRElement;
        public IRElement DataAsElement => Data as IRElement;

        public virtual void Draw() {
            throw new NotImplementedException();
        }

        protected (FormattedText Text, Point Position, Rect Bounds)
            CreateOptionalLabel(string label, Brush labelColor, LabelPlacementKind labelPlacement,
            Node relativeNode, double labelMargin = 0) {
            var labelText = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, TextFont, DefaultLabelTextSize, labelColor,
                VisualTreeHelper.GetDpi(Visual).PixelsPerDip);

            var region = new Rect(relativeNode.CenterX - relativeNode.Width / 2,
                relativeNode.CenterY - relativeNode.Height / 2, relativeNode.Width, relativeNode.Height);
            var position = labelPlacement switch {
                LabelPlacementKind.Top =>
                    new Point(relativeNode.CenterX - labelText.Width / 2,
                        region.Top - labelText.Height - labelMargin),
                LabelPlacementKind.TopLeft =>
                    new Point(region.Left + labelMargin,
                        region.Top - labelText.Height - labelMargin),
                LabelPlacementKind.Bottom =>
                    new Point(relativeNode.CenterX - labelText.Width / 2,
                        region.Bottom + labelMargin),
                LabelPlacementKind.BottomLeft =>
                    new Point(region.Left,
                        region.Bottom + labelMargin),
                LabelPlacementKind.Left =>
                    new Point(region.Left - labelText.Width - labelMargin,
                        region.Top + labelMargin),
                LabelPlacementKind.Right =>
                    new Point(region.Right + labelMargin,
                        region.Top + labelMargin),
                _ => throw new NotImplementedException()
            };

            var bounds = new Rect(position.X - labelMargin, position.Y - labelMargin,
                labelText.Width + 2 * labelMargin, labelText.Height + 2 * labelMargin);
            return (labelText, position, bounds);
        }
    }

    public sealed class GraphNode : GraphObject {
        private const double DefaultTextSize = 0.225;
        private GraphObjectKind kind_;

        public Node NodeInfo { get; set; }
        public override List<Edge> InEdges => NodeInfo.InEdges;
        public override List<Edge> OutEdges => NodeInfo.OutEdges;
        public override TaggedObject Data => NodeInfo.Data;
        public override GraphObjectKind Kind => kind_;

        public GraphNode(GraphObjectKind kind) {
            kind_ = kind;
        }

        public override void Draw() {
            using var dc = Visual.RenderOpen();
            var graphTag = NodeInfo.Data?.GetTag<GraphNodeTag>();

            // GraphNodeTag may override the colors.
            var backColor = Style.BackColor;
            var textColor = TextColor;

            if (graphTag != null) {
                //? TODO: Option in graphtag if whole node should use other background
                // if (graphTag.BackgroundColor.HasValue) {
                //     backColor = graphTag.BackgroundColor.Value.AsBrush();
                // }
            }

            var region = new Rect(NodeInfo.CenterX - NodeInfo.Width / 2,
                NodeInfo.CenterY - NodeInfo.Height / 2, NodeInfo.Width, NodeInfo.Height);

            // Force pixel-snapping to get sharper edges.
            if (Style.Border != null) {
                double halfPenWidth = Style.Border.Thickness / 2;
                var guidelines = new GuidelineSet();
                guidelines.GuidelinesX.Add(region.Left + halfPenWidth);
                guidelines.GuidelinesX.Add(region.Right + halfPenWidth);
                guidelines.GuidelinesY.Add(region.Top + halfPenWidth);
                guidelines.GuidelinesY.Add(region.Bottom + halfPenWidth);
                dc.PushGuidelineSet(guidelines);

                // Draw node and text.
                dc.DrawRectangle(backColor, Style.Border, region);
            }

            if (!string.IsNullOrEmpty(NodeInfo.Label)) {
                var text = new FormattedText(NodeInfo.Label, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, TextFont, DefaultTextSize, textColor,
                    VisualTreeHelper.GetDpi(Visual).PixelsPerDip);

                dc.DrawText(text, new Point(NodeInfo.CenterX - text.Width / 2,
                    NodeInfo.CenterY - text.Height / 2));
            }

            // Display the label under the node if there is a tag.
            if (graphTag != null && !string.IsNullOrEmpty(graphTag.Label)) {
                var labelColor = graphTag.LabelFontColor.HasValue ? ColorBrushes.GetBrush(graphTag.LabelFontColor.Value) : textColor;
                var (labelText, position, bounds) = CreateOptionalLabel(graphTag.Label, labelColor,
                    graphTag.LabelPlacement, NodeInfo, graphTag.LabelMargin);
                dc.DrawText(labelText, position);
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
        HighlightingStyle GetNodeStyle(Node node);

        HighlightingStyle GetBoundingBoxStyle(Node node);
        HighlightingStyle GetBoundingBoxLabelStyle(Node node);
        Brush GetBoundingBoxLabelColor(Node node);

        Pen GetEdgeStyle(GraphEdgeKind kind);
        GraphEdgeKind GetEdgeKind(Edge edge);
        HighlightingStyle GetEdgeLabelStyle(Edge edge);
        Brush GetEdgeLabelTextColor(Edge edge);

        bool ShouldRenderEdges(GraphEdgeKind kind);
        bool ShouldUsePolylines();
    }

    public class GraphRenderer {
        private const double DefaultEdgeThickness = 0.025;
        private const double GroupBoundingBoxMargin = 0.20;
        private const double RegionBoundingBoxMargin = 0.30;
        private const double GroupBoundingBoxTextMargin = 0.07;
        private Typeface nodeFont_;
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
            nodeFont_ = new Typeface("Verdana");
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

            foreach (var pair in graph_.DataNodeGroupsMap) {
                var nodeGroup = pair.Value;

                if (!nodeGroup.DrawBoundingBox) {
                    continue; // Used when grouping by region, but not by blocks.
                }

                var boundingBox = ComputeBoundingBox(nodeGroup.Nodes, out bool wrapsBlocks);

                if (boundingBox.IsEmpty) {
                    return;
                }

                var margin = wrapsBlocks ? RegionBoundingBoxMargin : GroupBoundingBoxMargin;
                boundingBox.Inflate(margin, margin);
                var groupVisual = new DrawingVisual();

                LabelPlacementKind labelPlacement = LabelPlacementKind.Bottom;
                Brush boxColor = Brushes.Transparent;

                if (pair.Key is BlockIR block) {
                    labelPlacement = LabelPlacementKind.Right;
                }
                else if (pair.Key is RegionIR region) {
                    //? TODO: Improve this to avoid overlap.
                    //if (region.HasChildRegions) {
                        labelPlacement = LabelPlacementKind.TopLeft;
                    //}
                    //else {
                     //   labelPlacement = LabelPlacementKind.Bottom;
                    //}
                }

                var node = new Node() {
                    Data = pair.Key,
                    CenterX = boundingBox.Left + boundingBox.Width / 2,
                    CenterY = boundingBox.Top + boundingBox.Height / 2,
                    Width = boundingBox.Width,
                    Height = boundingBox.Height,
                };

                var nodeVisual = new DrawingVisual();
                var graphNode = new GraphNode(GraphObjectKind.BoundingBox) {
                    NodeInfo = node,
                    Visual = nodeVisual,
                    TextFont = nodeFont_,
                    Style = graphStyle_.GetBoundingBoxStyle(node)
                };

                CreateBoundingBoxLabelVisual(nodeGroup.Label, node, labelPlacement);

                graphNode.Draw();
                node.Tag = graphNode;
                nodeVisual.SetValue(FrameworkElement.TagProperty, graphNode);
                visual_.Children.Add(nodeVisual);
            }
        }

        private Rect ComputeBoundingBox(List<TaggedObject> nodeElements, out bool wrapsBlocks) {
            double xMin = double.MaxValue;
            double yMin = double.MaxValue;
            double xMax = double.MinValue;
            double yMax = double.MinValue;
            wrapsBlocks = false;

            foreach (var element in nodeElements) {
                // For regions including multiple blocks.
                if (element is BlockIR block) {
                    wrapsBlocks = true;

                    foreach (var tuple in block.Tuples) {
                        ComputeBoundingBox(tuple, ref xMin, ref yMin, ref xMax, ref yMax);
                    }
                }
                else {
                    ComputeBoundingBox(element, ref xMin, ref yMin, ref xMax, ref yMax);
                }
            }

            if (xMax - xMin < 0 || yMax - yMin < 0) {
                Trace.TraceError($"ComputeBoundingBox invalid bounding box: {xMin}, {yMin}, {xMax}, {yMax}");
                return Rect.Empty;
            }

            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private void ComputeBoundingBox(TaggedObject element, ref double xMin, ref double yMin, ref double xMax,
            ref double yMax) {
            if (!graph_.DataNodeMap.ContainsKey(element)) {
                Trace.TraceError($"ComputeBoundingBox element not in node map: {element}");
                return;
            }

            var node = graph_.DataNodeMap[element];
            xMin = Math.Min(xMin, node.CenterX - node.Width / 2);
            yMin = Math.Min(yMin, node.CenterY - node.Height / 2);
            xMax = Math.Max(xMax, node.CenterX + node.Width / 2);
            yMax = Math.Max(yMax, node.CenterY + node.Height / 2);
        }

        public HighlightingStyle GetDefaultNodeStyle() {
            return graphStyle_.GetDefaultNodeStyle();
        }

        public HighlightingStyle GetDefaultNodeStyle(GraphObject node) {
            switch (node) {
                case GraphEdgeLabel edgeLabel:
                    return graphStyle_.GetEdgeLabelStyle(edgeLabel.EdgeInfo);
                case GraphNode graphNode:
                    return graphStyle_.GetNodeStyle(graphNode.NodeInfo);
                default:
                    throw new NotImplementedException();
            }
        }

        private void DrawNodes() {
            var textColor = graphStyle_.GetDefaultTextColor();

            foreach (var node in graph_.Nodes) {
                if (node == null) {
                    Trace.TraceError($"DrawNodes element null node");
                    continue; //? TODO: Investigate
                }

                var nodeVisual = new DrawingVisual();

                var graphNode = new GraphNode(GraphObjectKind.Node) {
                    NodeInfo = node,
                    Visual = nodeVisual,
                    TextFont = nodeFont_,
                    TextColor = textColor,
                    Style = graphStyle_.GetNodeStyle(node)
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

                var sc = edgeType switch {
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

                if (!string.IsNullOrEmpty(edge.Label)) {
                    CreateEdgeLabelVisual(edge);
                }
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

        private void CreateEdgeLabelVisual(Edge edge) {
            var nodeVisual = new DrawingVisual();

            var graphNode = new GraphEdgeLabel() {
                EdgeInfo = edge,
                Visual = nodeVisual,
                TextFont = edgeFont_,
                TextColor = graphStyle_.GetEdgeLabelTextColor(edge),
                Style = graphStyle_.GetEdgeLabelStyle(edge)
            };

            graphNode.Draw();
            edge.Tag = graphNode;
            nodeVisual.SetValue(FrameworkElement.TagProperty, graphNode);
            visual_.Children.Add(nodeVisual);
        }

        private void CreateBoundingBoxLabelVisual(string label, Node relativeNode, LabelPlacementKind labelPlacement) {
            var nodeVisual = new DrawingVisual();
            var graphNode = new GraphBoundingBoxLabel() {
                Label = label,
                RelativeNodeInfo = relativeNode,
                LabelPlacement = labelPlacement,
                Visual = nodeVisual,
                TextFont = edgeFont_,
                TextColor = graphStyle_.GetBoundingBoxLabelColor(relativeNode),
                Style = graphStyle_.GetBoundingBoxLabelStyle(relativeNode)
            };

            graphNode.Draw();
            nodeVisual.SetValue(FrameworkElement.TagProperty, graphNode);
            visual_.Children.Add(nodeVisual);
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
            for (int i = tempPoints.Length - 1; i > 0; i--) {
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