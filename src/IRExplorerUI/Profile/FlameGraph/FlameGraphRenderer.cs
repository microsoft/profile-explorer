// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IRExplorerUI.Profile;

public class FlameGraphRenderer {
  internal const double DefaultTextSize = 12;
  internal const double DefaultNodeHeight = 18;
  internal const double CompactTextSize = 11;
  internal const double CompactNodeHeight = 15;
  private FlameGraphSettings settings_;
  private FlameGraph flameGraph_;
  private int maxNodeDepth_;
  private uint renderVersion_;
  private double nodeHeight_;
  private double maxWidth_;
  private double prevMaxWidth_;
  private double minVisibleRectWidth_;
  private Rect visibleArea_;
  private Rect quadVisibleArea_;
  private Rect quadGraphArea_;
  private bool isTimeline_;
  private Typeface font_;
  private Typeface nameFont_;
  private double fontSize_;
  private Dictionary<ProfileCallTreeNodeKind, ColorPalette> palettes_;
  private Pen defaultBorder_;
  private Brush placeholderTileBrush_;
  private DrawingVisual graphVisual_;
  private GlyphRunCache glyphs_;
  private GlyphRunCache nameGlyphs_;
  private QuadTree<FlameGraphNode> nodesQuadTree_;
  private QuadTree<FlameGraphGroupNode> dummyNodesQuadTree_;
  private Dictionary<HighlightingStyle, HighlightingStyle> dummyNodeStyles_;
  private GuidelineSet cachedTextGuidelines_;
  private GuidelineSet cachedNodeGuidelines_;
  private GuidelineSet cachedDummyNodeGuidelines_;
  private SolidColorBrush nodeTextBrush_;
  private SolidColorBrush nodeModuleBrush_;
  private SolidColorBrush nodeWeightBrush_;
  private SolidColorBrush nodePercentageBrush_;
  private SolidColorBrush searchResultMarkingBrush_;

  public FlameGraphRenderer(FlameGraph flameGraph, Rect visibleArea, FlameGraphSettings settings, bool isTimeline) {
    isTimeline_ = isTimeline;
    settings_ = settings;
    flameGraph_ = flameGraph;
    maxWidth_ = visibleArea.Width;
    prevMaxWidth_ = maxWidth_;
    visibleArea_ = visibleArea;
    UpdateGraphSizes();
    ReloadSettings();
    dummyNodeStyles_ = new Dictionary<HighlightingStyle, HighlightingStyle>();
  }

  private void ReloadSettings() {
    palettes_ = new Dictionary<ProfileCallTreeNodeKind, ColorPalette> {
      [ProfileCallTreeNodeKind.Unset] = ColorPalette.GetPalette(settings_.DefaultColorPalette),
      [ProfileCallTreeNodeKind.NativeUser] = ColorPalette.GetPalette(settings_.DefaultColorPalette),
      [ProfileCallTreeNodeKind.NativeKernel] = settings_.UseKernelColorPalette ?
        ColorPalette.GetPalette(settings_.KernelColorPalette) :
        ColorPalette.GetPalette(settings_.DefaultColorPalette),
      [ProfileCallTreeNodeKind.Managed] = settings_.UseManagedColorPalette ?
        ColorPalette.GetPalette(settings_.ManagedColorPalette) :
        ColorPalette.GetPalette(settings_.DefaultColorPalette)
    };

    defaultBorder_ = ColorPens.GetPen(settings_.NodeBorderColor, 0.5);
    font_ = new Typeface("Segoe UI"); //? TODO: Option
    nameFont_ = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium,
                             FontStretch.FromOpenTypeStretch(5));
    nodeTextBrush_ = settings_.NodeTextColor.AsBrush();
    nodeModuleBrush_ = settings_.NodeModuleColor.AsBrush();
    nodeWeightBrush_ = settings_.NodeWeightColor.AsBrush();
    nodePercentageBrush_ = settings_.NodePercentageColor.AsBrush();
    searchResultMarkingBrush_ = settings_.SearchResultMarkingColor.AsBrush();

    if (settings_.UseCompactMode) {
      nodeHeight_ = CompactNodeHeight;
      fontSize_ = CompactTextSize;
    }
    else {
      nodeHeight_ = DefaultNodeHeight;
      fontSize_ = DefaultTextSize;
    }

    if (graphVisual_ != null) {
      glyphs_ = new GlyphRunCache(font_, fontSize_, VisualTreeHelper.GetDpi(graphVisual_).PixelsPerDip);
      nameGlyphs_ = new GlyphRunCache(nameFont_, fontSize_, VisualTreeHelper.GetDpi(graphVisual_).PixelsPerDip);
    }
  }

  public GlyphRunCache GlyphsCache => glyphs_;
  public GlyphRunCache NameGlyphsCache => nameGlyphs_;
  public double MaxGraphWidth => maxWidth_;
  public double MaxGraphHeight => (maxNodeDepth_ + 1) * nodeHeight_;
  public Rect VisibleArea => visibleArea_;
  public Rect GraphArea => new Rect(0, 0, MaxGraphWidth, MaxGraphHeight);
  public Dictionary<FlameGraphNode, HighlightingStyle> SelectedNodes { get; set; }

  public static BitmapSource BitmapSourceFromBrush(Brush drawingBrush, int size = 32, int dpi = 96) {
    // RenderTargetBitmap = builds a bitmap rendering of a visual
    var pixelFormat = PixelFormats.Pbgra32;
    var rtb = new RenderTargetBitmap(size, size, dpi, dpi, pixelFormat);

    // Drawing visual allows us to compose graphic drawing parts into a visual to render
    var drawingVisual = new DrawingVisual();

    using (var context = drawingVisual.RenderOpen()) {
      // Declaring drawing a rectangle using the input brush to fill up the visual
      context.DrawRectangle(drawingBrush, null, new Rect(0, 0, size, size));
    }

    // Actually rendering the bitmap
    rtb.Render(drawingVisual);
    return rtb;
  }

  public void SettingsUpdated(FlameGraphSettings settings) {
    settings_ = settings;
    ReloadSettings();

    // Use potentially new node style.
    SetupNode(flameGraph_.RootNode);
    RedrawGraph();
  }

  public DrawingVisual Setup() {
    graphVisual_ = new DrawingVisual();

    if (flameGraph_.RootNode != null) {
      SetupNode(flameGraph_.RootNode);
    }

    glyphs_ = new GlyphRunCache(font_, fontSize_, VisualTreeHelper.GetDpi(graphVisual_).PixelsPerDip);
    nameGlyphs_ = new GlyphRunCache(nameFont_, fontSize_, VisualTreeHelper.GetDpi(graphVisual_).PixelsPerDip);

    RedrawGraph();
    graphVisual_.Drawing?.Freeze();
    return graphVisual_;
  }

  public HighlightingStyle GetNodeStyle(FlameGraphNode node) {
    var palette = node.HasFunction ? palettes_[node.CallTreeNode.Kind] : palettes_[ProfileCallTreeNodeKind.Unset];
    int colorIndex = node.Depth % palette.Count;
    var backColor = palette.PickBrush(palette.Count - colorIndex - 1);

    // Override color based on module name.
    if (!string.IsNullOrEmpty(node.ModuleName)) {
      if (settings_.UseModuleColors &&
          settings_.GetModuleColor(node.ModuleName, out var moduleColor)) {
        backColor = moduleColor.AsBrush();
      }
      else if (settings_.UseAutoModuleColors) {
        // Use a color based on the module name.
        uint hash = (uint)node.ModuleName.GetStableHashCode();
        backColor = ColorUtils.GenerateLightPastelBrush(hash);
      }
    }

    return new HighlightingStyle(backColor, defaultBorder_);
  }

  public void Redraw() {
    RedrawGraph(false);
  }

  public void UpdateMaxWidth(double maxWidth) {
    prevMaxWidth_ = maxWidth_;
    maxWidth_ = Math.Max(maxWidth, 1);
    RedrawGraph();
    prevMaxWidth_ = maxWidth_;
  }

  public void UpdateVisibleArea(Rect visibleArea) {
    visibleArea_ = visibleArea;
    RedrawGraph(false);
  }

  public FlameGraphNode HitTestNode(Point point) {
    var queryRect = new Rect(point.X / maxWidth_, point.Y, 1.0 / maxWidth_, 1);

    if (nodesQuadTree_ != null) {
      var nodes = nodesQuadTree_.GetNodesInside(queryRect);

      foreach (var node in nodes) {
        return node;
      }
    }

    if (dummyNodesQuadTree_ != null) {
      var nodes = dummyNodesQuadTree_.GetNodesInside(queryRect);

      foreach (var node in nodes) {
        return node;
      }
    }

    return null;
  }

  public Rect ComputeNodeBounds(FlameGraphNode node) {
    return new Rect(node.Bounds.Left * maxWidth_, node.Bounds.Top, node.Bounds.Width * maxWidth_, node.Bounds.Height);
  }

  public void DrawNode(FlameGraphNode node, DrawingContext dc, bool issueDraw = true) {
    // Mark the node as part of this rendering step.
    node.RenderVersion = renderVersion_;

    if (node.IsDummyNode) {
      return;
    }

    var bounds = new Rect(node.Bounds.Left * maxWidth_,
                          node.Bounds.Top,
                          node.Bounds.Width * maxWidth_,
                          node.Bounds.Height);

    if (bounds.Width <= 1) {
      return;
    }

    if (cachedNodeGuidelines_ == null) {
      cachedNodeGuidelines_ = CreateGuidelineSet(bounds, 0.5f);
    }

    if (issueDraw) {
      dc.PushGuidelineSet(cachedNodeGuidelines_);
      dc.DrawRectangle(node.Style.BackColor, node.Style.Border, bounds);
    }

    // Draw each text part of the node (module, func name, percentage, time).
    int index = 0;
    bool trimmed = false;
    bool alignedWithParent = false;
    bool setPercentagePosition = false;
    double margin = FlameGraphNode.DefaultMargin;

    // Start the text in the visible area.
    bool startsInView = bounds.Left >= visibleArea_.Left;
    double offsetY = bounds.Top + 1;
    double offsetX = startsInView ? bounds.Left : visibleArea_.Left;
    double maxWidth = bounds.Width - 2 * FlameGraphNode.DefaultMargin;

    if (!startsInView) {
      maxWidth -= visibleArea_.Left - bounds.Left;
    }

    // Compute layout and render each text part.
    while (maxWidth > 8 && index < FlameGraphNode.MaxTextParts && !trimmed) {
      string label = "";
      bool useNameFont = false;
      var textColor = node.TextColor;

      void TryAlignTextWithParentNode() {
        if (alignedWithParent || node.Parent == null) {
          return;
        }

        // If the parent node is outside the view and was not rendered,
        // force a pass that only computes the coordinates, without actual
        // rendering. This is recursive and over the entire flame graph
        // will visit each node a single time.
        if (node.Parent.RenderVersion != renderVersion_) {
          DrawNode(node.Parent, dc, false);
        }

        // If the position of text after the function name is close enough
        // to the one in the parent and text will not get trimmed
        // because of it, use it to align the percentage/time text.
        double offsetDiff = node.Parent.PercentageTextPosition - offsetX;

        if (offsetDiff > 0 && offsetDiff < 150) {
          // Align by matching the parent offset.
          double availableWidth = maxWidth - margin - offsetDiff;

          if (availableWidth > 0) {
            (_, _, bool textTrimmed, var size) =
              TrimTextToWidth(label, availableWidth, useNameFont);

            if (!textTrimmed && size.Width < maxWidth) {
              offsetX = node.Parent.PercentageTextPosition - margin;
              maxWidth -= offsetDiff;
            }
          }
        }
        else if (offsetDiff < 0 && Math.Abs(offsetDiff) < (margin * 0.5)) {
          // Align by reducing the margin from previous text
          // to match the parent offset.
          margin += offsetDiff;
        }

        alignedWithParent = true;
      }

      switch (index) {
        case 0: {
          if (node.HasFunction) {
            label = node.FunctionName;

            if (settings_.PrependModuleToFunction) {
              string moduleLabel = node.ModuleName + "!";
              (string modText, var modGlyphs, bool modTextTrimmed, var modTextSize) =
                TrimTextToWidth(moduleLabel, maxWidth - margin, false);

              if (modText.Length > 0 && issueDraw) {
                DrawText(modGlyphs, bounds, node.ModuleTextColor, offsetX + margin, offsetY, modTextSize, dc);
              }

              maxWidth -= modTextSize.Width + 1;
              offsetX += modTextSize.Width + 1;
            }
          }
          else {
            label = "All";
          }

          margin = FlameGraphNode.DefaultMargin;
          useNameFont = true;
          break;
        }
        case 1: {
          if (settings_.AppendPercentageToFunction) {
            label = flameGraph_.ScaleWeight(node).AsPercentageString();
            margin = FlameGraphNode.ExtraValueMargin;
            textColor = node.PercentageTextColor;
            useNameFont = true;
            TryAlignTextWithParentNode();
          }

          break;
        }
        case 2: {
          if (settings_.AppendDurationToFunction) {
            label = settings_.AppendPercentageToFunction ?
              $"({node.Weight.AsMillisecondsString()})" :
              $"{node.Weight.AsMillisecondsString()}";
            margin = FlameGraphNode.DefaultMargin;
            textColor = node.WeightTextColor;
            TryAlignTextWithParentNode();
          }

          break;
        }
        default: {
          throw new InvalidOperationException();
        }
      }

      // Trim the text to fit into the remaining space and draw it.
      (string text, var glyphs, bool textTrimmed, var textSize) =
        TrimTextToWidth(label, maxWidth - margin, useNameFont);

      if (text.Length > 0) {
        if (issueDraw) {
          if (index == 0 && node.SearchResult.HasValue) {
            DrawSearchResultSelection(node.SearchResult.Value, text, glyphs, bounds, offsetX + margin, offsetY, dc);
          }

          DrawText(glyphs, bounds, textColor, offsetX + margin, offsetY, textSize, dc);
        }

        // Remember starting position after function name
        // to be used for aligning the same text in descendants.
        if (index > 0 && !setPercentagePosition) {
          node.PercentageTextPosition = offsetX + margin;
          setPercentagePosition = true;
        }
      }

      trimmed = textTrimmed;
      maxWidth -= textSize.Width + margin;
      offsetX += textSize.Width + margin;
      index++;
    }

    if (issueDraw) {
      dc.Pop(); // PushGuidelineSet
    }
  }

  private void UpdateGraphSizes() {
    minVisibleRectWidth_ = FlameGraphNode.MinVisibleRectWidth / maxWidth_;
    quadGraphArea_ = new Rect(0, 0, 1.0, MaxGraphHeight);
    quadVisibleArea_ = new Rect(visibleArea_.Left / maxWidth_, visibleArea_.Top,
                                visibleArea_.Width / maxWidth_, visibleArea_.Height);
  }

  private void SetupNode(FlameGraphNode node) {
    node.Style = GetNodeStyle(node);
    node.TextColor = nodeTextBrush_;
    node.ModuleTextColor = nodeModuleBrush_;
    node.WeightTextColor = nodeWeightBrush_;
    node.PercentageTextColor = nodePercentageBrush_;
    node.Owner = this;

    if (node.Children != null) {
      foreach (var childNode in node.Children) {
        SetupNode(childNode);
      }
    }
  }

  private void RedrawGraph(bool updateLayout = true) {
    if (!RedrawGraphImpl(updateLayout)) {
      RedrawGraphImpl(true); // Force layout update.
    }
  }

  private bool RedrawGraphImpl(bool updateLayout = true) {
    using var graphDC = graphVisual_.RenderOpen();
    bool nodeLayoutRecomputed = false;
    UpdateGraphSizes();

    if (updateLayout) {
      // Recompute the position of all nodes and rebuild the quad tree.
      // This is done once, with node position/size being relative to the maxWidth,
      // except when dummy nodes get involved, which can force a re-layout.
        nodesQuadTree_ = new QuadTree<FlameGraphNode>();
        dummyNodesQuadTree_ = new QuadTree<FlameGraphGroupNode>();
        nodesQuadTree_.Bounds = quadGraphArea_;
        dummyNodesQuadTree_.Bounds = quadGraphArea_;
        maxNodeDepth_ = 0;

        UpdateNodeLayout(flameGraph_.RootNode, 0, 0, true);
        nodeLayoutRecomputed = true;
    }

    // Update only the visible nodes on scrolling.
    bool layoutChanged = !nodeLayoutRecomputed && Math.Abs(maxWidth_ - prevMaxWidth_) > double.Epsilon;
    int shrinkingNodes = 0;
    renderVersion_++; // Mark a new rendering pass.

    foreach (var node in nodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
      DrawNode(node, graphDC);

      if (layoutChanged && ScaleNode(node) < minVisibleRectWidth_) {
        shrinkingNodes++;
      }
    }

    if (shrinkingNodes > 0) {
      //? TODO: Removing from the quad tree is very slow,
      //? recompute the entire layout instead...
      //! Consider a faster implementation or other kind of spatial tree.
      return false;
    }

    // Redraw selected nodes to show on top.
    DrawSelectedNodes(graphDC);
    return DrawDummyNodes(graphDC, layoutChanged);
  }

  private void DrawSelectedNodes(DrawingContext graphDC) {
    foreach (var node in SelectedNodes.Keys) {
      // When a different root node is used, the selected nodes
      // may not be part of the view, don't draw them in that case.
      bool onPath = false;
      var pathNode = node;

      while (pathNode != null) {
        if(pathNode == flameGraph_.RootNode) {
          onPath = true;
          break;
        }
        else if (pathNode.IsHidden) {
          // If node is collapsed itself or a descendant
          // of a collapsed node, don't select it since it's not visible.
          break;
        }

        pathNode = pathNode.Parent;
      }

      if(onPath) {
        DrawNode(node, graphDC);
      }
    }
  }

  private double ScaleNode(FlameGraphNode node) {
    if (isTimeline_) {
      return flameGraph_.ScaleDuration(node);
    }

    return flameGraph_.ScaleWeight(node);
  }

  private bool DrawDummyNodes(DrawingContext graphDC, bool layoutChanged) {
    int shrinkingNodes = 0;
    int growingNodes = 0;

    foreach (var node in dummyNodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
      double width = ScaleNode(node);

      if (width < minVisibleRectWidth_ * 0.5) {
        shrinkingNodes++;
        continue;
      }

      // Reconsider replacing the dummy node.
      if (layoutChanged && ScaleNode(node) > minVisibleRectWidth_ * 0.5) {
        growingNodes++;
      }
      else {
        DrawDummyNode(node, graphDC);
      }
    }

    if (growingNodes == 0 && shrinkingNodes == 0) {
      return true;
    }

    // Replace/split the enlarged dummy nodes.
    //? TODO: Still doesn't always expand like recomputing layout does
#if false
        bool update = false;

        foreach (var node in enlargeList) {
            if (node.Parent == null) {
                continue;
            }

            dummyNodesQuadTree_.Remove(node);

            // The dummy node may be recreated back, don't update in that case.
            if (UpdateChildrenNodeLayout(node.Parent, node.Parent.Bounds.Left,
                    node.Parent.Bounds.Top,
                    node.ReplacedStartIndex,
                    node.ReplacedEndIndex)) {
                update = true;
            }
        }

        // Redraw to show the newly create nodes replacing the dummy ones.
        if (true || update) {
            foreach (var node in nodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
                DrawNode(node, graphDC);
            }

            foreach (var node in dummyNodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
                DrawDummyNode(node, graphDC);
            }
        }
#else

    return false;
#endif
  }

  private void DrawDummyNode(FlameGraphGroupNode node, DrawingContext graphDC) {
    var scaledBounds = new Rect(node.Bounds.Left * maxWidth_, node.Bounds.Top,
                                node.Bounds.Width * maxWidth_, node.Bounds.Height);

    if (cachedDummyNodeGuidelines_ == null) {
      cachedDummyNodeGuidelines_ = CreateGuidelineSet(scaledBounds, 0.5f);
    }

    graphDC.PushGuidelineSet(cachedDummyNodeGuidelines_);
    graphDC.DrawRectangle(node.Style.BackColor, node.Style.Border, scaledBounds);
    graphDC.DrawRectangle(CreatePlaceholderTiledBrush(8), null, scaledBounds);
    graphDC.Pop();
  }

  private void DrawCenteredText(string text, double x, double y, DrawingContext dc) {
    var glyphInfo = glyphs_.GetGlyphs(text);
    x = Math.Max(0, x - glyphInfo.TextWidth / 2);
    y = Math.Max(0, y + glyphInfo.TextHeight / 2);

    dc.PushTransform(new TranslateTransform(x, y));
    dc.DrawGlyphRun(Brushes.Black, glyphInfo.Glyphs);
    dc.Pop();
  }

  private void UpdateNodeLayout(FlameGraphNode node, double x, double y, bool redraw) {
    double width = ScaleNode(node);

    if (isTimeline_) {
      x = flameGraph_.ScaleStartTime(node);
    }

    var prevBounds = node.Bounds;
    node.Bounds = new Rect(x, y, width, nodeHeight_);
    node.IsDummyNode = !redraw;
    node.IsHidden = false;

    if (redraw) {
      maxNodeDepth_ = Math.Max(node.Depth, maxNodeDepth_);
      double minWidth = minVisibleRectWidth_;
      if (isTimeline_) minWidth *= 0.1; // Add more nodes to be zoomed in.

      if (node.Bounds.Width > minWidth) {
        nodesQuadTree_.Insert(node, node.Bounds);
      }
    }

    if (node.Children == null || node.Children.Count == 0) {
      return;
    }

    // Children are sorted by weight or time.
    UpdateChildrenNodeLayout(node, x, y);
  }

  private bool UpdateChildrenNodeLayout(FlameGraphNode node, double x, double y,
                                        int startIndex = 0, int stopIndex = int.MaxValue) {
    int skippedChildren = 0;
    int totalSkippedChildren = 0;

    for (int i = 0; i < startIndex; i++) {
      var childNode = node.Children[i];
      double childWidth = flameGraph_.ScaleWeight(childNode);
      x += childWidth;
    }

    stopIndex = Math.Min(stopIndex, node.Children.Count);
    int range = stopIndex - startIndex;

    for (int i = startIndex; i < stopIndex; i++) {
      //? If multiple children below width, single patterned rect
      var childNode = node.Children[i];
      double childWidth = flameGraph_.ScaleWeight(childNode);
      FlameGraphNode dummyNode = null;

      if (skippedChildren == 0) {
        if (childWidth < minVisibleRectWidth_) {
          childNode.IsDummyNode = true;
          dummyNode = CreateSmallWeightDummyNode(node, x, y, i, stopIndex, out skippedChildren);
          totalSkippedChildren += skippedChildren;

          //? TODO: When called from enlarged dummy node,
          //? if all children part of new (same) dummy node skip going recursively
          //? since they are also too small and will not be displayed.
        }
      }
      else {
        childNode.IsDummyNode = true;
      }

      // If all child nodes were merged into a dummy node don't recurse.
      if (totalSkippedChildren < range) {
        UpdateNodeLayout(childNode, x, y + nodeHeight_, skippedChildren == 0);
      }

      x += childWidth;
    }

    return totalSkippedChildren < range;
  }

  private FlameGraphNode CreateSmallWeightDummyNode(FlameGraphNode node, double x, double y,
                                                    int startIndex, int stopIndex, out int skippedChildren) {
    var totalWeight = TimeSpan.Zero;
    double totalWidth = 0;

    // Collect all the child nodes that have a small weight
    // and replace them by one dummy node.
    var startTime = TimeSpan.MaxValue;
    var endTime = TimeSpan.MinValue;
    int k;

    for (k = startIndex; k < stopIndex; k++) {
      var childNode = node.Children[k];
      double childWidth = ScaleNode(childNode);

      // In case child sorting is not by weight, stop extending the range
      // when a larger one is found again.
      //? TODO: In timeline, nodes not placed properly
      if (childWidth > minVisibleRectWidth_) {
        break;
      }

      totalWidth += childWidth;
      totalWeight += childNode.Weight;
      startTime = TimeSpan.FromTicks(Math.Min(startTime.Ticks, childNode.StartTime.Ticks));
      endTime = TimeSpan.FromTicks(Math.Max(endTime.Ticks, childNode.EndTime.Ticks));
      childNode.IsHidden = true;
    }

    skippedChildren = k - startIndex; // Caller should ignore these children.

    if (totalWidth < minVisibleRectWidth_) {
      return null; // Nothing to draw.
    }

    if (isTimeline_) {
      x = flameGraph_.ScaleStartTime(startTime);
    }

    //? TODO: Use a pool for FlameGraphGroupNode instead of new (JIT_New dominates)
    var replacement = new Rect(x, y + nodeHeight_, totalWidth, nodeHeight_);
    var dummyNode = new FlameGraphGroupNode(node, startIndex, skippedChildren, totalWeight, node.Depth);
    dummyNode.IsDummyNode = true;
    dummyNode.Bounds = replacement;
    dummyNode.Style = PickDummyNodeStyle(node.Style);
    dummyNode.StartTime = startTime;
    dummyNode.EndTime = endTime;
    dummyNodesQuadTree_.Insert(dummyNode, replacement);

    //? Could make color darker than level, or less saturated better
    //! TODO: Make a fake node that has details (sum of weights, tooltip with child count, etc)
    return dummyNode;
  }

  private HighlightingStyle PickDummyNodeStyle(HighlightingStyle style) {
    if (!dummyNodeStyles_.TryGetValue(style, out var dummyStyle)) {
      var newColor = ColorUtils.AdjustSaturation(((SolidColorBrush)style.BackColor).Color, 0.2f);
      newColor = ColorUtils.AdjustLight(newColor, 2.0f);
      dummyStyle = new HighlightingStyle(newColor, defaultBorder_);
      dummyNodeStyles_[style] = dummyStyle;
    }

    return dummyStyle;
  }

  private Brush CreatePlaceholderTiledBrush(double tileSize) {
    // Create the brush once, freeze and reuse it everywhere.
    if (placeholderTileBrush_ != null) {
      return placeholderTileBrush_;
    }

    tileSize = Math.Ceiling(tileSize);
    var line = new LineSegment(new Point(0, 0), true);
    line.IsSmoothJoin = false;
    line.Freeze();
    var figure = new PathFigure();
    figure.IsClosed = false;
    figure.StartPoint = new Point(tileSize, tileSize);
    figure.Segments.Add(line);
    figure.Freeze();
    var geometry = new PathGeometry();
    geometry.Figures.Add(figure);
    geometry.Freeze();
    var drawing = new GeometryDrawing();
    drawing.Geometry = geometry;

    var penBrush = ColorBrushes.GetBrush(Colors.Gray);
    drawing.Pen = new Pen(penBrush, 1.0f);
    drawing.Freeze();
    var brush = new DrawingBrush();
    brush.Drawing = drawing;
    brush.Stretch = Stretch.None;
    brush.TileMode = TileMode.Tile;
    brush.Viewbox = new Rect(0, 0, tileSize, tileSize);
    brush.ViewboxUnits = BrushMappingMode.Absolute;
    brush.Viewport = new Rect(0, 0, tileSize, tileSize);
    brush.ViewportUnits = BrushMappingMode.Absolute;
    RenderOptions.SetCachingHint(brush, CachingHint.Cache);
    brush.Freeze();
    placeholderTileBrush_ = brush;
    return placeholderTileBrush_;
  }

  private void DrawSearchResultSelection(TextSearchResult searchResult, string text, GlyphRun glyphs,
                                         Rect bounds, double offsetX, double offsetY, DrawingContext dc) {
    var glypWidths = glyphs.AdvanceWidths;
    int startIndex = Math.Min(searchResult.Offset, glypWidths.Count);
    int endIndex = Math.Min(searchResult.Offset + searchResult.Length, glypWidths.Count);

    if (startIndex == text.Length || startIndex == endIndex) {
      return; // Result outside of visible text part.
    }

    for (int i = 0; i < startIndex; i++) {
      offsetX += glypWidths[i];
    }

    double width = 0;

    for (int i = startIndex; i < endIndex; i++) {
      width += glypWidths[i];
    }

    var selectionBounds = new Rect(offsetX, offsetY, width, nodeHeight_ - 2);
    dc.DrawRectangle(searchResultMarkingBrush_, null, selectionBounds);
  }

  private void DrawText(GlyphRun glyphs, Rect bounds, Brush textColor, double offsetX, double offsetY,
                        Size textSize, DrawingContext dc) {
    double x = offsetX;
    double y = bounds.Height / 2 + textSize.Height / 4 + offsetY;

    if (cachedTextGuidelines_ == null) {
      var rect = glyphs.ComputeAlignmentBox();
      cachedTextGuidelines_ = CreateGuidelineSet(rect, 1.0f);
    }

    dc.PushTransform(new TranslateTransform(x, y));
    dc.PushGuidelineSet(cachedTextGuidelines_);
    dc.DrawGlyphRun(textColor, glyphs);
    dc.Pop();
    dc.Pop(); // PushGuidelineSet
  }

  private (string Text, GlyphRun glyphs, bool Trimmed, Size TextSize)
    TrimTextToWidth(string text, double maxWidth, bool useNameFont) {
    string originalText = text;
    var glyphsCache = useNameFont ? NameGlyphsCache : GlyphsCache;

    if (maxWidth <= 0 || string.IsNullOrEmpty(text)) {
      return ("", glyphsCache.GetGlyphs("").Glyphs, false, new Size(0, 0));
    }

    var glyphInfo = glyphsCache.GetGlyphs(text, maxWidth);
    bool trimmed = glyphInfo.IsTrimmed;

    if (glyphInfo.TextWidth > maxWidth) {
      // The width of letters is the same only for monospace fonts,
      // use the glyph width to find where to trim the string instead.
      double trimmedWidth = 0;
      int trimmedLength = 0;
      var widths = glyphInfo.Glyphs.AdvanceWidths;

      double letterWidth = Math.Ceiling(glyphInfo.TextWidth / text.Length);
      double availableWidth = maxWidth - letterWidth * 2; // Space for ..

      for (trimmedLength = 0; trimmedLength < text.Length; trimmedLength++) {
        trimmedWidth += widths[trimmedLength];

        if (trimmedWidth >= availableWidth) {
          break;
        }
      }

      trimmed = true;

      if (trimmedLength > 0) {
        text = text.Substring(0, trimmedLength) + "..";
        glyphInfo = glyphsCache.GetGlyphs(text, maxWidth);
      }
      else {
        text = "";
        glyphInfo = glyphsCache.GetGlyphs(text, maxWidth);
        glyphInfo.TextWidth = maxWidth;
      }
    }

    glyphInfo.IsTrimmed = trimmed;
    glyphsCache.CacheGlyphs(glyphInfo, originalText, maxWidth);

    return (text, glyphInfo.Glyphs, trimmed,
      new Size(glyphInfo.TextWidth, glyphInfo.TextHeight));
  }

  private GuidelineSet CreateGuidelineSet(Rect rect, double penWidth) {
    var guidelines = new GuidelineSet();
    guidelines.GuidelinesX.Add(rect.Left + penWidth);
    guidelines.GuidelinesX.Add(rect.Right + penWidth);
    guidelines.GuidelinesY.Add(rect.Top + penWidth);
    guidelines.GuidelinesY.Add(rect.Bottom + penWidth);
    return guidelines;
  }
}