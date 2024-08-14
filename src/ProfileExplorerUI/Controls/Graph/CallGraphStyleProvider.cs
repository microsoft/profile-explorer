// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows.Media;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.Graph;

namespace ProfileExplorer.UI;

public sealed class CallGraphStyleProvider : IGraphStyleProvider {
  private const double DefaultEdgeThickness = 0.025;
  private const double BoldEdgeThickness = 0.05;
  private Graph graph_;
  private Brush defaultNodeBackground_;
  private HighlightingStyle defaultNodeStyle_;
  private HighlightingStyle leafNodeStyle_;
  private HighlightingStyle entryNodeStyle_;
  private HighlightingStyle externalNodeStyle_;
  private Brush defaultTextColor_;
  private Pen edgeStyle_;

  public CallGraphStyleProvider(Graph graph) {
    graph_ = graph;
    defaultTextColor_ = ColorBrushes.GetBrush(Colors.Black);
    defaultNodeBackground_ = ColorBrushes.GetBrush(Colors.Gainsboro);
    defaultNodeStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                              ColorPens.GetPen(Colors.DimGray, DefaultEdgeThickness));
    leafNodeStyle_ = new HighlightingStyle(ColorBrushes.GetBrush(Colors.LightBlue),
                                           ColorPens.GetPen(Colors.DimGray, DefaultEdgeThickness));
    entryNodeStyle_ = new HighlightingStyle(ColorBrushes.GetBrush(Colors.LightGreen),
                                            ColorPens.GetPen(Colors.DimGray, BoldEdgeThickness));
    externalNodeStyle_ = new HighlightingStyle(ColorBrushes.GetBrush(Colors.Moccasin),
                                               ColorPens.GetPen(Colors.DimGray, DefaultEdgeThickness));
    edgeStyle_ = ColorPens.GetPen(Colors.DarkBlue, DefaultEdgeThickness);
  }

  public Brush GetDefaultNodeBackground() {
    return defaultNodeBackground_;
  }

  public HighlightingStyle GetDefaultNodeStyle() {
    return defaultNodeStyle_;
  }

  public Brush GetDefaultTextColor() {
    return defaultTextColor_;
  }

  public GraphEdgeKind GetEdgeKind(Edge edge) {
    return GraphEdgeKind.Default;
  }

  public Pen GetEdgeStyle(GraphEdgeKind kind) {
    return edgeStyle_;
  }

  public HighlightingStyle GetNodeStyle(Node node) {
    var callNode = (CallGraphNode)node.Data;

    if (callNode == null) {
      return defaultNodeStyle_;
    }

    // Check for a tag that overrides the style.
    var graphTag = callNode.GetTag<GraphNodeTag>();

    if (graphTag != null) {
      var background = graphTag.BackgroundColor ?? Colors.Gainsboro;
      var borderColor = graphTag.BorderColor ?? Colors.DimGray;
      double borderThickness = graphTag.BorderThickness != 0 ? graphTag.BorderThickness : DefaultEdgeThickness;
      return new HighlightingStyle(background, ColorPens.GetPen(borderColor, borderThickness));
    }

    if (callNode.IsExternal) {
      return externalNodeStyle_;
    }

    if (!callNode.HasCallers) {
      return entryNodeStyle_;
    }

    if (!callNode.HasCallees) {
      return leafNodeStyle_;
    }

    return defaultNodeStyle_;
  }

  public bool ShouldRenderEdges(GraphEdgeKind kind) {
    return true;
  }

  public bool ShouldUsePolylines() {
    return false;
  }
}