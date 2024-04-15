// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI;

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
  Brush GetDefaultNodeBackground();
  HighlightingStyle GetDefaultNodeStyle();
  HighlightingStyle GetNodeStyle(Node node);
  Pen GetEdgeStyle(GraphEdgeKind kind);
  GraphEdgeKind GetEdgeKind(Edge edge);
  bool ShouldRenderEdges(GraphEdgeKind kind);
  bool ShouldUsePolylines();
}

public class GraphNode {
  private const double DefaultTextSize = 0.225;
  private const double DefaultLabelTextSize = 0.205;
  public Node NodeInfo { get; set; }
  public GraphSettings Settings { get; set; }
  public DrawingVisual Visual { get; set; }
  public HighlightingStyle Style { get; set; }
  public Typeface TextFont { get; set; }
  public Typeface MarkedTextFont { get; set; }
  public Typeface MarkedLabelTextFont { get; set; }
  public Brush TextColor { get; set; }

  public void Draw() {
    using var dc = Visual.RenderOpen();
    var graphTag = NodeInfo.Data?.GetTag<GraphNodeTag>();

    // GraphNodeTag may override the colors.
    var backColor = Style.BackColor;
    var textColor = TextColor;
    var labelColor = TextColor;
    var textFont = TextFont;

    if (graphTag != null) {
      if (graphTag.BackgroundColor.HasValue) {
        backColor = graphTag.BackgroundColor.Value.AsBrush();
      }

      if (graphTag.TextColor.HasValue) {
        textColor = graphTag.TextColor.Value.AsBrush();
        textFont = MarkedTextFont;
      }

      if (graphTag.LabelTextColor.HasValue) {
        labelColor = graphTag.LabelTextColor.Value.AsBrush();
      }
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

    var text = new FormattedText(NodeInfo.Label, CultureInfo.InvariantCulture,
                                 FlowDirection.LeftToRight, textFont, DefaultTextSize, textColor,
                                 VisualTreeHelper.GetDpi(Visual).PixelsPerDip);

    dc.DrawText(text, new Point(NodeInfo.CenterX - text.Width / 2,
                                NodeInfo.CenterY - text.Height / 2));

    // Display the label under the node if there is a tag.
    if (graphTag != null && !string.IsNullOrEmpty(graphTag.Label)) {
      var labelText = new FormattedText(graphTag.Label, CultureInfo.InvariantCulture,
                                        FlowDirection.LeftToRight, MarkedLabelTextFont, DefaultLabelTextSize, labelColor,
                                        VisualTreeHelper.GetDpi(Visual).PixelsPerDip);
      var textBackground = ColorBrushes.GetBrush(Settings.BackgroundColor);
      dc.DrawRectangle(textBackground, null, new Rect(NodeInfo.CenterX - labelText.Width / 2,
                                                      region.Bottom + labelText.Height / 4,
                                                      labelText.Width, labelText.Height));

      //? TODO: Use LabelPlacement
      dc.DrawText(labelText, new Point(NodeInfo.CenterX - labelText.Width / 2,
                                       region.Bottom + labelText.Height / 4));
    }
  }
}

public class GraphRenderer {
  private const double DefaultEdgeThickness = 0.025;
  private const double GroupBoundingBoxMargin = 0.20;
  private const double GroupBoundingBoxTextMargin = 0.10;
  private Typeface nodeFont_;
  private Typeface markedNodeFont_;
  private Typeface labelFont_;
  private Typeface edgeFont_;
  private Graph graph_;
  private IGraphStyleProvider graphStyle_;
  private GraphSettings settings_;
  private DrawingVisual visual_;

  public GraphRenderer(Graph graph, GraphSettings settings,
                       ICompilerInfoProvider compilerInfo) {
    settings_ = settings;
    graph_ = graph;
    edgeFont_ = new Typeface(new FontFamily("Verdana"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    nodeFont_ = new Typeface(new FontFamily("Verdana"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    markedNodeFont_= new Typeface(new FontFamily("Verdana"), FontStyles.Normal, FontWeights.DemiBold, FontStretches.Normal);
    labelFont_ = new Typeface(new FontFamily("Verdana"), FontStyles.Normal, FontWeights.DemiBold, FontStretches.Normal);

    graphStyle_ = graph.Kind switch {
      GraphKind.FlowGraph =>
        new FlowGraphStyleProvider(graph, (FlowGraphSettings)settings),
      GraphKind.DominatorTree =>
        new FlowGraphStyleProvider(graph, (FlowGraphSettings)settings),
      GraphKind.PostDominatorTree =>
        new FlowGraphStyleProvider(graph, (FlowGraphSettings)settings),
      GraphKind.ExpressionGraph =>
        new ExpressionGraphStyleProvider(graph_, (ExpressionGraphSettings)settings, compilerInfo),
      GraphKind.CallGraph =>
        new CallGraphStyleProvider(graph),
      _ => throw new InvalidOperationException("Unknown graph kind!")
    };
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

  public HighlightingStyle GetDefaultNodeStyle() {
    return graphStyle_.GetDefaultNodeStyle();
  }

  public HighlightingStyle GetDefaultNodeStyle(GraphNode node) {
    return graphStyle_.GetNodeStyle(node.NodeInfo);
  }

  public HighlightingStyle GetDefaultNodeStyle(Node node) {
    return graphStyle_.GetNodeStyle(node);
  }

  private void DrawNodeBoundingBoxes() {
    var pen = ColorPens.GetPen(Colors.Gray, DefaultEdgeThickness);

    foreach (var group in graph_.DataNodeGroupsMap) {
      var boundingBox = ComputeBoundingBox(group.Value);
      boundingBox.Inflate(GroupBoundingBoxMargin, GroupBoundingBoxMargin);
      var groupVisual = new DrawingVisual();

      using (var dc = groupVisual.RenderOpen()) {
        dc.DrawRectangle(Brushes.Transparent, pen, boundingBox);
        double textSize = 0.25;

        var text = new FormattedText($"B{((BlockIR)group.Key).Number}",
                                     CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                     nodeFont_, textSize, Brushes.DimGray,
                                     VisualTreeHelper.GetDpi(groupVisual).PixelsPerDip);
        //? TODO: Text placement can overlap with other elements,
        //? try each corner of the bounding box to find one that's free
        //? (it's a greedy approach, but would work in most cases).
        dc.DrawText(text, new Point(boundingBox.Right + GroupBoundingBoxTextMargin,
                                    boundingBox.Top + GroupBoundingBoxTextMargin));
      }

      visual_.Children.Add(groupVisual);
    }
  }

  private Rect ComputeBoundingBox(List<TaggedObject> nodeElements) {
    double xMin = double.MaxValue;
    double yMin = double.MaxValue;
    double xMax = double.MinValue;
    double yMax = double.MinValue;

    foreach (var element in nodeElements) {
      if (!graph_.DataNodeMap.ContainsKey(element)) {
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

  private void DrawNodes() {
    var textColor = graphStyle_.GetDefaultTextColor();

    foreach (var node in graph_.Nodes) {
      if (node == null) {
        Trace.TraceError("DrawNodes element null node");
        continue; //? TODO: Investigate
      }

      var nodeVisual = new DrawingVisual();

      var graphNode = new GraphNode {
        NodeInfo = node,
        Settings = settings_,
        Visual = nodeVisual,
        TextFont = nodeFont_,
        MarkedTextFont = markedNodeFont_,
        MarkedLabelTextFont = labelFont_,
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
  }

  private void DrawEdgeArrow(Edge edge, Point[] tempPoints, StreamGeometryContext sc) {
    // Draw arrow head with a slope matching the line,
    // this uses the last two points to find the angle.
    Point start;
    var v = FindArrowOrientation(tempPoints, out start);

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