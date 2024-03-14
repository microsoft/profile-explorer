// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

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
  private Brush selectedNodeBackColor_;
  private Pen searchResultBorderColor_;
  private Pen selectedNodeBorderColor_;
  private Pen markedNodeBorderColor_;
  private Dictionary<FlameGraphNode, HighlightingStyle> hoverNodes_;
  private Dictionary<FlameGraphNode, HighlightingStyle> markedNodes_;
  private Dictionary<FlameGraphNode, HighlightingStyle> fixedMarkedNodes_;
  private Dictionary<FlameGraphNode, HighlightingStyle> selectedNodes_;
  private FlameGraphSettings settings_;

  public FlameGraphViewer() {
    InitializeComponent();
    hoverNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
    markedNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
    fixedMarkedNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
    selectedNodes_ = new Dictionary<FlameGraphNode, HighlightingStyle>();
    SetupEvents();
  }

  public new bool IsInitialized => initialized_;
  public FlameGraph FlameGraph => flameGraph_;
  public double MaxGraphWidth => renderer_.MaxGraphWidth;
  public Rect VisibleArea => renderer_.VisibleArea;
  public bool IsZoomed => Math.Abs(MaxGraphWidth - VisibleArea.Width) > 1;
  public FlameGraphNode SelectedNode => selectedNode_;
  public ISession Session { get; set; }
  public HighlightingStyle SelectedNodeStyle { get; private set; }
  public HighlightingStyle MarkedNodeStyle { get; private set; }
  public List<FlameGraphNode> SelectedNodes => selectedNodes_.ToKeyList();
  protected override int VisualChildrenCount => 1;

  public HighlightingStyle MarkedColoredNodeStyle(Color color) {
    return new HighlightingStyle(color, markedNodeBorderColor_);
  }

  public void RestoreFixedMarkedNodes() {
    if (fixedMarkedNodes_.Count == 0) {
      return;
    }

    foreach (var pair in fixedMarkedNodes_) {
      if (pair.Key.HasFunction) {
        // Map the call tree node to the current flame graph node,
        // needed in case the flame graph got rebuilt.
        var node = flameGraph_.GetFlameGraphNode(pair.Key.CallTreeNode);

        if (node != null) {
          MarkNodeImpl(node, HighlighingType.Marked, pair.Value);
        }
      }
    }

    renderer_.Redraw();
  }

  public void ResetNodeHighlighting() {
    ResetHighlightedNodes(HighlighingType.Selected);
    ResetHighlightedNodes(HighlighingType.Hovered);
    selectedNodes_.Clear();
    selectedNode_ = null;
    hoveredNode_ = null;
    Session.SetApplicationStatus("");
  }

  public void SelectNode(FlameGraphNode graphNode, bool append = false) {
    if (selectedNode_ != graphNode) {
      if (!append) {
        ResetNodeHighlighting();
      }

      HighlightNode(graphNode, HighlighingType.Selected);
      selectedNode_ = graphNode; // Last selected node.
    }
    else if (append && selectedNodes_.ContainsKey(graphNode)) {
      selectedNodes_.Remove(graphNode);
      selectedNode_ = null;
    }
  }

  public void SelectNodes(List<FlameGraphNode> nodes) {
    ResetNodeHighlighting();

    foreach (var node in nodes) {
      MarkNodeImpl(node, HighlighingType.Selected);
    }

    renderer_.Redraw();
  }

  public void MarkSearchResultNodes(List<FlameGraphNode> searchResultNodes) {
    foreach (var node in searchResultNodes) {
      MarkNodeImpl(node, HighlighingType.Marked);
    }

    renderer_.Redraw();
  }

  public void ResetSearchResultNodes(List<FlameGraphNode> nodes, bool redraw = true) {
    ResetHighlightedNodes(HighlighingType.Marked, redraw);
    flameGraph_.ResetSearchResults(nodes);

    if (redraw) {
      renderer_.Redraw();
    }
  }

  public void MarkNodes(List<ProfileCallTreeNode> nodes, HighlightingStyle style, bool overwriteStyle) {
    foreach (var node in nodes) {
      MarkNodeNoRedraw(node, style, overwriteStyle);
    }

    renderer_.Redraw();
  }

  public void MarkNode(FlameGraphNode node, HighlightingStyle style) {
    MarkNodeImpl(node, HighlighingType.Marked, style, true);
    renderer_.Redraw();
  }

  public void ResetMarkedNodes(bool clearFixedNodes) {
    if (clearFixedNodes) {
      fixedMarkedNodes_.Clear();
    }

    ResetHighlightedNodes(HighlighingType.Marked);
  }

  public FlameGraphNode SelectNode(ProfileCallTreeNode graphNode) {
    var nodes = flameGraph_.GetNodes(graphNode);

    // Because the flame graph can be rooted at a different node then
    // the call tree, parts of the call tree may not have a flame graph node.
    if (nodes is {Count: > 0}) {
      SelectNode(nodes[0]);
      return nodes[0];
    }

    return null;
  }

  public List<FlameGraphNode> SelectNodes(ProfileCallTreeNode graphNode) {
    var nodes = flameGraph_.GetNodes(graphNode);
    SelectNodes(nodes);
    return nodes;
  }

  public List<FlameGraphNode> SelectNodes(List<ProfileCallTreeNode> nodes) {
    if (nodes == null || nodes.Count == 0) {
      return null;
    }

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

    ResetNodeHighlighting();
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

    settings_ = settings;
    ReloadSettings();

    renderer_ = new FlameGraphRenderer(flameGraph_, visibleArea, settings, isTimelineView_);
    renderer_.SelectedNodes = selectedNodes_;
    graphVisual_ = renderer_.Setup();

    AddVisualChild(graphVisual_);
    AddLogicalChild(graphVisual_);
    UpdateMaxWidth(renderer_.MaxGraphWidth);
    initialized_ = true;
  }

  private void ReloadSettings() {
    markedNodeBackColor_ = ColorBrushes.GetBrush(settings_.SearchedNodeColor);
    markedNodeBorderColor_ = ColorPens.GetPen(settings_.NodeBorderColor, 2);
    selectedNodeBackColor_ = ColorBrushes.GetBrush(settings_.SelectedNodeColor);
    selectedNodeBorderColor_ = ColorPens.GetPen(settings_.SelectedNodeBorderColor, 2);
    searchResultBorderColor_ = ColorPens.GetPen(settings_.SearchedNodeBorderColor, 2);
    SelectedNodeStyle = new HighlightingStyle(selectedNodeBackColor_, selectedNodeBorderColor_);
    MarkedNodeStyle = new HighlightingStyle(markedNodeBackColor_, markedNodeBorderColor_);
  }

  public void SettingsUpdated(FlameGraphSettings settings) {
    settings_ = settings;
    ReloadSettings();

    renderer_.SettingsUpdated(settings);
    InvalidateMeasure();
    InvalidateVisual();
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

  public void UpdateVisibleArea(Rect visibleArea) {
    if (!initialized_) {
      return;
    }

    renderer_.UpdateVisibleArea(visibleArea);
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

  protected override void OnMouseLeave(MouseEventArgs e) {
    if (!IsInitialized) {
      return;
    }

    ResetHighlightedNodes(HighlighingType.Hovered);
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
        HighlightNode(graphNode, HighlighingType.Hovered);
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

  private void HighlightNode(FlameGraphNode node, HighlighingType type) {
    node.Style = type switch {
      HighlighingType.Hovered => PickHoveredNodeStyle(node.Style),
      HighlighingType.Selected => PickSelectedNodeStyle(node.Style),
      _ => node.Style
    };

    var group = GetHighlightedNodeGroup(type);
    group[node] = node.Style;
    renderer_.Redraw();
  }

  private void MarkNodeImpl(FlameGraphNode node, HighlighingType type, HighlightingStyle style = null,
                            bool overwriteStyle = false) {
    if (type == HighlighingType.Marked && !overwriteStyle &&
        markedNodes_.TryGetValue(node, out var markedStyle)) {
      fixedMarkedNodes_[node] = markedStyle; // Save current marked style.
    }

    node.Style = type switch {
      HighlighingType.Hovered => PickHoveredNodeStyle(style),
      HighlighingType.Selected => PickSelectedNodeStyle(style),
      HighlighingType.Marked => PickMarkedNodeStyle(node, style),
      _ => node.Style
    };

    var group = GetHighlightedNodeGroup(type);
    group[node] = node.Style;

    if (type == HighlighingType.Marked && overwriteStyle) {
      fixedMarkedNodes_[node] = style;
    }
  }

  private void ResetHighlightedNodes(HighlighingType type, bool redraw = true) {
    var group = GetHighlightedNodeGroup(type);

    if (group.Count == 0) {
      return;
    }

    var tempNodes = new List<FlameGraphNode>(group.Keys);
    group.Clear();

    foreach (var node in tempNodes) {
      RestoreNodeStyle(node);
    }

    if (redraw) {
      renderer_.Redraw();
    }
  }

  private void RestoreNodeStyle(FlameGraphNode node) {
    if (!markedNodes_.TryGetValue(node, out var style) &&
        !selectedNodes_.TryGetValue(node, out style) &&
        !hoverNodes_.TryGetValue(node, out style)) {
      // Check marked directly by user.
      if (fixedMarkedNodes_.TryGetValue(node, out style)) {
        MarkNodeImpl(node, HighlighingType.Marked, style);
        return;
      }

      style = renderer_.GetNodeStyle(node);
    }

    node.Style = style;
  }

  private Dictionary<FlameGraphNode, HighlightingStyle> GetHighlightedNodeGroup(HighlighingType type) {
    return type switch {
      HighlighingType.Hovered => hoverNodes_,
      HighlighingType.Selected => selectedNodes_,
      HighlighingType.Marked => markedNodes_,
      _ => throw new InvalidOperationException("Unsupported highlighting type")
    };
  }

  private HighlightingStyle ApplyBorderToStyle(HighlightingStyle style, Pen border) {
    return new HighlightingStyle(style.BackColor, border);
  }

  private HighlightingStyle PickHoveredNodeStyle(HighlightingStyle style) {
    var newColor = ColorUtils.AdjustLight(((SolidColorBrush)style.BackColor).Color, 0.9f);
    return new HighlightingStyle(newColor, style.Border);
  }

  private HighlightingStyle PickSelectedNodeStyle(HighlightingStyle style) {
    var newColor = selectedNodeBackColor_;
    var newPen = selectedNodeBorderColor_;
    return new HighlightingStyle(newColor, newPen);
  }

  private HighlightingStyle PickMarkedNodeStyle(FlameGraphNode node, HighlightingStyle style = null) {
    if (style != null) {
      return style;
    }

    var newColor = markedNodeBackColor_;
    var newPen = markedNodeBorderColor_;

    if (node.SearchResult.HasValue) {
      newPen = searchResultBorderColor_;
    }

    return new HighlightingStyle(newColor, newPen);
  }

  private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
    SelectPointedNode(e);
  }

  private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    SelectPointedNode(e);
  }

  private void SelectPointedNode(MouseButtonEventArgs e) {
    var point = e.GetPosition(this);
    var graphNode = FindPointedNode(point);

    if (graphNode != null) {
      SelectNode(graphNode, Utils.IsControlModifierActive());
    }
    else {
      ClearSelection();
    }
  }

  private void MarkNodeNoRedraw(ProfileCallTreeNode node, HighlightingStyle style, bool overwriteStyle) {
    var fgNodes = flameGraph_.GetNodes(node);

    foreach (var fgNode in fgNodes) {
      MarkNodeImpl(fgNode, HighlighingType.Marked, style, overwriteStyle);
    }
  }
}