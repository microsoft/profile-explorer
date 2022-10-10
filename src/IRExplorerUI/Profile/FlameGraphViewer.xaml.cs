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
    private FlameGraphNode hoveredNode_;
    private FlameGraphNode selectedNode_;
    private bool initialized_;

    private Dictionary<FlameGraphNode, HighlightingStyle> hoverNodes_;
    private Dictionary<FlameGraphNode, HighlightingStyle> markedNodes_;
    private Dictionary<FlameGraphNode, HighlightingStyle> selectedNodes_;

    public bool IsInitialized => initialized_;
    public FlameGraph FlameGraph => flameGraph_;
    public double MaxGraphWidth => renderer_.MaxGraphWidth;
    public Rect VisibleArea => renderer_.VisibleArea;
    public bool IsZoomed => Math.Abs(MaxGraphWidth - VisibleArea.Width) > 1;

    private Dictionary<FlameGraphNode, HighlightingStyle> GetHighlightedNodeGroup(HighlighingType type) {
        return type switch {
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
                e.Handled = true;
            }
        }
        else {
            ResetHighlightedNodes(HighlighingType.Hovered);
            hoveredNode_ = null;
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e) {
        if (!IsInitialized) {
            return;
        }

        ResetHighlightedNodes(HighlighingType.Hovered);
    }

    private void HighlightNode(FlameGraphNode node, HighlighingType type, bool includeParents = false, bool isParent = false) {
        var group = GetHighlightedNodeGroup(type);
        group[node] = node.Style;

        node.Style = type switch {
            HighlighingType.Hovered => isParent ? PickHoveredParentNodeStyle(node.Style) : PickHoveredNodeStyle(node.Style),
            HighlighingType.Selected => isParent ? PickSelectedParentNodeStyle(node.Style) : PickSelectedNodeStyle(node.Style),
            _ => node.Style
        };

        if (includeParents && node.Parent != null) {
            HighlightNode(node.Parent, type, includeParents, true);
        }

        if (!isParent) {
            renderer_.Redraw();
        }
    }


    private void ResetHighlightedNodes(HighlighingType type, bool includeParents = false) {
        var group = GetHighlightedNodeGroup(type);

        if (group.Count == 0) {
            return;
        }

        foreach (var pair in group) {
            pair.Key.Style = pair.Value;

            if (includeParents) {
                FlameGraphNode parentNode = pair.Key.Parent;

                while (parentNode != null) {
                    if (group.TryGetValue(parentNode, out var oldStyle)) {
                        parentNode.Style = oldStyle;
                    }

                    parentNode = parentNode.Parent;
                }
            }
        }

        group.Clear();
        renderer_.Redraw();
    }

    public void ResetNodeHighlighting() {
        ResetHighlightedNodes(HighlighingType.Hovered, true);
        ResetHighlightedNodes(HighlighingType.Selected, true);
        hoveredNode_ = null;
        selectedNode_ = null;
    }

    private HighlightingStyle ApplyBorderToStyle(HighlightingStyle style, Pen border) {
        return new HighlightingStyle(style.BackColor, border);
    }

    private HighlightingStyle PickHoveredParentNodeStyle(HighlightingStyle style) {
        var newColor = ColorUtils.AdjustLight(((SolidColorBrush)style.BackColor).Color, 0.95f);
        return new HighlightingStyle(newColor, style.Border);
    }

    private HighlightingStyle PickHoveredNodeStyle(HighlightingStyle style) {
        var newColor = ColorUtils.AdjustLight(((SolidColorBrush)style.BackColor).Color, 0.85f);
        return new HighlightingStyle(newColor, style.Border);
    }

    private HighlightingStyle PickSelectedNodeStyle(HighlightingStyle style) {
        var newColor = ColorUtils.AdjustLight(((SolidColorBrush)style.BackColor).Color, 0.85f);
        //var newColor = style.BackColor;
        return new HighlightingStyle(newColor, ColorPens.GetBoldPen(style.Border));
    }

    private HighlightingStyle PickSelectedParentNodeStyle(HighlightingStyle style) {
        var newColor = ColorUtils.AdjustLight(((SolidColorBrush)style.BackColor).Color, 0.95f);
        //var newColor = style.BackColor;
        return new HighlightingStyle(newColor, style.Border);
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        var point = e.GetPosition(this);
        var graphNode = FindPointedNode(point);

        if (graphNode != null) {
            SelectNode(graphNode);
        }
        else {
            ClearSelection();
        }

        e.Handled = true;
    }

    public void SelectNode(FlameGraphNode graphNode) {
        if (selectedNode_ != graphNode) {
            ResetHighlightedNodes(HighlighingType.Hovered);
            ResetHighlightedNodes(HighlighingType.Selected);
            HighlightNode(graphNode, HighlighingType.Selected, true);
            selectedNode_ = graphNode;
        }
    }

    public void ClearSelection() {
        ResetHighlightedNodes(HighlighingType.Hovered);
        ResetHighlightedNodes(HighlighingType.Selected, true);
        selectedNode_ = null;
    }

    public async Task Initialize(ProfileCallTree callTree, ProfileCallTreeNode rootNode, Rect visibleArea) {
        if (graphVisual_ != null) {
            Reset();
        }

        initialized_ = true;
        flameGraph_ = new FlameGraph(callTree);

#if true
        await Task.Run(() => flameGraph_.Build(rootNode));
#else
       //var x = (App.Current.MainWindow as ISession).ProfileData;
       //flameGraph_.BuildTimeli\ne(x);
#endif

        Trace.WriteLine($"Init FlameGraph with visible area {visibleArea}");
        renderer_ = new FlameGraphRenderer(flameGraph_, visibleArea);
        graphVisual_ = renderer_.Setup();
        AddVisualChild(graphVisual_);
        AddLogicalChild(graphVisual_);
        UpdateMaxWidth(renderer_.MaxGraphWidth);
    }

    public async Task Initialize(ProfileCallTree callTree, Rect visibleArea) {
        await Initialize(callTree, null, visibleArea);
    }

    public void UpdateMaxWidth(double maxWidth) {
        renderer_.UpdateMaxWidth(maxWidth);
        InvalidateMeasure();
    }

    public void AdjustMaxWidth(double amount) {
        renderer_.UpdateMaxWidth(renderer_.MaxGraphWidth + amount);
        InvalidateMeasure();
    }

    public FlameGraphNode FindPointedNode(Point point) {
        return renderer_.HitTestNode(point);
    }

    public Point ComputeNodePosition(FlameGraphNode node) {
        return renderer_.ComputeNodePosition(node);
    }

    protected override int VisualChildrenCount => 1;

    public void UpdateVisibleArea(Rect visibleArea) {
        renderer_.UpdateVisibleArea(visibleArea);
    }

    protected override Visual GetVisualChild(int index) {
        return graphVisual_;
    }

    protected override Size MeasureOverride(Size availableSize) {
        if (graphVisual_ == null) {
            return new Size(0, 0);
        }

        return renderer_.GraphArea.Size;
    }

    public void Reset() {
        if (graphVisual_ == null) {
            return;
        }

        RemoveVisualChild(graphVisual_);
        RemoveLogicalChild(graphVisual_);
        graphVisual_ = null;
        flameGraph_ = null;
        renderer_ = null;
        initialized_ = false;
    }
}