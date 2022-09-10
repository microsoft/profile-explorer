﻿using System;
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
    private FlameGraphNode hoveredNode_;

    private Dictionary<FlameGraphNode, HighlightingStyle> hoverNodes_;
    private Dictionary<FlameGraphNode, HighlightingStyle> markedNodes_;
    private Dictionary<FlameGraphNode, HighlightingStyle> selectedNodes_;

    public double MaxWidth => renderer_.maxWidth_;
    public Rect VisibleArea => renderer_.visibleArea_;

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

    private void HighlightNode(FlameGraphNode node, HighlighingType type, bool includeParents = false) {
        var group = GetHighlightedNodeGroup(type);
        group[node] = node.Style;

        node.Style = PickHoveredNodeStyle(node.Style);
        node.Draw();

        if (includeParents && node.Parent != null) {
            HighlightNode(node.Parent, type, true);
        }
    }


    private void ResetHighlightedNodes(HighlighingType type, bool includeParents = false) {
        var group = GetHighlightedNodeGroup(type);

        foreach (var pair in group) {
            pair.Key.Style = pair.Value;
            pair.Key.Draw();

            if (includeParents) {
                FlameGraphNode parentNode = pair.Key.Parent;

                while (parentNode != null) {
                    if (group.TryGetValue(parentNode, out var oldStyle)) {
                        parentNode.Style = oldStyle;
                        parentNode.Draw();
                    }

                    parentNode = parentNode.Parent;
                }
            }
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
                ResetHighlightedNodes(HighlighingType.Hovered);
                ResetHighlightedNodes(HighlighingType.Selected);
                HighlightNode(graphNode, HighlighingType.Selected, true);
            }
        }
        else {
            ResetHighlightedNodes(HighlighingType.Hovered);
            ResetHighlightedNodes(HighlighingType.Selected, true);
        }
    }

    public async Task Initialize(ProfileCallTree callTree, double maxWidth, Rect visibleArea) {
        flameGraph_ = new FlameGraph();
        await Task.Run(() => flameGraph_.Build(callTree));

        renderer_ = new FlameGraphRenderer(flameGraph_, maxWidth, visibleArea);
        graphVisual_ = renderer_.Render();
        AddVisualChild(graphVisual_);
        AddLogicalChild(graphVisual_);
        UpdateMaxWidth(maxWidth);
    }

    public void UpdateMaxWidth(double maxWidth) {
        renderer_.UpdateMaxWidth(maxWidth);
        InvalidateMeasure();
    }

    public void AdjustMaxWidth(double amount) {
        renderer_.UpdateMaxWidth(renderer_.maxWidth_ + amount);
        InvalidateMeasure();
    }

    // stack of  prev state to go back, maxWidh, panning, move to FgPanel

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

    public void UpdateVisibleArea(Rect visibleArea) {
        renderer_.visibleArea_ = visibleArea;
        renderer_.Redraw();
    }

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