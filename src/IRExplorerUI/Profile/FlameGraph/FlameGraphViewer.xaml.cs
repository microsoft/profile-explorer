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
    private bool isTimelineView_;
    private bool initialized_;
    private Brush markedNodeBackColor_;
    private Pen searchResultBorderColor_;
    private Pen selectedNodeBorderColor_;

    private Dictionary<FlameGraphNode, HighlightingStyle> hoverNodes_;
    private Dictionary<FlameGraphNode, HighlightingStyle> markedNodes_;
    private Dictionary<FlameGraphNode, HighlightingStyle> selectedNodes_;

    public new bool IsInitialized => initialized_;
    public FlameGraph FlameGraph => flameGraph_;
    public double MaxGraphWidth => renderer_.MaxGraphWidth;
    public Rect VisibleArea => renderer_.VisibleArea;
    public bool IsZoomed => Math.Abs(MaxGraphWidth - VisibleArea.Width) > 1;
    public FlameGraphNode SelectedNode => selectedNode_;
    public ISession Session { get; set; }

    public FlameGraphViewer() {
        InitializeComponent();
        hoverNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
        markedNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
        selectedNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
        markedNodeBackColor_ = ColorBrushes.GetBrush("#D0E3F1");
        selectedNodeBorderColor_ = ColorPens.GetPen("#0F92EF", 2);
        searchResultBorderColor_ = ColorPens.GetPen(Colors.Black, 2);
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

    private void MarkNode(FlameGraphNode node, HighlighingType type) {
        var group = GetHighlightedNodeGroup(type);
        group[node] = node.Style;

        node.Style = type switch {
            HighlighingType.Hovered => PickHoveredNodeStyle(node.Style),
            HighlighingType.Selected => PickSelectedNodeStyle(node.Style),
            HighlighingType.Marked => PickMarkedNodeStyle(node, node.Style),
            _ => node.Style
        };
    }

    private void ResetHighlightedNodes(HighlighingType type, bool includeParents = false, bool redraw = true) {
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

        if (redraw) {
            renderer_.Redraw();
        }
    }

    private Dictionary<FlameGraphNode, HighlightingStyle> GetHighlightedNodeGroup(HighlighingType type) {
        return type switch {
            HighlighingType.Hovered => hoverNodes_,
            HighlighingType.Selected => selectedNodes_,
            HighlighingType.Marked => markedNodes_,
            _ => throw new InvalidOperationException("Unsupported highlighting type")
        };
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
        var newColor = ColorUtils.AdjustLight(((SolidColorBrush)style.BackColor).Color, 0.9f);
        return new HighlightingStyle(newColor, style.Border);
    }

    private HighlightingStyle PickSelectedNodeStyle(HighlightingStyle style) {
        var newColor = markedNodeBackColor_;
        var newPen = ColorPens.GetBoldPen(Colors.Black);
        return new HighlightingStyle(newColor, newPen);
    }

    private HighlightingStyle PickSelectedParentNodeStyle(HighlightingStyle style) {
        var newColor = ColorUtils.AdjustLight(((SolidColorBrush)style.BackColor).Color, 0.95f);
        return new HighlightingStyle(newColor, style.Border);
    }

    private HighlightingStyle PickMarkedNodeStyle(FlameGraphNode node, HighlightingStyle style) {
        var newColor = node.SearchResult.HasValue ? node.Style.BackColor : markedNodeBackColor_;
        var newPen = node.SearchResult.HasValue  ? searchResultBorderColor_ : ColorPens.GetBoldPen(Colors.Black);
        return new HighlightingStyle(newColor, newPen);
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        SelectPointedNode(e);
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        SelectPointedNode(e);
        e.Handled = true;
    }

    private void SelectPointedNode(MouseButtonEventArgs e) {
        var point = e.GetPosition(this);
        var graphNode = FindPointedNode(point);

        if (graphNode != null) {
            SelectNode(graphNode);
        }
        else {
            ClearSelection();
        }
    }

    public void SelectNode(FlameGraphNode graphNode) {
        if (selectedNode_ != graphNode) {
            ResetHighlightedNodes(HighlighingType.Hovered);
            ResetHighlightedNodes(HighlighingType.Selected);
            HighlightNode(graphNode, HighlighingType.Selected, true);
            selectedNode_ = graphNode;
        }
    }

    public void SelectNodes(List<FlameGraphNode> nodes) {
        ResetHighlightedNodes(HighlighingType.Hovered);
        ResetHighlightedNodes(HighlighingType.Selected);

        foreach (var node in nodes) {
            MarkNode(node, HighlighingType.Selected);

        }

        renderer_.Redraw();
    }

    public void MarkSearchResultNodes(List<FlameGraphNode> searchResultNodes) {
        foreach (var node in searchResultNodes) {
            MarkNode(node, HighlighingType.Marked);
        }

        renderer_.Redraw();
    }

    public void ResetSearchResultNodes(List<FlameGraphNode> nodes, bool redraw = true) {
        ResetHighlightedNodes(HighlighingType.Marked, includeParents: false, redraw);
        flameGraph_.ResetSearchResults(nodes);

        if (redraw) {
            renderer_.Redraw();
        }
    }

    public void MarkNodes(List<ProfileCallTreeNode> nodes) {
        foreach (var node in nodes) {
            MarkNodeImpl(node, redraw: false);
        }

        renderer_.Redraw();
    }


    public void MarkNode(ProfileCallTreeNode node) {
        MarkNodeImpl(node, redraw: true);
    }

    private void MarkNodeImpl(ProfileCallTreeNode node, bool redraw) {
        var fgNodes = flameGraph_.GetNodes(node);

        foreach (var fgNode in fgNodes) {
            MarkNode(fgNode, HighlighingType.Marked);
        }

        if (redraw) {
            renderer_.Redraw();
        }
    }

    public void MarkNode(FlameGraphNode node) {
        MarkNode(node, HighlighingType.Marked);
    }

    public void ResetMarkedNodes() {
        ResetHighlightedNodes(HighlighingType.Marked);
    }

    public FlameGraphNode SelectNode(ProfileCallTreeNode graphNode) {
        var nodes = flameGraph_.GetNodes(graphNode);
        SelectNode(nodes[0]);
        return nodes[0];
    }

    public List<FlameGraphNode> SelectNodes(ProfileCallTreeNode graphNode) {
        var nodes = flameGraph_.GetNodes(graphNode);
        SelectNodes(nodes);
        return nodes;
    }

    public List<FlameGraphNode> SelectNodes(List<ProfileCallTreeNode> nodes) {
        var fgNodes = new List<FlameGraphNode>(nodes.Count);

        foreach (var node in nodes) {
            flameGraph_.AppendNodes(node, fgNodes);
        }

        SelectNodes(fgNodes);
        return fgNodes;
    }

    public void ClearSelection() {
        if (!initialized_) {
            return;
        }
        
        ResetHighlightedNodes(HighlighingType.Hovered);
        ResetHighlightedNodes(HighlighingType.Selected, true);
        selectedNode_ = null;
        renderer_.Redraw();
    }

    public async Task Initialize(ProfileCallTree callTree, ProfileCallTreeNode rootNode,
                                 Rect visibleArea, FlameGraphSettings settings, ISession session,
                                 bool isTimelineView = false, int threadId = -1) {
        if (initialized_) {
            Reset();
        }

        Session = session;
        flameGraph_ = new FlameGraph(callTree, Session.CompilerInfo.NameProvider.FormatFunctionName);
        isTimelineView_ = isTimelineView;

        if (isTimelineView_) {
            flameGraph_.BuildTimeline(Session.ProfileData, threadId);
        }
        else {
            await Task.Run(() => flameGraph_.Build(rootNode));
        }

        renderer_ = new FlameGraphRenderer(flameGraph_, visibleArea, settings, isTimelineView_);
        graphVisual_ = renderer_.Setup();
        AddVisualChild(graphVisual_);
        AddLogicalChild(graphVisual_);
        UpdateMaxWidth(renderer_.MaxGraphWidth);
        initialized_ = true;
    }

    public void SettingsUpdated(FlameGraphSettings settings) {
        renderer_.SettingsUpdated(settings);
    }

    public async Task Initialize(ProfileCallTree callTree, Rect visibleArea, FlameGraphSettings settings,
                                 ISession session, bool isTimelineView = false, int threadId = -1) {
        await Initialize(callTree, null, visibleArea, settings, session, isTimelineView, threadId);
    }

    public void UpdateMaxWidth(double maxWidth) {
        if (!initialized_) {
            return;
        }

        renderer_.UpdateMaxWidth(maxWidth);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void AdjustMaxWidth(double amount) {
        if (!initialized_) {
            return;
        }

        renderer_.UpdateMaxWidth(renderer_.MaxGraphWidth + amount);
        InvalidateMeasure();
    }

    public FlameGraphNode FindPointedNode(Point point) {
        if (!initialized_) {
            return null;
        }

        return renderer_.HitTestNode(point);
    }

    public Rect ComputeNodeBounds(FlameGraphNode node) {
        return renderer_.ComputeNodeBounds(node);
    }

    public Point ComputeNodePosition(FlameGraphNode node) {
        return ComputeNodeBounds(node).TopLeft;
    }

    public Size ComputeNodeSize(FlameGraphNode node) {
        return ComputeNodeBounds(node).Size;
    }

    protected override int VisualChildrenCount => 1;

    public void UpdateVisibleArea(Rect visibleArea) {
        if (!initialized_) {
            return;
        }

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
        if (!initialized_) {
            return;
        }

        RemoveVisualChild(graphVisual_);
        RemoveLogicalChild(graphVisual_);
        hoverNodes_.Clear();
        markedNodes_.Clear();
        selectedNodes_.Clear();
        selectedNode_ = null;
        graphVisual_ = null;
        flameGraph_ = null;
        renderer_ = null;
        initialized_ = false;
    }
}