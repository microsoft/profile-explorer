using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using System.Windows.Documents;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Size = System.Windows.Size;

namespace IRExplorerUI.Profile {
    public class FlameGraphNode {
        internal const double DefaultMargin = 4;
        internal const double ExtraValueMargin = 20;
        internal const double MinVisibleRectWidth = 4;
        internal const double MinVisibleWidth = 1;
        private const int MaxTextParts = 3;

        public FlameGraphNode(ProfileCallTreeNode callTreeNode, TimeSpan weight, int depth) {
            CallTreeNode = callTreeNode;
            Weight = weight;
            Depth = depth;
            ShowWeight = true;
            ShowWeightPercentage = true;
        }

        public virtual bool IsGroup => false;
        public ProfileCallTreeNode CallTreeNode { get; }
        public FlameGraphRenderer Owner { get; set; }
        public FlameGraphNode Parent { get; set; }
        public List<FlameGraphNode> Children { get; set; }

        public TimeSpan Weight { get; set; }
        public TimeSpan ChildrenWeight { get; set; }
        public int Depth { get; }

        public HighlightingStyle Style { get; set; }
        public Brush TextColor { get; set; }
        public Brush ModuleTextColor { get; set; }
        public Brush WeightTextColor { get; set; }
        public Rect Bounds { get; set; }
        public bool ShowWeight { get; set; }
        public bool ShowWeightPercentage { get; set; }
        public bool ShowInclusiveWeight { get; set; }
        public bool IsDummyNode { get; set; }

        public bool HasChildren => Children is { Count: > 0 };

        public virtual void Draw(Rect visibleArea, DrawingContext dc) {
            if (IsDummyNode || Bounds.Width < MinVisibleWidth) {
                return;
            }

            dc.PushGuidelineSet(CreateGuidelineSet(Bounds));
            dc.DrawRectangle(Style.BackColor, Style.Border, Bounds);

            // ...
            int index = 0;
            bool trimmed = false;
            double margin = DefaultMargin;

            // Start the text in the visible area.
            bool startsInView = Bounds.Left >= visibleArea.Left;
            double offsetX = startsInView ? Bounds.Left : visibleArea.Left;
            double maxWidth = Bounds.Width - 2 * DefaultMargin;

            if (!startsInView) {
                maxWidth -= visibleArea.Left - Bounds.Left;
            }

            while (maxWidth > 8 && index < MaxTextParts && !trimmed) {
                string label = "";
                bool useNameFont = false;
                Brush textColor = TextColor;

                switch (index) {
                    case 0: {
                        if (CallTreeNode != null) {
                            label = CallTreeNode.FunctionName;

                            if (true) {
                                //? TODO: option
                                var moduleLabel = CallTreeNode.ModuleName + "!";
                                var (modText, modGlyphs, modTextTrimmed, modTextSize) =
                                    TrimTextToWidth(moduleLabel, maxWidth - margin, false);

                                if (modText.Length > 0) {
                                    DrawText(modGlyphs, ModuleTextColor, offsetX, margin, modTextSize, dc);
                                }

                                maxWidth -= modTextSize.Width + 1;
                                offsetX += modTextSize.Width + 1;
                            }
                        }
                        else {
                            label = "All";
                        }

                        useNameFont = true;
                        break;
                    }
                    case 1: {
                        if (ShowWeightPercentage) {
                            label = Owner.ScaleWeight(Weight).AsPercentageString();
                            margin = ExtraValueMargin;
                            textColor = WeightTextColor;
                        }

                        break;
                    }

                    case 2: {
                        if (ShowWeight) {
                            label = $"({Weight.AsMillisecondsString()})";
                            margin = ExtraValueMargin;
                            textColor = WeightTextColor;
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
                    DrawText(glyphs, textColor, offsetX, margin, textSize, dc);
                }

                trimmed = textTrimmed;
                maxWidth -= textSize.Width + margin;
                offsetX += textSize.Width + margin;
                index++;
            }

            dc.Pop(); // PushGuidelineSet
        }

        private void DrawText(GlyphRun glyphs, Brush textColor, double offsetX, double margin,
            Size textSize, DrawingContext dc) {
            double x = offsetX + margin;
            double y = Bounds.Top + Bounds.Height / 2 + textSize.Height / 4;

            var rect = glyphs.ComputeAlignmentBox();
            dc.PushGuidelineSet(CreateGuidelineSet(rect));
            dc.PushTransform(new TranslateTransform(x, y));
            dc.DrawGlyphRun(textColor, glyphs);
            dc.Pop();
            dc.Pop(); // PushGuidelineSet
        }

        private static GuidelineSet CreateGuidelineSet(Rect rect) {
            const double halfPenWidth = 0.5f;
            GuidelineSet guidelines = new GuidelineSet();
            guidelines.GuidelinesX.Add(rect.Left + halfPenWidth);
            guidelines.GuidelinesX.Add(rect.Right + halfPenWidth);
            guidelines.GuidelinesY.Add(rect.Top + halfPenWidth);
            guidelines.GuidelinesY.Add(rect.Bottom + halfPenWidth);
            return guidelines;
        }

        private (string Text, GlyphRun glyphs, bool Trimmed, Size TextSize)
            TrimTextToWidth(string text, double maxWidth, bool useNameFont) {
            var originalText = text;
            var glyphsCache = useNameFont ? Owner.NameGlyphsCache : Owner.GlyphsCache;

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

                // var letterWidth = Math.Ceiling(glyphInfo.TextWidth / text.Length);
                // var extraWidth = glyphInfo.TextWidth - maxWidth + 1;
                // var extraLetters = Math.Max(1, (int)Math.Ceiling(extraWidth / letterWidth));
                // extraLetters += 1; // . suffix
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
    }


    public class FlameGraphGroupNode : FlameGraphNode {
        public override bool IsGroup => true;
        public List<ProfileCallTreeNode> Nodes { get; }

        public FlameGraphGroupNode(List<ProfileCallTreeNode> nodes, TimeSpan weight, int depth) :
            base(null, weight, depth) {
            Nodes = nodes;

        }

        public override void Draw(Rect visibleArea, DrawingContext dc) {

        }
    }

    public class FlameGraph {
        public double MaxWidth { get; set; }
        public double NodeHeight { get; set; }
        public FlameGraphNode RootNode { get; set; }
        public TimeSpan RootWeight { get; set; }
        public ProfileCallTree CallTree { get; set; }

        public FlameGraph(ProfileCallTree callTree) {
            CallTree = callTree;
        }

        public void Build(ProfileCallTreeNode rootNode) {
            if (rootNode == null) {
                // Make on dummy root node that hosts all real root nodes.
                RootWeight = CallTree.TotalRootNodesWeight;
                var flameNode = new FlameGraphNode(null, RootWeight, 0);
                RootNode = Build(flameNode, RootWeight, CallTree.RootNodes, 0);
            }
            else {
                RootWeight = rootNode.Weight;
                var flameNode = new FlameGraphNode(rootNode, rootNode.Weight, 0);
                RootNode = Build(flameNode, RootWeight, rootNode.Children, 0);
            }
        }

        private FlameGraphNode Build(FlameGraphNode flameNode, TimeSpan weight,
            ICollection<ProfileCallTreeNode> children, int depth) {
            if (children == null || children.Count == 0) {
                return flameNode;
            }

            var sortedChildren = new List<ProfileCallTreeNode>(children.Count);
            TimeSpan childrenWeight = TimeSpan.Zero;

            foreach (var child in children) {
                sortedChildren.Add(child);
                childrenWeight += child.Weight;
            }

            sortedChildren.Sort((a, b) => b.Weight.CompareTo(a.Weight));

            flameNode.Children = new List<FlameGraphNode>(children.Count);
            flameNode.ChildrenWeight = childrenWeight;

            foreach (var child in sortedChildren) {
                var childFlameNode = new FlameGraphNode(child, child.Weight, depth + 1);
                var childNode = Build(childFlameNode, child.Weight, child.Children, depth + 1);
                childNode.Parent = flameNode;
                flameNode.Children.Add(childNode);
            }

            return flameNode;
        }

        public double ScaleWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)RootWeight.Ticks;
        }
    }

    public class FlameGraphRenderer {
        internal const double DefaultTextSize = 12;

        private FlameGraph flameGraph_;
        private int maxNodeDepth_;
        private double nodeHeight_;
        private double maxWidth_;
        private Rect visibleArea_;
        private Rect previousVisibleArea_;
        private Typeface font_;
        private Typeface nameFont_;
        private double fontSize_;
        private Dictionary<ProfileCallTreeNodeKind, ColorPalette> palettes_;
        private Pen defaultBorder_;
        private Brush placeholderColor_;
        private DrawingVisual graphVisual_;
        private GlyphRunCache glyphs_;
        private GlyphRunCache nameGlyphs_;
        private QuadTree<FlameGraphNode> nodesQuadTree_;
        private QuadTree<FlameGraphNode> dummyNodesQuadTree_;

        public GlyphRunCache GlyphsCache => glyphs_;
        public GlyphRunCache NameGlyphsCache => nameGlyphs_;

        public double MaxGraphWidth => maxWidth_;
        public double MaxGraphHeight => (maxNodeDepth_ + 1) * nodeHeight_;
        public Rect VisibleArea => visibleArea_;
        public Rect GraphArea => new Rect(0, 0, MaxGraphWidth, MaxGraphHeight);

        public FlameGraphRenderer(FlameGraph flameGraph, Rect visibleArea) {
            flameGraph_ = flameGraph;
            maxWidth_ = visibleArea.Width;
            visibleArea_ = visibleArea;
            palettes_ = new Dictionary<ProfileCallTreeNodeKind, ColorPalette>();

            palettes_[ProfileCallTreeNodeKind.Unset] = ColorPalette.Profile;
            palettes_[ProfileCallTreeNodeKind.NativeUser] = ColorPalette.Profile;
            palettes_[ProfileCallTreeNodeKind.NativeKernel] = ColorPalette.HeatMap;
            palettes_[ProfileCallTreeNodeKind.Managed] = ColorPalette.HeatMap2;


            //palettes_ = ColorPalette.MakeScale(0.5f, 0.8f, 0.8f, 1, 10);

            defaultBorder_ = ColorPens.GetPen(Colors.Black);
            placeholderColor_ = ColorBrushes.GetBrush(Colors.Green);

            nodeHeight_ = 18; //? Option
            font_ = new Typeface("Segoe UI");
            nameFont_ = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.DemiBold, FontStretch.FromOpenTypeStretch(5));
            fontSize_ = DefaultTextSize;
        }

        public DrawingVisual Setup() {
            graphVisual_ = new DrawingVisual();
            SetupNode(flameGraph_.RootNode);
            glyphs_ = new GlyphRunCache(font_, fontSize_, VisualTreeHelper.GetDpi(graphVisual_).PixelsPerDip);
            nameGlyphs_ = new GlyphRunCache(nameFont_, fontSize_, VisualTreeHelper.GetDpi(graphVisual_).PixelsPerDip);

            Redraw(true);
            graphVisual_.Drawing?.Freeze();
            return graphVisual_;
        }

        private void SetupNode(FlameGraphNode node) {
            //? TODO: Palette based on module
            //? int colorIndex = Math.Min(node.Depth, palettes_.Count - 1);
            var palette = node.CallTreeNode != null ?
                palettes_[node.CallTreeNode.Kind] :
                palettes_[ProfileCallTreeNodeKind.Unset];

            int colorIndex = node.Depth % palette.Count;
            var backColor = palette[palette.Count - colorIndex - 1];
            node.Style = new HighlightingStyle(backColor, defaultBorder_);
            node.TextColor = Brushes.DarkBlue;
            node.ModuleTextColor = Brushes.DimGray;
            node.WeightTextColor = Brushes.DarkGreen;
            node.Owner = this;

            if (node.Children != null) {
                foreach (var childNode in node.Children) {
                    SetupNode(childNode);
                }
            }
        }

        public void Redraw() {
            Redraw(false);
        }

        public void UpdateMaxWidth(double maxWidth) {
            maxWidth_ = maxWidth;
            Redraw(true);
        }

        public void UpdateVisibleArea(Rect visibleArea) {
            previousVisibleArea_ = visibleArea_;
            visibleArea_ = visibleArea;
            Redraw(false);
        }

        public double ScaleWeight(TimeSpan weight) {
            return flameGraph_.ScaleWeight(weight);
        }

        private DrawingBrush placeholderTileBrush_;

        private void Redraw(bool resized) {
            using var graphDC = graphVisual_.RenderOpen();

            if (resized || nodesQuadTree_ == null) {
                // Recompute the position of all nodes and rebuild the quad tree.
                nodesQuadTree_ = new QuadTree<FlameGraphNode>();
                dummyNodesQuadTree_ = new QuadTree<FlameGraphNode>();
                nodesQuadTree_.Bounds = GraphArea;
                dummyNodesQuadTree_.Bounds = GraphArea;
                maxNodeDepth_ = 0;
                UpdateNodeWidth(flameGraph_.RootNode, 0, 0, true);
            }

            // Update only the visible nodes on scrolling.
            foreach (var node in nodesQuadTree_.GetNodesInside(visibleArea_)) {
                node.Draw(visibleArea_, graphDC);
            }

            foreach (var node in dummyNodesQuadTree_.GetNodesInside(visibleArea_)) {
                graphDC.DrawRectangle(node.Style.BackColor, node.Style.Border, node.Bounds);
                graphDC.DrawRectangle(CreatePlaceholderTiledBrush(8), null, node.Bounds);
            }
        }

        private void UpdateNodeWidth(FlameGraphNode node, double x, double y, bool redraw) {
            double width = flameGraph_.ScaleWeight(node.Weight) * maxWidth_;
            var prevBounds = node.Bounds;
            node.Bounds = new Rect(x, y, width, nodeHeight_);
            node.Bounds = Utils.SnapToPixels(node.Bounds);
            node.IsDummyNode = !redraw;

            if (redraw) {
                maxNodeDepth_ = Math.Max(node.Depth, maxNodeDepth_);
            }

            if (node.Bounds.Width > 0 && node.Bounds.Height > 0) {
                nodesQuadTree_.Insert(node, node.Bounds);
            }

            if (node.Children == null) {
                return;
            }

            // Children are sorted by weight or time.
            int skippedChildren = 0;

            for (int i = 0; i < node.Children.Count; i++) {
                //? If multiple children below width, single patterned rect
                var childNode = node.Children[i];
                var childWidth = flameGraph_.ScaleWeight(childNode.Weight) * maxWidth_;
                childWidth = Utils.SnapToPixels(childWidth);


                if (skippedChildren == 0) {
                    if (childWidth < FlameGraphNode.MinVisibleRectWidth) {
                        childNode.IsDummyNode = true;
                        var replacement = CreateSmallWeightDummyNode(node, x, y, i, out skippedChildren);
                    }
                }
                else {
                    childNode.IsDummyNode = true;
                }

                UpdateNodeWidth(childNode, x, y + nodeHeight_, skippedChildren == 0);
                x += childWidth;

                if (skippedChildren > 0) {
                    childNode.IsDummyNode = true;
                    skippedChildren--;
                }
            }
        }

        private FlameGraphNode CreateSmallWeightDummyNode(FlameGraphNode node, double x, double y,
            int startIndex, out int skippedChildren) {
            TimeSpan totalWeight = TimeSpan.Zero;
            double totalWidth = 0;

            // Collect all the child nodes that have a small weight
            // and replace them by one dummy node.
            var nodes = new List<ProfileCallTreeNode>(node.Children.Count - startIndex);
            int k;

            for (k = startIndex; k < node.Children.Count; k++) {
                var childNode = node.Children[k];
                double childWidth = flameGraph_.ScaleWeight(childNode.Weight) * maxWidth_;

                // In case child sorting is not by weight, stop extending the range
                // when a larger one is found again.
                if (childWidth > FlameGraphNode.MinVisibleRectWidth) {
                    break;
                }

                nodes.Add(childNode.CallTreeNode);
                totalWidth += childWidth;
                totalWeight += childNode.Weight;
            }

            skippedChildren = k - startIndex; // Caller should ignore these children.

            if (totalWidth < FlameGraphNode.MinVisibleRectWidth) {
                return null; // Nothing to draw.
            }

            var replacement = new Rect(x, y + nodeHeight_, totalWidth, nodeHeight_);
            var dummyNode = new FlameGraphGroupNode(nodes, totalWeight, node.Depth);
            dummyNode.IsDummyNode = true;
            dummyNode.Bounds = replacement;
            dummyNode.Style = PickDummyNodeStyle(node.Style);
            dummyNodesQuadTree_.Insert(dummyNode, replacement);

            //? Could make color darker than level, or less satureded  better
            //! TODO: Make a fake node that has details (sum of weights, tooltip with child count, etc)
            return dummyNode;
        }

        public FlameGraphNode HitTestNode(Point point) {
            if (nodesQuadTree_ != null) {
                var nodes = nodesQuadTree_.GetNodesInside(new Rect(point, point));
                foreach (var node in nodes) {
                    return node;
                }
            }

            if (dummyNodesQuadTree_ != null) {
                var nodes = dummyNodesQuadTree_.GetNodesInside(new Rect(point, point));
                foreach (var node in nodes) {
                    return node;
                }
            }

            return null;
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
    }
}