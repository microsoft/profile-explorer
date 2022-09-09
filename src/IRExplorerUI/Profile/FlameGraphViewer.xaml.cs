using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;

namespace IRExplorerUI.Profile;

public partial class FlameGraphViewer : FrameworkElement {
    private FlameGraph flameGraph_;
    private FlameGraphRenderer renderer_;
    private DrawingVisual graphVisual_;
    private double maxWidth_;
    private FlameGraphNode hoveredNode_;

    private Dictionary<FlameGraphNode, HighlightingStyle> hoverNodes_;
    private Dictionary<FlameGraphNode, HighlightingStyle> markedNodes_;
    private Dictionary<FlameGraphNode, HighlightingStyle> selectedNodes_;

    private Dictionary<FlameGraphNode, HighlightingStyle> GetHighlightedNodeGroup(HighlighingType type) {
        return type switch
        {
            HighlighingType.Hovered => hoverNodes_,
            HighlighingType.Selected => selectedNodes_,
            HighlighingType.Marked => markedNodes_,
            _ => throw new InvalidOperationException("Unsupported highlighting type")
        };
    }

    public FlameGraphViewer() {
        InitializeComponent();

        hoverNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
        markedNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
        selectedNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
        SetupEvents();
    }

    private void SetupEvents() {
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonDown += OnMouseRightButtonDown;
        MouseMove += OnMouseMove;
    }

    private void OnMouseMove(object sender, MouseEventArgs e) {
        var point = e.GetPosition(this);
        var graphNode = FindPointedNode(point);

        if (graphNode != null) {
            //Trace.WriteLine($"Over {graphNode.Node?.FunctionName}");
            if (hoveredNode_ != graphNode) {
                ResetHighlightedNodes(HighlighingType.Hovered);
                HighlightNode(graphNode, HighlighingType.Hovered, false);
                hoveredNode_ = graphNode;
            }
        }
        else {
            ResetHighlightedNodes(HighlighingType.Hovered);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e) {
        ResetHighlightedNodes(HighlighingType.Hovered);
    }

    private void HighlightNode(FlameGraphNode node, HighlighingType type, bool includeParents) {
        var group = GetHighlightedNodeGroup(type);
        group[node] = node.Style;

        node.Style = PickHoveredNodeStyle(node.Style);
        node.Draw();

        if (includeParents && node.Parent != null) {
            HighlightNode(node.Parent, type, true);
        }
    }


    private void ResetHighlightedNodes(HighlighingType hovered) {
        var group = GetHighlightedNodeGroup(hovered);

        foreach (var pair in group) {
            pair.Key.Style = pair.Value;
            pair.Key.Draw();
        }

        group.Clear();
    }

    private HighlightingStyle ApplyBorderToStyle(HighlightingStyle style, Pen border) {
        return new HighlightingStyle(style.BackColor, border);
    }

    private HighlightingStyle PickHoveredNodeStyle(HighlightingStyle style) {
        var newColor = ColorUtils.AdjustLight(((SolidColorBrush)style.BackColor).Color, 0.9f);
        return new HighlightingStyle(newColor, style.Border);
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        var point = e.GetPosition(this);
        var graphNode = FindPointedNode(point);

        if (graphNode != null) {
            //Trace.WriteLine($"Over {graphNode.Node?.FunctionName}");
            if (!selectedNodes_.ContainsKey(graphNode)) {
                ResetHighlightedNodes(HighlighingType.Selected);
                HighlightNode(graphNode, HighlighingType.Selected, true);
            }
        }
        else {
            ResetHighlightedNodes(HighlighingType.Selected);
        }
    }

    public async Task Initialize(ProfileCallTree callTree, double maxWidth) {
        flameGraph_ = new FlameGraph();
        await Task.Run(() => flameGraph_.Build(callTree));

        maxWidth_ = maxWidth;
        renderer_ = new FlameGraphRenderer(flameGraph_, maxWidth);
        graphVisual_ = renderer_.Render();
        AddVisualChild(graphVisual_);
        AddLogicalChild(graphVisual_);
        UpdateMaxWidth(maxWidth);
    }

    public void UpdateMaxWidth(double maxWidth) {
        maxWidth_ = maxWidth;
        Refresh();
    }

    public void Refresh() {
        renderer_.UpdateMaxWidth(maxWidth_);
        InvalidateMeasure();
    }

    public FlameGraphNode FindPointedNode(Point point) {
        var result = VisualTreeHelper.HitTest(this, point);

        if (result == null) {
            return null;
        }

        if (result.VisualHit is DrawingVisual visual) {
            return visual.ReadLocalValue(TagProperty) as FlameGraphNode;
        }

        return null;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) {
        return graphVisual_;
    }

    protected override Size MeasureOverride(Size availableSize) {
        if (graphVisual_ == null) {
            return new Size(0, 0);
        }

        var bounds = graphVisual_.ContentBounds;
        bounds.Union(graphVisual_.DescendantBounds);
        return new Size(bounds.Width, bounds.Height);
    }
}