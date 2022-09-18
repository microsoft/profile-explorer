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
        internal const double MinVisibleRectWidth = 3;
        internal const double MinVisibleWidth = 1;

        public FlameGraphNode(ProfileCallTreeNode callTreeNode, TimeSpan weight, int depth) {
            CallTreeNode = callTreeNode;
            Weight = weight;
            Depth = depth;

            ShowWeight = true;
            ShowWeightPercentage = true;
            lastWidth_ = double.MaxValue;
        }

        public FlameGraphRenderer Owner { get; set; }
        public ProfileCallTreeNode CallTreeNode { get; }
        public FlameGraphNode Parent { get; set; }
        public List<FlameGraphNode> Children { get; set; }

        public TimeSpan Weight { get; set; }
        public TimeSpan ChildrenWeight { get; set; }
        public int Depth { get; }

        public DrawingVisual Visual { get; set; }
        public HighlightingStyle Style { get; set; }
        public Brush TextColor { get; set; }
        public Brush ModuleTextColor { get; set; }
        public Brush WeightTextColor { get; set; }
        public Rect Bounds { get; set; }
        public bool IsSelected { get; set; }
        public bool IsHovered { get; set; }
        public bool ShowWeight { get; set; }
        public bool ShowWeightPercentage { get; set; }
        public bool ShowInclusiveWeight { get; set; }

        private double lastWidth_;
        private Size lastSize_;
        private GlyphRun lastGlyphs_;
        private bool isCleared_;

        public void Draw(Rect visibleArea) {
            using var dc = Visual.RenderOpen();

            if (Bounds.Width < MinVisibleWidth) {
                return;
            }

            isCleared_ = false;
            dc.DrawRectangle(Style.BackColor, Style.Border, Bounds);

            // ...
            int index = 0;
            bool trimmed = false;
            double margin = DefaultMargin;

            // Start the text in the visible area.
            bool startsInView = Bounds.Left >= visibleArea.Left;
            double offsetX = startsInView? Bounds.Left : visibleArea.Left;
            double maxWidth = Bounds.Width - 2 * DefaultMargin;

            if (!startsInView) {
                maxWidth -= visibleArea.Left - Bounds.Left;
            }

            while (maxWidth > 8 && index < 3 && !trimmed) {
                string label = "";
                string moduleLabel = null;
                bool useNameFont = false;
                Brush textColor = TextColor;

                //? font color, bold
                switch(index) {
                    case 0: {
                        if (CallTreeNode != null) {
                            label = CallTreeNode.FunctionName;

                            if (true) { //? TODO: option and cache
                              moduleLabel = CallTreeNode.ModuleName + "!";
                              var (modText, modGlyphs, modTextTrimmed, modTextSize) = TrimTextToWidth(moduleLabel, maxWidth - margin, false);

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
                        if (ShowWeightPercentage && CallTreeNode != null) {
                            label = Owner.ScaleWeight(CallTreeNode.Weight).AsPercentageString();
                            margin = ExtraValueMargin;
                            textColor = WeightTextColor;
                        }

                        break;
                    }

                    case 2: {
                        if (ShowWeight && CallTreeNode != null) {
                            label = $"({CallTreeNode.Weight.AsMillisecondsString()})";
                            margin = ExtraValueMargin;
                            textColor = WeightTextColor;
                        }

                        break;
                    }
                };

                //? Extend cache to all text parts, saved in array by index
                var (text, glyphs, textTrimmed, textSize) = TrimTextToWidth(label, maxWidth - margin, useNameFont, index == 0);

                if (text.Length > 0) {
                    DrawText(glyphs, textColor, offsetX, margin, textSize, dc);
                }

                trimmed = textTrimmed;
                maxWidth -= textSize.Width + margin;
                offsetX += textSize.Width + margin;
                index++;
            }
        }

        private void DrawText(GlyphRun glyphs, Brush textColor, double offsetX, double margin,
                              Size textSize, DrawingContext dc) {
            double x = offsetX + margin;
            double y = Bounds.Top + Bounds.Height / 2 + textSize.Height / 4;

            var rect = glyphs.ComputeAlignmentBox();
            const double halfPenWidth = 0.5;
            GuidelineSet guidelines = new GuidelineSet();
            guidelines.GuidelinesX.Add(rect.Left + halfPenWidth);
            guidelines.GuidelinesX.Add(rect.Right + halfPenWidth);
            guidelines.GuidelinesY.Add(rect.Top + halfPenWidth);
            guidelines.GuidelinesY.Add(rect.Bottom + halfPenWidth);

            dc.PushTransform(new TranslateTransform(x, y));
            dc.PushGuidelineSet(guidelines);
            dc.DrawGlyphRun(textColor, glyphs);
            dc.Pop();
            dc.Pop();
        }

        public void Clear() {
            if (isCleared_) {
                return;
            }

            using var dc = Visual.RenderOpen();
            isCleared_ = true;
        }

        private (string Text, GlyphRun glyphs, bool Trimmed, Size TextSize)
            TrimTextToWidth(string text, double maxWidth, bool useNameFont, bool useCache = false) {
            var cache = useNameFont ? Owner.NameGlyphsCache : Owner.GlyphsCache;

            if (maxWidth <= 0 || string.IsNullOrEmpty(text)) {
                return ("", cache.GetGlyphs("").Glyphs, true, Size.Empty);
            }

            if (useCache) {
                if (maxWidth >= lastWidth_) {
                    return (text, lastGlyphs_, false, lastSize_);
                }
            }

            //? TODO: cache measurement and reuse if
            //  - size is larger than prev
            //  - size is smaller, but less than a delta that's some multiple of avg letter width
            //? could also remember based on # of letter,  letters -> Size mapping

            var (glyphs, textWidth, textHeight) = cache.GetGlyphs(text);
            bool trimmed = false;

            if (textWidth > maxWidth) {
                var letterWidth = Math.Ceiling(textWidth / text.Length);
                var extraWidth = textWidth - maxWidth;
                var extraLetters = Math.Max(1, (int)Math.Ceiling(extraWidth / letterWidth));
                extraLetters += 1; // . suffix
                trimmed = true;

                if (text.Length > extraLetters) {
                    text = text.Substring(0, text.Length - extraLetters) + ".";
                    (glyphs, textWidth, textHeight) = cache.GetGlyphs(text);
                }
                else {
                    text = "";
                    (glyphs, textWidth, textHeight) = cache.GetGlyphs(text);
                    textWidth = maxWidth;
                }
            }

            if (useCache) {
                if (!trimmed) {
                    lastWidth_ = maxWidth;
                    lastGlyphs_ = glyphs;
                    lastSize_ = new Size(textWidth, textHeight);
                }
                else {
                    lastWidth_ = Double.MaxValue; // Invalidate.
                }
            }

            return (text, glyphs, trimmed, new Size(textWidth, textHeight));
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
            if(children == null || children.Count == 0) {
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
        private ColorPalette palette_;
        private Pen defaultBorder_;
        private Brush placeholderColor_;
        private DrawingVisual graphVisual_;
        private GlyphRunCache glyphs_;
        private GlyphRunCache nameGlyphs_;
        private QuadTree<FlameGraphNode> nodesQuadTree_;

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
            palette_ = ColorPalette.Profile;
            defaultBorder_ = ColorPens.GetPen(Colors.Black);
            placeholderColor_ = ColorBrushes.GetBrush(Colors.DarkGray);

            nodeHeight_ = 18; //? Option
            font_ = new Typeface("Segoe UI");
            nameFont_ = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.DemiBold, FontStretch.FromOpenTypeStretch(5));
            fontSize_ = DefaultTextSize;
        }

        public DrawingVisual Setup() {
            var visual = SetupNodeVisual(flameGraph_.RootNode);
            glyphs_ = new GlyphRunCache(font_, fontSize_, VisualTreeHelper.GetDpi(visual).PixelsPerDip);
            nameGlyphs_ = new GlyphRunCache(nameFont_, fontSize_, VisualTreeHelper.GetDpi(visual).PixelsPerDip);

            Redraw(true);
            visual.Drawing?.Freeze();
            return visual;
        }

        private DrawingVisual SetupNodeVisual(FlameGraphNode node) {
            //? Option to sort by timeline
            //? invert graph, tooltips, options struct with node height, font, histogram etc
            //! keyboard, cener zoom if not mouse
            graphVisual_ = new DrawingVisual();
            SetupNodeVisual(node, graphVisual_);
            return graphVisual_;
        }

        private DrawingVisual SetupNodeVisual(FlameGraphNode node, DrawingVisual graphVisual) {
            node.Owner = this;
            node.Visual = new DrawingVisual();
            node.Visual.SetValue(FrameworkElement.TagProperty, node);
            graphVisual.Children.Add(node.Visual);

            //? TODO: Palette based on module
            //? int colorIndex = Math.Min(node.Depth, palette_.Count - 1);
            int colorIndex = node.Depth % palette_.Count;
            var backColor = palette_[palette_.Count - colorIndex - 1];
            node.Style = new HighlightingStyle(backColor, defaultBorder_);
            node.TextColor = Brushes.DarkBlue;
            node.ModuleTextColor = Brushes.DimGray;
            node.WeightTextColor = Brushes.DarkGreen;

            if (node.Children != null) {
                foreach (var childNode in node.Children) {
                    SetupNodeVisual(childNode, graphVisual);
                }
            }

            return node.Visual;
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

        private void Redraw(bool resized) {
            using var graphDC = graphVisual_.RenderOpen();

            if (!resized && nodesQuadTree_ != null) {
                // Update only the visible nodes on scrolling.
                foreach (var node in nodesQuadTree_.GetNodesInside(visibleArea_)) {
                    node.Draw(visibleArea_);
                }
                return;
            }

            // Recompute the position of all nodes and rebuild the quad tree.
            nodesQuadTree_ = new QuadTree<FlameGraphNode>();
            nodesQuadTree_.Bounds = GraphArea;
            maxNodeDepth_ = 0;
            UpdateNodeWidth(flameGraph_.RootNode, 0, 0, graphDC, true);
        }

        private void UpdateNodeWidth(FlameGraphNode node, double x, double y, DrawingContext graphDC, bool redraw) {
            double width = flameGraph_.ScaleWeight(node.Weight) * maxWidth_;
            var prevBounds = node.Bounds;
            node.Bounds = new Rect(x, y, width, nodeHeight_);
            node.Bounds = Utils.SnapToPixels(node.Bounds);

            if (node.Bounds.Width > 0 && node.Bounds.Height > 0) {
                nodesQuadTree_.Insert(node, node.Bounds);
            }

            if (redraw) {
                if (width < FlameGraphNode.MinVisibleRectWidth) {
                    redraw = false;
                }
                else {
                    maxNodeDepth_ = Math.Max(node.Depth, maxNodeDepth_);
                }
            }

            if (node.Children != null) {
                bool redrawChildren = redraw;

                // Children are sorted by weight.
                for(int i = 0; i < node.Children.Count; i++) {
                    //? If multiple children below width, single patterned rect
                    var childNode = node.Children[i];
                    var childWidth = flameGraph_.ScaleWeight(childNode.Weight) * maxWidth_;
                    childWidth = Utils.SnapToPixels(childWidth);

                    if (redrawChildren && childWidth < FlameGraphNode.MinVisibleRectWidth) {
                        double remainingWidth = childWidth;

                        for (int k = i + 1; k < node.Children.Count; k++) {
                            remainingWidth += flameGraph_.ScaleWeight(node.Children[k].Weight) * maxWidth_;
                        }

                        var replacement = new Rect(x, y + nodeHeight_, remainingWidth, nodeHeight_);
                        //? Could make color darker than level
                        //! TODO: Make a fake node that has details (sum of weights, tooltip with child count, etc)
                        graphDC.DrawRectangle(placeholderColor_, childNode.Style.Border, replacement);
                        redrawChildren = false;
                    }

                    UpdateNodeWidth(childNode, x, y + nodeHeight_, graphDC, redrawChildren);
                    x += childWidth;
                }
            }

            if (redraw && (node.Bounds.IntersectsWith(visibleArea_) ||
                           prevBounds.IntersectsWith(previousVisibleArea_))) {
                node.Draw(visibleArea_);
            }
            else {
                node.Clear();
            }
        }
    }
}