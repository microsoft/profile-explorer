using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace IRExplorerUI.Profile;

public class FlameGraphRenderer {
    internal const double DefaultTextSize = 12;
    internal const double DefaultNodeHeight = 18;
    internal const double TimeBarHeight = 24;

    private FlameGraph flameGraph_;
    private int maxNodeDepth_;
    private double nodeHeight_;
    private double maxWidth_;
    private double minVisibleRectWidth_;
    private bool nodeLayoutComputed_;
    private Rect visibleArea_;
    private Rect quadVisibleArea_;
    private Rect quadGraphArea_;

    private Typeface font_;
    private Typeface nameFont_;
    private double fontSize_;
    private Dictionary<ProfileCallTreeNodeKind, ColorPalette> palettes_;
    private Pen defaultBorder_;
    private DrawingBrush placeholderTileBrush_;
    private DrawingVisual graphVisual_;
    private GlyphRunCache glyphs_;
    private GlyphRunCache nameGlyphs_;
    private QuadTree<FlameGraphNode> nodesQuadTree_;
    private QuadTree<FlameGraphGroupNode> dummyNodesQuadTree_;

    public GlyphRunCache GlyphsCache => glyphs_;
    public GlyphRunCache NameGlyphsCache => nameGlyphs_;

    public double MaxGraphWidth => maxWidth_;
    public double MaxGraphHeight => (maxNodeDepth_ + 1) * nodeHeight_;
    public Rect VisibleArea => visibleArea_;
    public Rect GraphArea => new Rect(0, 0, MaxGraphWidth, MaxGraphHeight);

    private void UpdateGraphSizes() {
        minVisibleRectWidth_ = FlameGraphNode.MinVisibleRectWidth / maxWidth_;
        quadGraphArea_ = new Rect(0, 0, 1.0, MaxGraphHeight);
        quadVisibleArea_ = new Rect(visibleArea_.Left / maxWidth_, visibleArea_.Top,
            visibleArea_.Width / maxWidth_, visibleArea_.Height);
    }

    public FlameGraphRenderer(FlameGraph flameGraph, Rect visibleArea) {
        flameGraph_ = flameGraph;
        maxWidth_ = visibleArea.Width;
        visibleArea_ = visibleArea;
        UpdateGraphSizes();

        palettes_ = new Dictionary<ProfileCallTreeNodeKind, ColorPalette>();
        palettes_[ProfileCallTreeNodeKind.Unset] = ColorPalette.Profile;
        palettes_[ProfileCallTreeNodeKind.NativeUser] = ColorPalette.Profile;
        palettes_[ProfileCallTreeNodeKind.NativeKernel] = ColorPalette.HeatMap;
        palettes_[ProfileCallTreeNodeKind.Managed] = ColorPalette.Profile;

        defaultBorder_ = ColorPens.GetPen(Colors.Black, 0.5);
        nodeHeight_ = DefaultNodeHeight; //? TODO: Option
        font_ = new Typeface("Segoe UI");
        nameFont_ = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretch.FromOpenTypeStretch(5));
        fontSize_ = DefaultTextSize;
    }

    public DrawingVisual Setup() {
        graphVisual_ = new DrawingVisual();
        SetupNode(flameGraph_.RootNode);
        glyphs_ = new GlyphRunCache(font_, fontSize_, VisualTreeHelper.GetDpi(graphVisual_).PixelsPerDip);
        nameGlyphs_ = new GlyphRunCache(nameFont_, fontSize_, VisualTreeHelper.GetDpi(graphVisual_).PixelsPerDip);

        RedrawGraph();
        graphVisual_.Drawing?.Freeze();
        return graphVisual_;
    }

    private void SetupNode(FlameGraphNode node) {
        //? TODO: Palette based on module
        //? int colorIndex = Math.Min(node.Depth, palettes_.Count - 1);
        var palette = node.CallTreeNode != null ? palettes_[node.CallTreeNode.Kind] : palettes_[ProfileCallTreeNodeKind.Unset];

        int colorIndex = node.Depth % palette.Count;
        var backColor = palette[palette.Count - colorIndex - 1];
        node.Style = new HighlightingStyle(backColor, defaultBorder_);
        node.TextColor = Brushes.DarkBlue;
        node.ModuleTextColor = Brushes.DimGray;
        node.WeightTextColor = Brushes.Maroon;
        node.Owner = this;

        if (node.Children != null) {
            foreach (var childNode in node.Children) {
                SetupNode(childNode);
            }
        }
    }

    public void Redraw() {
        RedrawGraph(false);
    }

    public void UpdateMaxWidth(double maxWidth) {
        maxWidth_ = Math.Max(maxWidth, 1);
        RedrawGraph();
    }

    public void UpdateVisibleArea(Rect visibleArea) {
        visibleArea_ = visibleArea;
        RedrawGraph(false);
    }

    public double ScaleWeight(TimeSpan weight) {
        return flameGraph_.ScaleWeight(weight);
    }

    private void RedrawGraph(bool updateLayout = true) {
        if(!RedrawGraphImpl(updateLayout)) {
            nodeLayoutComputed_ = false;
            RedrawGraphImpl(true);
        }
    }

    private bool RedrawGraphImpl(bool updateLayout = true) {
        using var graphDC = graphVisual_.RenderOpen();
        var nodeLayoutRecomputed = false;
        UpdateGraphSizes();

        if (updateLayout) {
            // Recompute the position of all nodes and rebuild the quad tree.
            if (!nodeLayoutComputed_) {
                nodesQuadTree_ = new QuadTree<FlameGraphNode>();
                dummyNodesQuadTree_ = new QuadTree<FlameGraphGroupNode>();
                nodesQuadTree_.Bounds = quadGraphArea_;
                dummyNodesQuadTree_.Bounds = quadGraphArea_;
                maxNodeDepth_ = 0;

                UpdateNodeLayout(flameGraph_.RootNode, 0, 0, true);
                nodeLayoutComputed_ = true;
                nodeLayoutRecomputed = true;
            }
        }

        // Update only the visible nodes on scrolling.
        int shrinkingNodes = 0;

#if true
        foreach (var node in nodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
            node.Owner.DrawNode(node, graphDC);
            if (!nodeLayoutRecomputed && !node.IsDummyNode &&
                node.Bounds.Width < minVisibleRectWidth_) {
                shrinkingNodes++;
            }
        }

        if (shrinkingNodes > 0) {
            //? TODO: Removing from the quad tree is very slow,
            //? recompute the entire layout instead...
            //! Consider a faster implementation or other kind of spatial tree.
            Trace.WriteLine($"=> Shrinking {shrinkingNodes}");
            return false;
        }
#else
        foreach (var node in nodesQuadTree_.table.Keys) {
            node.Owner.DrawNode(node, graphDC);
        }

#endif
        DrawDummyNodes(graphDC, nodeLayoutRecomputed);
        //DrawTimeBar(graphDC);
        return true;
    }

    private void DrawDummyNodes(DrawingContext graphDC, bool nodeLayoutRecomputed) {
        List<FlameGraphGroupNode> enlargeList = null;

        foreach (var node in dummyNodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
            // Reconsider replacing the dummy node.
            if (!nodeLayoutRecomputed &&
                flameGraph_.ScaleWeight(node.Weight) > 5*minVisibleRectWidth_) {
                enlargeList ??= new List<FlameGraphGroupNode>();
                enlargeList.Add(node);
            }
            else {
                DrawDummyNode(node, graphDC);
            }
        }

        if (enlargeList == null) {
            return;
        }

        // Replace/split the enlarged dummy nodes.
#if true
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
        nodeLayoutComputed_ = false;
        RedrawGraph();
        return;
#endif
    }

    private void DrawDummyNodesTimeline(DrawingContext graphDC, bool nodeLayoutRecomputed) {
        List<FlameGraphGroupNode> enlargeList = null;

        foreach (var node in dummyNodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
            // Reconsider replacing the dummy node.
            if (!nodeLayoutRecomputed &&
                flameGraph_.ScaleDuration(node.StartTime, node.EndTime) > 5*minVisibleRectWidth_ ) {
                enlargeList ??= new List<FlameGraphGroupNode>();
                enlargeList.Add(node);
            }
            else {
                DrawDummyNode(node, graphDC);
            }
        }

        if (enlargeList == null) {
            return;
        }

        // Replace/split the enlarged dummy nodes.
#if true
        bool update = false;

        foreach (var node in enlargeList) {
            if (node.Parent == null) {
                continue;
            }

            dummyNodesQuadTree_.Remove(node);

            if (flameGraph_.ScaleDuration(node.StartTime, node.EndTime) > minVisibleRectWidth_) {
                // The dummy node may be recreated back, don't update in that case.
                if (UpdateChildrenNodeLayoutTimeline(node.Parent, node.Parent.Bounds.Left,
                        node.Parent.Bounds.Top,
                        node.ReplacedStartIndex,
                        node.ReplacedEndIndex)) {
                    update = true;
                }
            }
        }

        // Redraw to show the newly create nodes replacing the dummy ones.
        if (update) {
            foreach (var node in nodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
                DrawNode(node, graphDC);
            }

            foreach (var node in dummyNodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
                DrawDummyNode(node, graphDC);
            }
        }
#else
        nodeLayoutComputed_ = false;
        RedrawGraph();
        return;
#endif
    }

    private void DrawDummyNode(FlameGraphGroupNode node, DrawingContext graphDC) {
        double estimatedDepth = Math.Max(1, 1 + Math.Log10(node.MaxDepthUnder));
        var scaledBounds = new Rect(node.Bounds.Left * maxWidth_, node.Bounds.Top + timeBarHeight_,
            node.Bounds.Width * maxWidth_, node.Bounds.Height * estimatedDepth);

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
        double width;

        if (false /* timeline */) {
            x = flameGraph_.ScaleStartTime(node.StartTime);
            width = flameGraph_.ScaleDuration(node.StartTime, node.EndTime);
        }
        else {
            width = flameGraph_.ScaleWeight(node.Weight);
        }

        //Trace.WriteLine($"Node at {x}, width {width}");

        var prevBounds = node.Bounds;
        node.Bounds = new Rect(x, y, width, nodeHeight_);

        //node.Bounds = Utils.SnapToPixels(node.Bounds);
        node.IsDummyNode = !redraw;

        if (redraw) {
            maxNodeDepth_ = Math.Max(node.Depth, maxNodeDepth_);

            if (node.Bounds.Width >= minVisibleRectWidth_) {
                nodesQuadTree_.Insert(node, node.Bounds);
            }
        }

        if (node.Children == null) {
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
            var childWidth = flameGraph_.ScaleWeight(childNode.Weight);
            x += childWidth;
        }

        stopIndex = Math.Min(stopIndex, node.Children.Count);
        int range = stopIndex - startIndex;

        for (int i = startIndex; i < stopIndex; i++) {
            //? If multiple children below width, single patterned rect
            var childNode = node.Children[i];
            var childWidth = flameGraph_.ScaleWeight(childNode.Weight);
            FlameGraphNode dummyNode = null;

            if (skippedChildren == 0) {
                 if (childWidth < minVisibleRectWidth_) {
                     childNode.IsDummyNode = true;
                     dummyNode = CreateSmallWeightDummyNode(node, x, y, i, stopIndex, out skippedChildren);
                     totalSkippedChildren += skippedChildren;

                    //? TODO: When called from enlarged dummy node,
                    //? if all children part of new (same) dummy node skip going recursively.
                 }
            }
            else {
                childNode.IsDummyNode = true;
            }

            // If all child nodes were merged into a dummy node don't recurse.
            if (totalSkippedChildren < range) {
                UpdateNodeLayout(childNode, x, y + nodeHeight_, skippedChildren == 0);
            }

            childNode.MaxDepthUnder = maxNodeDepth_ - childNode.Depth;

            if (dummyNode != null) {
                dummyNode.MaxDepthUnder = maxNodeDepth_ - dummyNode.Depth;
            }

            x += childWidth;

            if (skippedChildren > 0) {
                childNode.IsDummyNode = true;
                skippedChildren--;
            }
        }

        node.MaxDepthUnder = maxNodeDepth_ - node.Depth;
        return totalSkippedChildren < range;
    }

     private FlameGraphNode CreateSmallWeightDummyNode(FlameGraphNode node, double x, double y,
        int startIndex, int stopIndex, out int skippedChildren) {
        TimeSpan totalWeight = TimeSpan.Zero;
        double totalWidth = 0;

        // Collect all the child nodes that have a small weight
        // and replace them by one dummy node.
        TimeSpan startTime = TimeSpan.MaxValue;
        TimeSpan endTime = TimeSpan.MinValue;
        int k;

        for (k = startIndex; k < stopIndex; k++) {
            var childNode = node.Children[k];
            double childWidth = flameGraph_.ScaleWeight(childNode.Weight);

            // In case child sorting is not by weight, stop extending the range
            // when a larger one is found again.
            if (childWidth > minVisibleRectWidth_) {
                break;
            }

            totalWidth += childWidth;
            totalWeight += childNode.Weight;
            startTime = TimeSpan.FromTicks(Math.Min(startTime.Ticks, childNode.StartTime.Ticks));
            endTime = TimeSpan.FromTicks(Math.Max(endTime.Ticks, childNode.EndTime.Ticks));
        }

        skippedChildren = k - startIndex; // Caller should ignore these children.

        if (totalWidth < minVisibleRectWidth_) {
            return null; // Nothing to draw.
        }

        var replacement = new Rect(x, y + nodeHeight_, totalWidth, nodeHeight_);
        var dummyNode = new FlameGraphGroupNode(node, startIndex, skippedChildren, totalWeight, node.Depth);
        dummyNode.IsDummyNode = true;
        dummyNode.Bounds = replacement;
        dummyNode.Style = PickDummyNodeStyle(node.Style);
        dummyNode.StartTime = startTime;
        dummyNode.EndTime = endTime;
        dummyNodesQuadTree_.Insert(dummyNode, replacement);

        //? Could make color darker than level, or less satureded  better
        //! TODO: Make a fake node that has details (sum of weights, tooltip with child count, etc)
        return dummyNode;
    }

    private void UpdateNodeLayoutTimeline(FlameGraphNode node, double x, double y, bool redraw) {
        double width;

        if (false /* timeline */) {
            x = flameGraph_.ScaleStartTime(node.StartTime);
            width = flameGraph_.ScaleDuration(node.StartTime, node.EndTime);
        }
        else {
            width = flameGraph_.ScaleWeight(node.Weight);
        }

        //Trace.Write(new  string(' ', node.Depth * 2 ));
        //Trace.WriteLine($"{node.CallTreeNode?.FunctionName}  | {node.Depth} | {node.Weight.TotalMilliseconds} | x = {x * maxWidth_} | w = {width * maxWidth_}");

        //redraw = true;
        var prevBounds = node.Bounds;
        node.Bounds = new Rect(x, y, width, nodeHeight_);

        //node.Bounds = Utils.SnapToPixels(node.Bounds);
        node.IsDummyNode = !redraw;

        //if (redraw) {
            maxNodeDepth_ = Math.Max(node.Depth, maxNodeDepth_);

            //if (node.Bounds.Width >= minVisibleRectWidth_) {
               // nodesQuadTree_.Insert(node, node.Bounds);
            //}

            if (node.Bounds.Width * maxWidth_ > 0.1) {
                nodesQuadTree_.Insert(node, node.Bounds);
            }
            else {
                node.IsDummyNode = true; //? Ignore node from enlarging checks.
            }
        //}

        if (node.Children == null) {
            return;
        }

        // Children are sorted by weight or time.
        UpdateChildrenNodeLayoutTimeline(node, x, y);

        // if (node.Depth < 1) {
        //     Trace.WriteLine("==============================================");
        // }
    }

    private bool UpdateChildrenNodeLayoutTimeline(FlameGraphNode node, double x, double y,
                                          int startIndex = 0, int stopIndex = int.MaxValue) {
        int skippedChildren = 0;
        int totalSkippedChildren = 0;

        for (int i = 0; i < startIndex; i++) {
            var childNode = node.Children[i];
            var childWidth = flameGraph_.ScaleDuration(childNode.StartTime, childNode.EndTime);
            x += childWidth;
        }

        stopIndex = Math.Min(stopIndex, node.Children.Count);

        for (int i = startIndex; i < stopIndex; i++) {
            //? If multiple children below width, single patterned rect
            var childNode = node.Children[i];
            var childWidth = flameGraph_.ScaleDuration(childNode.StartTime, childNode.EndTime);
            FlameGraphNode dummyNode = null;

            if (skippedChildren == 0) {
                 if (childWidth < minVisibleRectWidth_) {
                     childNode.IsDummyNode = true;
                     dummyNode = CreateSmallWeightDummyNodeTimeline(node, x, y, i, stopIndex, out skippedChildren);
                     totalSkippedChildren += skippedChildren;

                    //? TODO: When called from enlarged dummy node,
                    //? if all children part of new (same) dummy node skip going recursively.
                 }
            }
            else {
                childNode.IsDummyNode = true;
            }

            UpdateNodeLayoutTimeline(childNode, x, y + nodeHeight_, skippedChildren == 0);
            childNode.MaxDepthUnder = maxNodeDepth_ - childNode.Depth;

            if (dummyNode != null) {
                dummyNode.MaxDepthUnder = maxNodeDepth_ - dummyNode.Depth;
            }

            x += childWidth;

            if (skippedChildren > 0) {
                childNode.IsDummyNode = true;
                skippedChildren--;
            }
        }

        node.MaxDepthUnder = maxNodeDepth_ - node.Depth;
        return totalSkippedChildren < (stopIndex - startIndex);
    }

    private FlameGraphNode CreateSmallWeightDummyNodeTimeline(FlameGraphNode node, double x, double y,
        int startIndex, int stopIndex, out int skippedChildren) {
        TimeSpan totalWeight = TimeSpan.Zero;
        double totalWidth = 0;

        // Collect all the child nodes that have a small weight
        // and replace them by one dummy node.
        TimeSpan startTime = TimeSpan.MaxValue;
        TimeSpan endTime = TimeSpan.MinValue;
        int k;
        
        for (k = startIndex; k < stopIndex; k++) {
            var childNode = node.Children[k];
            double childWidth = flameGraph_.ScaleDuration(childNode.StartTime, childNode.EndTime);

            // In case child sorting is not by weight, stop extending the range
            // when a larger one is found again.
            if (childWidth > minVisibleRectWidth_) {
                break;
            }

            totalWidth += childWidth;
            totalWeight += childNode.Weight;
            startTime = TimeSpan.FromTicks(Math.Min(startTime.Ticks, childNode.StartTime.Ticks));
            endTime = TimeSpan.FromTicks(Math.Max(endTime.Ticks, childNode.EndTime.Ticks));
        }

        skippedChildren = k - startIndex; // Caller should ignore these children.

        if (totalWidth < minVisibleRectWidth_) {
            return null; // Nothing to draw.
        }

        var replacement = new Rect(x, y + nodeHeight_, totalWidth, nodeHeight_);
        var dummyNode = new FlameGraphGroupNode(node, startIndex, skippedChildren, totalWeight, node.Depth);
        dummyNode.IsDummyNode = true;
        dummyNode.Bounds = replacement;
        dummyNode.Style = PickDummyNodeStyle(node.Style);
        dummyNode.StartTime = startTime;
        dummyNode.EndTime = endTime;
        dummyNodesQuadTree_.Insert(dummyNode, replacement);

        //? Could make color darker than level, or less satureded  better
        //! TODO: Make a fake node that has details (sum of weights, tooltip with child count, etc)
        return dummyNode;
    }

    private void DrawTimeBar(DrawingContext graphDC) {
        const double MinTickDistance = 50;
        const double TextMarginY = 7;

        var bar = new Rect(visibleArea_.Left, visibleArea_.Top,
            visibleArea_.Width, timeBarHeight_);
        graphDC.DrawRectangle(Brushes.Bisque, null, bar);

        double secondTickDist = maxWidth_ / flameGraph_.RootNode.Duration.TotalSeconds;
        double msTickDist = maxWidth_ / flameGraph_.RootNode.Duration.TotalMilliseconds;
        int seconds = 0;

        for (double x = 0; x < maxWidth_; x += secondTickDist) {
            var tickRect = new Rect(x, visibleArea_.Top, 2, 4);
            graphDC.DrawRectangle(Brushes.Black, null, tickRect);
            DrawCenteredText($"{seconds} s", tickRect.Left, tickRect.Top + TextMarginY, graphDC);

            double subTicks = secondTickDist / MinTickDistance;
            double subTickDist = secondTickDist / subTicks;
            double timePerSubTick = 1000.0 / subTicks;
            double ms = timePerSubTick;

            for (double y = subTickDist; y < secondTickDist - subTickDist; y += subTickDist) {
                var msTickRect = new Rect(x + y, visibleArea_.Top, 2, 3);
                graphDC.DrawRectangle(Brushes.DimGray, null, msTickRect);
                double time = (seconds + ms / 1000);
                if (subTicks <= 10) {
                    DrawCenteredText($"{time:0.0}", msTickRect.Left, msTickRect.Top + TextMarginY, graphDC);
                }
                else if (subTicks <= 100) {
                    DrawCenteredText($"{time:0.00}", msTickRect.Left, msTickRect.Top + TextMarginY, graphDC);
                }
                else {
                    int digits = (int)Math.Ceiling(Math.Log10(subTicks));
                    var timeStr = String.Format("{0:0." + new string('0', digits) + "}", time);
                    DrawCenteredText(timeStr, msTickRect.Left, msTickRect.Top + TextMarginY, graphDC);
                }

                ms += timePerSubTick;
            }

            seconds++;
        }
    }

    public FlameGraphNode HitTestNode(Point point) {
        var queryRect = new Rect(point.X / maxWidth_, point.Y - timeBarHeight_, 1.0 / maxWidth_, 1);

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

    public Point ComputeNodePosition(FlameGraphNode node) {
        return new Point(node.Bounds.Left * maxWidth_, node.Bounds.Top);
    }

    private HighlightingStyle PickDummyNodeStyle(HighlightingStyle style) {
        /// TODO Cache
        var newColor = ColorUtils.IncreaseSaturation(((SolidColorBrush)style.BackColor).Color, 0.3f);
        newColor = ColorUtils.AdjustLight(newColor, 2.0f);
        return new HighlightingStyle(newColor, style.Border);
    }

    private DrawingBrush CreatePlaceholderTiledBrush(double tileSize) {
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

        var penBrush = ColorBrushes.GetBrush(Colors.DimGray);
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

#if false
    private double timeBarHeight_ = TimeBarHeight;
#else
    private double timeBarHeight_ = 0;
#endif

    public void DrawNode(FlameGraphNode flameGraphNode, DrawingContext dc) {
        if (flameGraphNode.IsDummyNode) {
            return;
        }

        var bounds = new Rect(flameGraphNode.Bounds.Left * maxWidth_,
                              flameGraphNode.Bounds.Top + timeBarHeight_,
                              flameGraphNode.Bounds.Width * maxWidth_,
                              flameGraphNode.Bounds.Height);

        if (bounds.Width <= 1) {
            return;
        }

        if (cachedNodeGuidelines_ == null) {
            cachedNodeGuidelines_ = CreateGuidelineSet(bounds, 0.5f);
        }

        dc.PushGuidelineSet(cachedNodeGuidelines_);
        dc.DrawRectangle(flameGraphNode.Style.BackColor, flameGraphNode.Style.Border, bounds);

        // ...
        int index = 0;
        bool trimmed = false;
        double margin = FlameGraphNode.DefaultMargin;

        // Start the text in the visible area.
        bool startsInView = bounds.Left >= visibleArea_.Left;
        double offsetY = 1;
        double offsetX = startsInView ? bounds.Left : visibleArea_.Left;
        double maxWidth = bounds.Width - 2 * FlameGraphNode.DefaultMargin;

        if (!startsInView) {
            maxWidth -= visibleArea_.Left - bounds.Left;
        }

        // Compute layout and render each text part.
        while (maxWidth > 8 && index < FlameGraphNode.MaxTextParts && !trimmed) {
            string label = "";
            bool useNameFont = false;
            Brush textColor = flameGraphNode.TextColor;

            switch (index) {
                case 0: {
                    if (flameGraphNode.CallTreeNode != null) {
                        label = flameGraphNode.CallTreeNode.FunctionName;

                        if (true) {
                            //? TODO: option
                            var moduleLabel = flameGraphNode.CallTreeNode.ModuleName + "!";
                            var (modText, modGlyphs, modTextTrimmed, modTextSize) =
                                TrimTextToWidth(moduleLabel, maxWidth - margin, false);

                            if (modText.Length > 0) {
                                DrawText(modGlyphs, bounds, flameGraphNode.ModuleTextColor, offsetX + margin, offsetY, modTextSize, dc);
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
                    if (flameGraphNode.ShowWeightPercentage) {
                        label = ExtensionMethods.AsPercentageString(ScaleWeight(flameGraphNode.Weight));
                        margin = FlameGraphNode.ExtraValueMargin;
                        textColor = flameGraphNode.WeightTextColor;
                        useNameFont = true;
                    }

                    break;
                }

                case 2: {
                    if (flameGraphNode.ShowWeight) {
                        label = $"({ExtensionMethods.AsMillisecondsString(flameGraphNode.Weight)})";
                        margin = FlameGraphNode.DefaultMargin;
                        textColor = flameGraphNode.WeightTextColor;
                    }

                    break;
                }
                default: {
                    throw new InvalidOperationException();
                }
            }

            // Trim the text to fit into the remaining space and draw it.
            var (text, glyphs, textTrimmed, textSize) =
                TrimTextToWidth(label, maxWidth - margin, useNameFont);

            if (text.Length > 0) {
                DrawText(glyphs, bounds, textColor, offsetX + margin, offsetY, textSize, dc);
            }

            trimmed = textTrimmed;
            maxWidth -= textSize.Width + margin;
            offsetX += textSize.Width + margin;
            index++;
        }

        dc.Pop(); // PushGuidelineSet
    }

    private GuidelineSet cachedTextGuidelines_;
    private GuidelineSet cachedNodeGuidelines_;
    private GuidelineSet cachedDummyNodeGuidelines_;

    private void DrawText(GlyphRun glyphs, Rect bounds, Brush textColor, double offsetX, double offsetY,
            Size textSize, DrawingContext dc) {
            double x = offsetX;
            double y = bounds.Top + bounds.Height / 2 + textSize.Height / 4 + offsetY;

            if (cachedTextGuidelines_ == null) {
                var rect = glyphs.ComputeAlignmentBox();
                cachedTextGuidelines_ = CreateGuidelineSet(rect, 1.0f);
            }

            dc.PushGuidelineSet(cachedTextGuidelines_);
            dc.PushTransform(new TranslateTransform(x, y));
            dc.DrawGlyphRun(textColor, glyphs);
            dc.Pop();
            dc.Pop(); // PushGuidelineSet
        }

    private (string Text, GlyphRun glyphs, bool Trimmed, Size TextSize)
            TrimTextToWidth(string text, double maxWidth, bool useNameFont) {
            var originalText = text;
            var glyphsCache = useNameFont ? NameGlyphsCache : GlyphsCache;

            if (maxWidth <= 0 || string.IsNullOrEmpty(text)) {
                return ("", glyphsCache.GetGlyphs("").Glyphs, true, Size.Empty);
            }

            //? TODO: cache measurement and reuse if
            //  - size is larger than prev
            //  - size is smaller, but less than a delta that's some multiple of avg letter width
            //? could also remember based on # of letter,  letters -> Size mapping

            var glyphInfo = glyphsCache.GetGlyphs(text, maxWidth);
            bool trimmed = false;

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

            glyphsCache.CacheGlyphs(glyphInfo, originalText, maxWidth);

            return (text, glyphInfo.Glyphs, trimmed,
                new Size(glyphInfo.TextWidth, glyphInfo.TextHeight));
        }

        private GuidelineSet CreateGuidelineSet(Rect rect, double penWidth) {
            GuidelineSet guidelines = new GuidelineSet();
            guidelines.GuidelinesX.Add(rect.Left + penWidth);
            guidelines.GuidelinesX.Add(rect.Right + penWidth);
            guidelines.GuidelinesY.Add(rect.Top + penWidth);
            guidelines.GuidelinesY.Add(rect.Bottom + penWidth);
            return guidelines;
        }

}