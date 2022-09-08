using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace IRExplorerUI.Profile {
    public class FlameGraphNode {
        private const double DefaultTextSize = 12;
        private const double DefaultMargin = 4;

        public FlameGraphNode(ProfileCallTreeNode node, double unit, int depth) {
            Node = node;
            Unit = unit;
            Depth = depth;

            ShowWeightPercentage = true;
        }

        public ProfileCallTreeNode Node { get; }
        public List<FlameGraphNode> Children;
        public double Unit { get; }
        public int Depth { get; }

        public DrawingVisual Visual { get; set; }
        public HighlightingStyle Style { get; set; }
        public Typeface TextFont { get; set; }
        public Brush TextColor { get; set; }
        public Rect Bounds { get; set; }
        public bool IsSelected { get; set; }
        public bool IsHovered { get; set; }
        public bool ShowWeight { get; set; }
        public bool ShowWeightPercentage { get; set; }

        public void Draw() {
            using var dc = Visual.RenderOpen();
            double halfPenWidth = Style.Border.Thickness / 2;

            // Force pixel-snapping to get sharper edges.
            var guidelines = new GuidelineSet();
            guidelines.GuidelinesX.Add(Bounds.Left + halfPenWidth);
            guidelines.GuidelinesX.Add(Bounds.Right + halfPenWidth);
            guidelines.GuidelinesY.Add(Bounds.Top + halfPenWidth);
            guidelines.GuidelinesY.Add(Bounds.Bottom + halfPenWidth);
            dc.PushGuidelineSet(guidelines);

            dc.DrawRectangle(Style.BackColor, Style.Border, Bounds);

            if (Bounds.Width > 2) {
                string label = Node != null ? Node.FunctionName : "All";
                var (text, trimmed, textWidth, textHeight) = TrimTextToWidth(label, Bounds.Width - 2 * DefaultMargin);

                var styledText = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, TextFont, DefaultTextSize, TextColor,
                    VisualTreeHelper.GetDpi(Visual).PixelsPerDip);
                //? todo: trim text, tooltip, click event handler, double-click to open func (or instance in tree with filter)
                double x = Bounds.Left + DefaultMargin;
                double y = Bounds.Top + (Bounds.Height - textHeight) / 2;
                dc.DrawText(styledText, new Point(x, y));

                if (ShowWeightPercentage && !trimmed) {

                    var styledText2 = new FormattedText("20.35%", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, TextFont, DefaultTextSize, Brushes.DarkBlue,
                        VisualTreeHelper.GetDpi(Visual).PixelsPerDip);
                    //? todo: trim text, tooltip, click event handler, double-click to open func (or instance in tree with filter)
                    x = Bounds.Left + textWidth + DefaultMargin * 2;
                    y = Bounds.Top + (Bounds.Height - textHeight) / 2;
                    dc.DrawText(styledText2, new Point(x, y));
                }
            }
        }

        private (string  Text, bool Trimmed, double Width, double Height) TrimTextToWidth(string text, double maxWidth) {
            var size = Utils.MeasureString(text, TextFont, DefaultTextSize);
            bool trimmed = false;

            if (size.Width > maxWidth) {
                var letterSize = Utils.MeasureString(1, TextFont, DefaultTextSize);
                var extraWidth = size.Width - maxWidth;
                var extraLetters = Math.Max(1, (int)(extraWidth / letterSize.Width));
                extraLetters++; // . suffix

                if (text.Length > extraLetters) {
                    text = text.Substring(0, text.Length - extraLetters) + ".";
                    trimmed = true;
                }
            }

            return (text, trimmed, size.Width, size.Height);
        }

        // private void DrawText(string text, Brush textColor, double x, double y)
    }


    public class FlameGraph {
        public double MaxWidth { get; set; }
        public double NodeHeight { get; set; }

        public FlameGraphNode Node { get; set; }

        public void Build(ProfileCallTree callTree) {
            // Make on dummy root node that hosts all real root nodes.
            double unit = 1.0;
            var rootWeight = callTree.TotalRootNodesWeight;
            Node = Build(null, rootWeight, callTree.RootNodes, unit, 0);
        }

        public FlameGraphNode Build(ProfileCallTreeNode node, TimeSpan nodeWeight, ICollection<ProfileCallTreeNode> children,
                                    double unit, int depth) {
            var flameNode = new FlameGraphNode(node, unit, depth);

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

            double childrenUnit = ComputeUnit(unit, childrenWeight, nodeWeight);
            flameNode.Children ??= new List<FlameGraphNode>();

            foreach (var child in sortedChildren) {
                double childUnit = ComputeUnit(childrenUnit, child.Weight, childrenWeight);
                flameNode.Children.Add(Build(child, child.Weight, child.Children, childUnit, depth + 1));
            }

            return flameNode;
        }

        private double ComputeUnit(double baseUnit, TimeSpan weight, TimeSpan totalWeight) {
            return (baseUnit * (double)weight.Ticks) / (double)totalWeight.Ticks;
        }
    }

    public class FlameGraphRenderer {

        private FlameGraph flameGraph_;
        private double surfaceWidth_;
        private double nodeHeight_;
        private Typeface font_;
        private ColorPalette palette_;
        private Pen defaultBorder_;

        public FlameGraphRenderer(FlameGraph flameGraph, double surfaceWidth) {
            flameGraph_ = flameGraph;
            surfaceWidth_ = surfaceWidth;
            nodeHeight_ = 20;
            font_ = new Typeface("Verdana");
            palette_ = ColorPalette.Profile;
            defaultBorder_ = ColorPens.GetPen(Colors.Black);
        }

        public DrawingVisual Render() {
            var visual = SetupNodeVisual(flameGraph_.Node);
            UpdateNodeWidth(flameGraph_.Node, 0, 0);
            visual.Drawing?.Freeze();
            return visual;
        }

        private DrawingVisual SetupNodeVisual(FlameGraphNode node) {
            node.Visual = new DrawingVisual();
            node.Visual.SetValue(FrameworkElement.TagProperty, node);

            int colorIndex = Math.Min(node.Depth, palette_.Count - 1);
            var backColor = palette_[palette_.Count - colorIndex - 1];
            node.Style = new HighlightingStyle(backColor, defaultBorder_);
            node.TextFont = font_;
            node.TextColor = Brushes.Black;

            if (node.Children != null) {
                foreach (var childNode in node.Children) {
                    node.Visual.Children.Add(SetupNodeVisual(childNode));
                }
            }

            return node.Visual;
        }

        public void UpdateMaxWidth(double maxWidth) {
            surfaceWidth_ = maxWidth;
            UpdateNodeWidth(flameGraph_.Node, 0, 0);
        }

        private void UpdateNodeWidth(FlameGraphNode node, double x, double y) {
            double width = node.Unit * surfaceWidth_;
            width = Math.Max(width, 2);
            node.Bounds = new Rect(x, y, width, nodeHeight_);

            if (node.Children != null) {
                // Children are sorted by weight.
                foreach (var childNode in node.Children) {
                    UpdateNodeWidth(childNode, x, y + nodeHeight_);
                    var childWidth = childNode.Unit * surfaceWidth_;
                    x += childWidth;
                }
            }

            node.Draw();
        }
    }
}