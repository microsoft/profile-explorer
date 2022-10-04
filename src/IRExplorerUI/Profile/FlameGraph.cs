using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using System.Windows.Controls;
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
        internal const double RecomputeVisibleRectWidth = MinVisibleRectWidth * 4;
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

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public TimeSpan Duration { get; set; }

        public virtual void Draw(Rect visibleArea, DrawingContext dc) {
            if (IsDummyNode) {
                return;
            }

            var bounds = new Rect(Bounds.Left * Owner.MaxGraphWidth, Bounds.Top,
                                  Bounds.Width * Owner.MaxGraphWidth, Bounds.Height);
            dc.PushGuidelineSet(CreateGuidelineSet(bounds));
            dc.DrawRectangle(Style.BackColor, Style.Border, bounds);

            // ...
            int index = 0;
            bool trimmed = false;
            double margin = DefaultMargin;

            // Start the text in the visible area.
            bool startsInView = bounds.Left >= visibleArea.Left;
            double offsetX = startsInView ? bounds.Left : visibleArea.Left;
            double maxWidth = bounds.Width - 2 * DefaultMargin;

            if (!startsInView) {
                maxWidth -= visibleArea.Left - bounds.Left;
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
        public List<ProfileCallTreeNode> ReplacedNodes { get; }
        public int ReplacedStartIndex { get; }

        public FlameGraphGroupNode(FlameGraphNode parentNode, int startIndex, List<ProfileCallTreeNode> replacedNodes, TimeSpan weight, int depth) :
            base(null, weight, depth) {
            Parent = parentNode;
            ReplacedStartIndex = startIndex;
            ReplacedNodes = replacedNodes;
        }

        public override void Draw(Rect visibleArea, DrawingContext dc) {
        }
    }

    public class FlameGraph {
        private Dictionary<ProfileCallTreeNode, FlameGraphNode> treeNodeToFgNodeMap_;
        private FlameGraphNode rootNode_;
        private TimeSpan rootWeight_;
        private double rootWeightReciprocal_;
        private double rootDurationReciprocal_;

        public ProfileCallTree CallTree { get; set; }

        public FlameGraphNode RootNode {
            get => rootNode_;
            set {
                rootNode_ = value;

                if (rootNode_.Duration.Ticks != 0) {
                    rootDurationReciprocal_ = 1.0 / (double)rootNode_.Duration.Ticks;
                }
            }
        }

        public TimeSpan RootWeight {
            get => rootWeight_;
            set {
                rootWeight_ = value;

                if (rootWeight_.Ticks != 0) {
                    rootWeightReciprocal_ = 1.0 / (double)rootWeight_.Ticks;
                }
            }
        }

        public FlameGraph(ProfileCallTree callTree) {
            CallTree = callTree;
            treeNodeToFgNodeMap_ = new Dictionary<ProfileCallTreeNode, FlameGraphNode>();
        }

        public FlameGraphNode GetNode(ProfileCallTreeNode node) {
            return treeNodeToFgNodeMap_.GetValueOrDefault(node);
        }

        public void BuildTimeline(ProfileData data) {
            Trace.WriteLine($"Timeline Samples: {data.Samples.Count}");
            data.Samples.Sort((a, b) => a.Sample.Time.CompareTo(b.Sample.Time));

            var flameNode = new FlameGraphNode(null, RootWeight, 0);
            flameNode.StartTime = TimeSpan.MaxValue;
            flameNode.EndTime = TimeSpan.MinValue;

            foreach (var (sample, stack) in data.Samples) {
                AddSample(flameNode, sample, stack);

                flameNode.StartTime = TimeSpan.FromTicks(Math.Min(flameNode.StartTime.Ticks, sample.Time.Ticks));
                flameNode.EndTime = TimeSpan.FromTicks(Math.Max(flameNode.EndTime.Ticks, sample.Time.Ticks + sample.Weight.Ticks));
                flameNode.Weight = flameNode.EndTime - flameNode.StartTime + sample.Weight;
            }

            flameNode.Duration = flameNode.EndTime - flameNode.StartTime;
            RootNode = flameNode;
            RootWeight = flameNode.Weight;
        }

        private void AddSample(FlameGraphNode rootNode, ProfileSample sample, ETWProfileDataProvider.ResolvedProfileStack stack) {
            var node = rootNode;
            int depth = 0;

            for (int k = stack.FrameCount - 1; k >= 0; k--) {
                var resolvedFrame = stack.StackFrames[k];

                // if (resolvedFrame.IsUnknown) {
                //     continue;
                // }

                if (resolvedFrame.Function == null) {
                    continue;
                }

                FlameGraphNode targetNode = null;

                if (node.HasChildren) {
                    for (int i = node.Children.Count - 1; i >= 0; i--) {
                        var child = node.Children[i];

                        if (!child.CallTreeNode.Function.Equals(resolvedFrame.Function)) {
                            break;
                        }

                        //if (child.EndTime <= sample.Time) {
                        targetNode = child;
                        break;
                        //}
                    }
                }

                if (targetNode == null) {
                    targetNode = new FlameGraphNode(new ProfileCallTreeNode(resolvedFrame.DebugInfo, resolvedFrame.Function), TimeSpan.Zero, depth);
                    node.Children ??= new List<FlameGraphNode>();
                    node.Children.Add(targetNode);
                    targetNode.StartTime = sample.Time;
                    targetNode.EndTime = sample.Time + sample.Weight;
                    targetNode.Parent = targetNode;
                }
                else {
                    targetNode.StartTime = TimeSpan.FromTicks(Math.Min(targetNode.StartTime.Ticks, sample.Time.Ticks));
                    targetNode.EndTime = TimeSpan.FromTicks(Math.Max(targetNode.EndTime.Ticks, sample.Time.Ticks + sample.Weight.Ticks));
                }


                if (k > 0) {
                    node.ChildrenWeight += sample.Weight;
                }

                //targetNode.Weight += sample.Weight;
                targetNode.Weight = targetNode.EndTime - targetNode.StartTime + sample.Weight;

                node = targetNode;
                depth++;
            }
        }

        public void Build(ProfileCallTreeNode rootNode) {
            if (rootNode == null) {
                // Make on dummy root node that hosts all real root nodes.
                RootWeight = CallTree.TotalRootNodesWeight;
                var flameNode = new FlameGraphNode(null, RootWeight, 0);
                RootNode = Build(flameNode, CallTree.RootNodes, 0);
            }
            else {
                RootWeight = rootNode.Weight;
                var flameNode = new FlameGraphNode(rootNode, rootNode.Weight, 0);
                RootNode = Build(flameNode, rootNode.Children, 0);
            }
        }

        private FlameGraphNode Build(FlameGraphNode flameNode,
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
                var childNode = Build(childFlameNode, child.Children, depth + 1);
                childNode.Parent = flameNode;
                flameNode.Children.Add(childNode);
                treeNodeToFgNodeMap_[child] = childFlameNode;
            }

            return flameNode;
        }

        public double ScaleWeight(TimeSpan weight) {
            return (double)weight.Ticks * rootWeightReciprocal_;
        }

        public double ScaleStartTime(TimeSpan time) {
            return (double)(time.Ticks - RootNode.StartTime.Ticks) / rootDurationReciprocal_;
        }

        public double ScaleDuration(TimeSpan startTime, TimeSpan endTime) {
            return (double)(endTime.Ticks - startTime.Ticks) / rootDurationReciprocal_;
        }

        public double InverseScaleWeight(TimeSpan weight) {
            return (double)RootWeight.Ticks / weight.Ticks;
        }
    }

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
            palettes_[ProfileCallTreeNodeKind.Managed] = ColorPalette.HeatMap2;

            defaultBorder_ = ColorPens.GetPen(Colors.Black, 0.5);
            nodeHeight_ = DefaultNodeHeight; //? TODO: Option
            font_ = new Typeface("Segoe UI");
            nameFont_ = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.DemiBold, FontStretch.FromOpenTypeStretch(5));
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
            node.WeightTextColor = Brushes.DarkGreen;
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

            foreach (var node in nodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
                node.Draw(visibleArea_, graphDC);

                if (!nodeLayoutRecomputed && node.Bounds.Width < minVisibleRectWidth_) {
                    shrinkingNodes++;
                }
            }

            if (shrinkingNodes > 0) {
                //? TODO: Removing from the quad tree is very slow,
                //? recompute the entire layout instead...
                //! Consider a faster implementation or other kind of spatial tree.
                nodeLayoutComputed_ = false;
                RedrawGraph();
                return;
            }

            DrawDummyNodes(graphDC);
            //? DrawTimeBar(graphDC);
        }

        private void DrawDummyNodes(DrawingContext graphDC) {
            List<FlameGraphGroupNode> enlargeList = null;

            foreach (var node in dummyNodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
                // Reconsider replacing the dummy node.
                if (flameGraph_.ScaleWeight(node.ReplacedNodes[0].Weight) > minVisibleRectWidth_) {
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
#if false
            foreach (var node in enlargeList) {
                if (node.Parent == null) {
                    continue;
                }

                dummyNodesQuadTree_.Remove(node);
                UpdateChildrenNodeLayout(node.Parent, node.Parent.Bounds.Left,
                                         node.Parent.Bounds.Top, node.ReplacedStartIndex);
            }

            // Redraw to show the newly create nodes replacing the dummy ones.
            foreach (var node in nodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
                node.Draw(visibleArea_, graphDC);
            }

            foreach (var node in dummyNodesQuadTree_.GetNodesInside(quadVisibleArea_)) {
                DrawDummyNode(node, graphDC);
            }
#else
            nodeLayoutComputed_ = false;
            RedrawGraph();
            return;
#endif
        }

        private void DrawDummyNode(FlameGraphGroupNode node, DrawingContext graphDC) {
            var scaledBounds = new Rect(node.Bounds.Left * maxWidth_, node.Bounds.Top,
                                        node.Bounds.Width * maxWidth_, node.Bounds.Height);
            graphDC.DrawRectangle(node.Style.BackColor, node.Style.Border, scaledBounds);
            graphDC.DrawRectangle(CreatePlaceholderTiledBrush(8), null, scaledBounds);
        }

        private void DrawCenteredText(string text, double x, double y, DrawingContext dc) {
            var glyphInfo = glyphs_.GetGlyphs(text);
            x = Math.Max(0, x - glyphInfo.TextWidth / 2);
            y = Math.Max(0, y - glyphInfo.TextHeight / 4);

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

        private void UpdateChildrenNodeLayout(FlameGraphNode node, double x, double y, int startIndex = 0) {
            int skippedChildren = 0;

            for (int i = 0; i < startIndex; i++) {
                var childNode = node.Children[i];
                var childWidth = flameGraph_.ScaleWeight(childNode.Weight);
                x += childWidth;
            }

            for (int i = startIndex; i < node.Children.Count; i++) {
                //? If multiple children below width, single patterned rect
                var childNode = node.Children[i];
                var childWidth = flameGraph_.ScaleWeight(childNode.Weight);

                if (skippedChildren == 0) {
                    if (childWidth < minVisibleRectWidth_) {
                        childNode.IsDummyNode = true;
                        CreateSmallWeightDummyNode(node, x, y, i, out skippedChildren);
                    }
                }
                else {
                    childNode.IsDummyNode = true;
                }

                UpdateNodeLayout(childNode, x, y + nodeHeight_, skippedChildren == 0);
                x += childWidth;

                if (skippedChildren > 0) {
                    childNode.IsDummyNode = true;
                    skippedChildren--;
                }
            }
        }

        private void CreateSmallWeightDummyNode(FlameGraphNode node, double x, double y,
                                                int startIndex, out int skippedChildren) {
            TimeSpan totalWeight = TimeSpan.Zero;
            double totalWidth = 0;

            // Collect all the child nodes that have a small weight
            // and replace them by one dummy node.
            var replacedNodes = new List<ProfileCallTreeNode>(node.Children.Count - startIndex);
            int k;

            for (k = startIndex; k < node.Children.Count; k++) {
                var childNode = node.Children[k];
                double childWidth = flameGraph_.ScaleWeight(childNode.Weight);

                // In case child sorting is not by weight, stop extending the range
                // when a larger one is found again.
                if (childWidth > minVisibleRectWidth_) {
                    break;
                }

                replacedNodes.Add(childNode.CallTreeNode);
                totalWidth += childWidth;
                totalWeight += childNode.Weight;
            }

            skippedChildren = k - startIndex; // Caller should ignore these children.

            if (totalWidth < minVisibleRectWidth_) {
                return; // Nothing to draw.
            }

            var replacement = new Rect(x, y + nodeHeight_, totalWidth, nodeHeight_);
            var dummyNode = new FlameGraphGroupNode(node, startIndex, replacedNodes, totalWeight, node.Depth);
            dummyNode.IsDummyNode = true;
            dummyNode.Bounds = replacement;
            dummyNode.Style = PickDummyNodeStyle(node.Style);
            dummyNodesQuadTree_.Insert(dummyNode, replacement);

            //? Could make color darker than level, or less satureded  better
            //! TODO: Make a fake node that has details (sum of weights, tooltip with child count, etc)
        }

        private void DrawTimeBar(DrawingContext graphDC) {
            const double TimeBarHeight = 20;
            const double MinTickDistance = 50;

            var bar = new Rect(visibleArea_.Left, visibleArea_.Bottom - TimeBarHeight,
                visibleArea_.Width, TimeBarHeight);
            graphDC.DrawRectangle(Brushes.Bisque, null, bar);

            double secondTickDist = maxWidth_ / flameGraph_.RootNode.Duration.TotalSeconds;
            double msTickDist = maxWidth_ / flameGraph_.RootNode.Duration.TotalMilliseconds;
            int seconds = 0;

            for (double x = 0; x < maxWidth_; x += secondTickDist) {
                var tickRect = new Rect(x, visibleArea_.Bottom - 4, 4, 4);
                graphDC.DrawRectangle(Brushes.Black, null, tickRect);
                DrawCenteredText($"{seconds} s", tickRect.Left, tickRect.Top, graphDC);

                double subTicks = secondTickDist / MinTickDistance;
                double subTickDist = secondTickDist / subTicks;
                double timePerSubTick = 1000.0 / subTicks;
                double ms = timePerSubTick;

                for (double y = subTickDist; y < secondTickDist - subTickDist; y += subTickDist) {
                    var msTickRect = new Rect(x + y, visibleArea_.Bottom - 2, 2, 3);
                    graphDC.DrawRectangle(Brushes.DimGray, null, msTickRect);
                    double time = (seconds + ms / 1000);
                    if (subTicks <= 10) {
                        DrawCenteredText($"{time:0.0}", msTickRect.Left, msTickRect.Top, graphDC);
                    }
                    else if (subTicks <= 100) {
                        DrawCenteredText($"{time:0.00}", msTickRect.Left, msTickRect.Top, graphDC);
                    }
                    else {
                        int digits = (int)Math.Ceiling(Math.Log10(subTicks));
                        var timeStr = String.Format("{0:0." + new string('0', digits) + "}", time);
                        DrawCenteredText(timeStr, msTickRect.Left, msTickRect.Top, graphDC);
                    }

                    ms += timePerSubTick;
                }

                seconds++;
            }
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
    }
}