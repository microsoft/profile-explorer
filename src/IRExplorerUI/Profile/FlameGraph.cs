using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;

namespace IRExplorerUI.Profile {
    public class FlameGraphNode {
        private const double DefaultTextSize = 12;
        private const double DefaultMargin = 4;
        private const double ExtraValueMargin = 20;
        private const double MinVisibleWidth = 2;

        public FlameGraphNode(ProfileCallTreeNode node, FlameGraph owner, TimeSpan weight, int depth) {
            Node = node;
            Owner = owner;
            Weight = weight;
            Depth = depth;

            ShowWeight = true;
            ShowWeightPercentage = true;
            lastWidth_ = double.MaxValue;
        }

        public FlameGraph Owner { get; }
        public ProfileCallTreeNode Node { get; }
        public FlameGraphNode Parent { get; set; }
        public List<FlameGraphNode> Children { get; set; }

        public TimeSpan Weight { get; set; }
        public TimeSpan ChildrenWeight { get; set; }
        public int Depth { get; }

        public DrawingVisual Visual { get; set; }
        public HighlightingStyle Style { get; set; }
        public Typeface TextFont { get; set; }
        public Brush TextColor { get; set; }
        public Brush WeightTextColor { get; set; }
        public Rect Bounds { get; set; }
        public bool IsSelected { get; set; }
        public bool IsHovered { get; set; }
        public bool ShowWeight { get; set; }
        public bool ShowWeightPercentage { get; set; }
        public bool ShowInclusiveWeight { get; set; }

        private double lastWidth_;
        private Size lastSize_;

        public void Clear() {
            using var dc = Visual.RenderOpen();
        }

        public void Draw() {
            using var dc = Visual.RenderOpen();

            if (Bounds.Width < MinVisibleWidth) {
                return;
            }

            // Force pixel-snapping to get sharper edges.
            // var guidelines = new GuidelineSet();
            // double halfPenWidth = Style.Border.Thickness / 2;
            // guidelines.GuidelinesX.Add(Bounds.Left + halfPenWidth);
            // guidelines.GuidelinesX.Add(Bounds.Right + halfPenWidth);
            // guidelines.GuidelinesY.Add(Bounds.Top + halfPenWidth);
            // guidelines.GuidelinesY.Add(Bounds.Bottom + halfPenWidth);
            // dc.PushGuidelineSet(guidelines);
            dc.DrawRectangle(Style.BackColor, Style.Border, Bounds);

            // ...
            int index = 0;
            bool trimmed = false;
            double margin = DefaultMargin;
            double offsetX = 0;
            double maxWidth = Bounds.Width - 2 * DefaultMargin;

            while (maxWidth > 8 && index < 3 && !trimmed) {
                string label = "";
                Brush textColor = TextColor;

                //? font color, bold
                switch(index) {
                    case 0: {
                        label = Node != null ? Node.FunctionName : "All";
                        break;
                    }
                    case 1: {
                        if (ShowWeightPercentage && Node != null) {
                            label = Owner.ScaleWeight(Node.Weight).AsPercentageString();
                            margin = ExtraValueMargin;
                            textColor = WeightTextColor;
                        }

                        break;
                    }

                    case 2: {
                        if (ShowWeight && Node != null) {
                            label = $"({Node.Weight.AsMillisecondsString()})";
                            margin = ExtraValueMargin;
                            textColor = WeightTextColor;
                        }

                        break;
                    }
                };

                //? todo: trim text, tooltip, click event handler, double-click to open func (or instance in tree with filter)
                var (text, textTrimmed, textSize) = TrimTextToWidth(label, maxWidth - margin, index == 0);

                if (text.Length > 0) {
                    var styledText = new FormattedText(text, CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, TextFont, DefaultTextSize, textColor,
                        VisualTreeHelper.GetDpi(Visual).PixelsPerDip);
                    double x = Bounds.Left + offsetX + margin;
                    double y = Bounds.Top + (Bounds.Height - textSize.Height) / 2;
                    dc.DrawText(styledText, new Point(x, y));
                }

                trimmed = textTrimmed;
                maxWidth -= textSize.Width + margin;
                offsetX += textSize.Width + margin;
                index++;
            }
        }

        private (string  Text, bool Trimmed, Size TextSize) TrimTextToWidth(string text, double maxWidth, bool useCache = false) {
            if (maxWidth <= 0) {
                return ("", true, Size.Empty);
            }

            if (useCache) {
                if (maxWidth >= lastWidth_) {
                    return (text, false, lastSize_);
                }
            }

            //? TODO: cache measurement and reuse if
            //  - size is larger than prev
            //  - size is smaller, but less than a delta that's some multiple of avg letter width
            //? could also remember based on # of letter,  letters -> Size mapping

            var size = Utils.MeasureString(text, TextFont, DefaultTextSize);
            bool trimmed = false;

            if (size.Width > maxWidth) {
                var letterSize = Utils.MeasureString(1, TextFont, DefaultTextSize);
                var extraWidth = size.Width - maxWidth;
                var extraLetters = Math.Max(1, (int)Math.Ceiling(extraWidth / letterSize.Width));
                extraLetters += 1; // . suffix
                trimmed = true;

                if (text.Length > extraLetters) {
                    text = text.Substring(0, text.Length - extraLetters) + ".";

                    //? TODO: instead measure the cut part, or the shorter one
                    size = Utils.MeasureString(text, TextFont, DefaultTextSize);
                }
                else {
                    text = "";
                    size = new Size(maxWidth, size.Height);
                }
            }

            if (useCache) {
                if (!trimmed) {
                    lastWidth_ = maxWidth;
                    lastSize_ = size;
                }
                else {
                    lastWidth_ = Double.MaxValue; // Invalidate.
                }
            }

            return (text, trimmed, size);
        }
    }


    public class FlameGraph {
        public double MaxWidth { get; set; }
        public double NodeHeight { get; set; }
        public FlameGraphNode RootNode { get; set; }
        public TimeSpan RootWeight { get; set; }

        public void Build(ProfileCallTree callTree) {
            // Make on dummy root node that hosts all real root nodes.
            RootWeight = callTree.TotalRootNodesWeight;
            RootNode = Build(null, RootWeight, callTree.RootNodes, 0);
        }

        public FlameGraphNode Build(ProfileCallTreeNode node, TimeSpan weight,
                                    ICollection<ProfileCallTreeNode> children, int depth) {
            var flameNode = new FlameGraphNode(node, this, weight, depth);

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
                var childNode = Build(child, child.Weight, child.Children, depth + 1);
                childNode.Parent = flameNode;
                flameNode.Children.Add(childNode);
            }

            return flameNode;
        }

        public double ScaleWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)RootWeight.Ticks;
        }

        private double ComputeUnit(double baseUnit, TimeSpan weight, TimeSpan totalWeight) {
            return (baseUnit * (double)weight.Ticks) / (double)totalWeight.Ticks;
        }
    }

    public class FlameGraphRenderer {
        private FlameGraph flameGraph_;
        private double nodeHeight_;
        internal double maxWidth_;
        internal Rect visibleArea_;
        private Typeface font_;
        private ColorPalette palette_;
        private Pen defaultBorder_;
        private DrawingVisual graphVisual_;

        public FlameGraphRenderer(FlameGraph flameGraph, double maxWidth, Rect visibleArea) {
            flameGraph_ = flameGraph;
            maxWidth_ = maxWidth;
            visibleArea_ = visibleArea;
            nodeHeight_ = 20; //? Option
            font_ = new Typeface("Verdana");
            palette_ = ColorPalette.Profile;
            defaultBorder_ = ColorPens.GetPen(Colors.Black);
        }

        public DrawingVisual Render() {
            var visual = SetupNodeVisual(flameGraph_.RootNode);
            UpdateNodeWidth(flameGraph_.RootNode, 0, 0);
            visual.Drawing?.Freeze();
            return visual;
        }

        private DrawingVisual SetupNodeVisual(FlameGraphNode node) {
            graphVisual_ = new DrawingVisual();
            SetupNodeVisual(node, graphVisual_);
            return graphVisual_;
        }

        private DrawingVisual SetupNodeVisual(FlameGraphNode node, DrawingVisual graphVisual) {
            node.Visual = new DrawingVisual();
            node.Visual.SetValue(FrameworkElement.TagProperty, node);
            graphVisual.Children.Add(node.Visual);

            //? TODO: Palette based on module
            int colorIndex = Math.Min(node.Depth, palette_.Count - 1);
            var backColor = palette_[palette_.Count - colorIndex - 1];
            node.Style = new HighlightingStyle(backColor, defaultBorder_);
            node.TextFont = font_;
            node.TextColor = Brushes.Black;
            node.WeightTextColor = Brushes.DarkBlue;

            if (node.Children != null) {
                foreach (var childNode in node.Children) {
                    SetupNodeVisual(childNode, graphVisual);
                }
            }

            return node.Visual;
        }

        public void UpdateMaxWidth(double maxWidth) {
            maxWidth_ = maxWidth;
            Redraw();
        }

        public void Redraw() {
            using var dc = graphVisual_.RenderOpen();
            UpdateNodeWidth(flameGraph_.RootNode, 0, 0);
        }

        private void UpdateNodeWidth(FlameGraphNode node, double x, double y) {
            double width = flameGraph_.ScaleWeight(node.Weight) * maxWidth_;
            width = Math.Max(width, 1);
            var prevBounds = node.Bounds;
            node.Bounds = new Rect(x, y, width, nodeHeight_);

            if (node.Children != null && width > 1) {
                // Children are sorted by weight.
                foreach (var childNode in node.Children) {
                    UpdateNodeWidth(childNode, x, y + nodeHeight_);
                    var childWidth = flameGraph_.ScaleWeight(childNode.Weight) * maxWidth_;
                    x += childWidth;
                }
            }

            //? TODO: if node has < 2 width, don't bother to iterate over nodes
            //? - if nodes outside view, don't need update

            if (node.Bounds.IntersectsWith(visibleArea_) ||
                prevBounds.IntersectsWith(visibleArea_)) {
                node.Draw();
            }
        }
    }
}