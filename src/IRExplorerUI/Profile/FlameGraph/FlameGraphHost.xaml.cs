// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerUI.Controls;
using IRExplorerUI.Utilities;

namespace IRExplorerUI.Profile;

public partial class FlameGraphHost : UserControl, IFunctionProfileInfoProvider, INotifyPropertyChanged {
  private const double ZoomAmount = 500;
  private const double ScrollWheelZoomAmount = 300;
  private const double TimePerFrame = 1000.0 / 60; // ~16.6ms per frame at 60Hz.
  private const double FastPanOffset = 1000;
  private const double DefaultPanOffset = 100;
  private const double ZoomAnimationDuration = TimePerFrame * 10;
  private const double EnlargeAnimationDuration = TimePerFrame * 12;
  private const double ScrollWheelZoomAnimationDuration = TimePerFrame * 8;
  private ProfileCallTree callTree_;
  private bool dragging_;
  private Point draggingStart_;
  private Point draggingViewStart_;
  private double endOffsetX_;
  private FlameGraphNode enlargedNode_;
  private bool ignoreScrollEvent_;
  private double initialOffsetX_;
  private double initialWidth_;
  private DateTime lastWheelZoomTime_;
  private FlameGraphNode rootNode_;
  private FlameGraphSettings settings_;
  private bool showNodePanel_;
  private PopupHoverPreview nodeHoverPreview_;
  private readonly Stack<FlameGraphState> stateStack_;
  private DoubleAnimation widthAnimation_;
  private DoubleAnimation zoomAnimation_;
  private double zoomPointX_;

  public FlameGraphHost() {
    InitializeComponent();
    settings_ = App.Settings.FlameGraphSettings;
    stateStack_ = new Stack<FlameGraphState>();
    SetupEvents();
    DataContext = this;
    ShowNodePanel = true;
  }

  public new bool IsInitialized => GraphViewer.IsInitialized;
  public ISession Session { get; set; }
  public List<FlameGraphNode> SelectedNodes => GraphViewer.SelectedNodes;
  public RelayCommand<object> SelectFunctionCallTreeCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.CallTree);
  });
  public RelayCommand<object> SelectFunctionTimelineCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.Timeline);
  });
  public RelayCommand<object> SelectFunctionSourceCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.Source);
  });
  public RelayCommand<object> CopyFunctionNameCommand => new RelayCommand<object>(async obj => {
    if (GraphViewer.SelectedNode is {HasFunction: true}) {
      string text = Session.CompilerInfo.NameProvider.GetFunctionName(GraphViewer.SelectedNode.Function);
      Clipboard.SetText(text);
    }
  });
  public RelayCommand<object> CopyDemangledFunctionNameCommand => new RelayCommand<object>(async obj => {
    if (GraphViewer.SelectedNode is {HasFunction: true}) {
      var options = FunctionNameDemanglingOptions.Default;
      string text = Session.CompilerInfo.NameProvider.DemangleFunctionName(GraphViewer.SelectedNode.Function, options);
      Clipboard.SetText(text);
    }
  });
  public RelayCommand<object> CopyFunctionDetailsCommand => new RelayCommand<object>(async obj => {
    if (SelectedNodes.Count > 0) {
      var funcList = new List<SearchableProfileItem>();

      foreach (var item in SelectedNodes) {
        if (item.HasFunction) {
          funcList.Add(item);
        }
      }

      SearchableProfileItem.CopyFunctionListAsHtml(funcList);
    }
  });
  public RelayCommand<object> MarkFunctionCommand => new RelayCommand<object>(async obj => {
    if (obj is SelectedColorEventArgs e && GraphViewer.SelectedNode is {HasFunction: true}) {
      GraphViewer.MarkNode(GraphViewer.SelectedNode, GraphViewer.MarkedColoredNodeStyle(e.SelectedColor));
    }
  });
  public RelayCommand<object> MarkAllFunctionsCommand => new RelayCommand<object>(async obj => {
    if (obj is SelectedColorEventArgs e && GraphViewer.SelectedNode is {HasFunction: true}) {
      MarkFunctionInstances(GraphViewer.SelectedNode.Function, GraphViewer.MarkedColoredNodeStyle(e.SelectedColor));
    }
  });
  public RelayCommand<object> MarkModuleCommand => new RelayCommand<object>(async obj => {
    if (obj is SelectedColorEventArgs e && GraphViewer.SelectedNode is { HasFunction: true }) {
      settings_.AddModuleColor(GraphViewer.SelectedNode.ModuleName, e.SelectedColor);
      settings_.UseModuleColors = true;
      SettingsUpdated(settings_);
    }
  });
  public RelayCommand<object> MarkTimelineCommand => new RelayCommand<object>(async obj => {
    if (obj is SelectedColorEventArgs e && GraphViewer.SelectedNode is {HasFunction: true}) {
      GraphViewer.MarkNode(GraphViewer.SelectedNode, GraphViewer.MarkedColoredNodeStyle(e.SelectedColor));
      await Session.MarkProfileFunction(GraphViewer.SelectedNode.CallTreeNode, ToolPanelKind.Timeline,
                                        GraphViewer.MarkedColoredNodeStyle(e.SelectedColor));
    }
  });
  public RelayCommand<object> PreviewFunctionCommand => new RelayCommand<object>(async obj => {
    if (GraphViewer.SelectedNode is {HasFunction: true}) {
      await IRDocumentPopupInstance.ShowPreviewPopup(GraphViewer.SelectedNode.Function,
                                                     $"Function {GraphViewer.SelectedNode.FunctionName}",
                                                     GraphViewer, Session);
    }
  });

  public RelayCommand<object> OpenInstanceCommand => new RelayCommand<object>(async obj => {
    if (GraphViewer.SelectedNode is {HasFunction: true}) {
      var filter = new ProfileSampleFilter(GraphViewer.SelectedNode.CallTreeNode);
      var mode = Utils.IsControlModifierActive() ? OpenSectionKind.NewTabDockRight : OpenSectionKind.ReplaceCurrent;
      await Session.OpenProfileFunction(GraphViewer.SelectedNode.CallTreeNode, mode, filter);
    }
  });

  public RelayCommand<object> OpenInstanceInNewTabCommand => new RelayCommand<object>(async obj => {
    if (GraphViewer.SelectedNode is {HasFunction: true}) {
      var filter = new ProfileSampleFilter(GraphViewer.SelectedNode.CallTreeNode);
      await Session.OpenProfileFunction(GraphViewer.SelectedNode.CallTreeNode,
                                        OpenSectionKind.NewTabDockRight, filter);
    }
  });

  public bool ShowNodePanel {
    get => showNodePanel_;
    set => SetField(ref showNodePanel_, value);
  }

  private double GraphAreaWidth => Math.Max(0, GraphHost.ViewportWidth - 1);
  private double GraphAreaHeight => GraphHost.ViewportHeight;
  private Rect GraphArea => new Rect(0, 0, GraphAreaWidth, GraphAreaHeight);
  private Rect GraphVisibleArea => new Rect(GraphHost.HorizontalOffset,
                                            GraphHost.VerticalOffset,
                                            GraphAreaWidth, GraphAreaHeight);
  private Rect GraphHostBounds => new Rect(0, 0, GraphHost.ActualWidth, GraphHost.ActualHeight);
  private double GraphZoomRatio => GraphViewer.MaxGraphWidth / GraphAreaWidth;
  private double CenterZoomPointX => GraphHost.HorizontalOffset + GraphAreaWidth / 2;
  private double PanOffset => Utils.IsKeyboardModifierActive() ?
    FastPanOffset : DefaultPanOffset;

  public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
    return callTree_.GetBacktrace(node);
  }

  public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
    return callTree_.GetTopModules(node);
  }

  public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node) {
    return callTree_.GetTopFunctions(node);
  }

  public event PropertyChangedEventHandler PropertyChanged;

  private enum FlameGraphStateKind {
    Default,
    EnlargeNode,
    ChangeRootNode
  }

  private class FlameGraphState {
    public FlameGraphStateKind Kind { get; set; }
    public FlameGraphNode Node { get; set; }
    public double MaxGraphWidth { get; set; }
    public double HorizontalOffset { get; set; }
    public double VerticalOffset { get; set; }
  }

  public event EventHandler<FlameGraphNode> NodeSelected;
  public event EventHandler NodesDeselected;
  public event EventHandler<double> HorizontalOffsetChanged;
  public event EventHandler<double> MaxWidthChanged;
  public event EventHandler<FlameGraphNode> RootNodeChanged;
  public event EventHandler RootNodeCleared;

  private static double Lerp(double start, double end, double progress) {
    return start + (end - start) * progress;
  }

  public async Task InitializeFlameGraph(ProfileCallTree callTree) {
    if (IsInitialized) {
      Reset();
    }

    callTree_ = callTree;

    if (callTree_ != null) {
      await GraphViewer.Initialize(callTree, GraphArea, settings_, Session);
      ResetWidth(false);
    }
  }

  public async Task InitializeTimeline(ProfileCallTree callTree, int threadId) {
    //? TODO: Timeline-style view not used yet.
    if (IsInitialized) {
      Reset();
    }

    callTree_ = callTree;

    if (callTree != null && !GraphViewer.IsInitialized) {
      await GraphViewer.Initialize(callTree, GraphArea, settings_, Session, true, threadId);
    }
  }

  private void HostOnKeyUp(object sender, KeyEventArgs e) {
    if (Utils.IsControlModifierActive()) {
      Cursor = Cursors.Arrow;
    }
  }

  public void BringNodeIntoView(FlameGraphNode node, bool fitSize = false) {
    var bounds = GraphViewer.ComputeNodeBounds(node);
    var graphArea = GraphVisibleArea;
    DoubleAnimation animation1 = null;
    DoubleAnimation animation2 = null;

    if (bounds.Left < graphArea.Left || bounds.Right > graphArea.Right) {
      //? TODO: If node is outside on the right, increase offset to show it all
      animation1 = ScrollToHorizontalOffset(bounds.Left);
    }

    if (bounds.Top < graphArea.Top || bounds.Bottom > graphArea.Bottom) {
      animation2 = ScrollToVerticalOffset(bounds.Top);
    }

    if (fitSize) {
      switch ((animation1 != null, animation2 != null)) {
        case (true, true):
        case (true, false): {
          animation1.Completed += (sender, e) => BringNodeIntoViewZoom(node);
          break;
        }
        case (false, true): {
          animation2.Completed += (sender, e) => BringNodeIntoViewZoom(node);
          break;
        }
        default: {
          BringNodeIntoViewZoom(node);
          break;
        }
      }
    }

    animation1?.BeginAnimation(FlameGraphHorizontalOffsetProperty, animation1, HandoffBehavior.SnapshotAndReplace);
    animation2?.BeginAnimation(FlameGraphVerticalOffsetProperty, animation2, HandoffBehavior.SnapshotAndReplace);
  }

  public void SetHorizontalOffset(double offset) {
    if (!IsInitialized) {
      return;
    }

    ignoreScrollEvent_ = true;
    GraphHost.ScrollToHorizontalOffset(offset);
  }

  public async Task ChangeRootNode(FlameGraphNode node, bool saveState = true) {
    // Update the undo stack.
    if (saveState) {
      SaveCurrentState(FlameGraphStateKind.ChangeRootNode);
    }

    ResetHighlightedNodes();
    SetHorizontalOffset(0);
    rootNode_ = node;
    await GraphViewer.Initialize(GraphViewer.FlameGraph.CallTree, node.CallTreeNode,
                                 GraphHostBounds, settings_, Session);
    GraphViewer.RestoreFixedMarkedNodes();

    if (node.HasFunction) {
      RootNodeChanged?.Invoke(this, node);
    }
  }

  public async Task RestorePreviousState() {
    if (!stateStack_.TryPop(out var state)) {
      return;
    }

    switch (state.Kind) {
      case FlameGraphStateKind.EnlargeNode: {
        // IF there is no node the root node should be selected.
        var node = state.Node ?? GraphViewer.FlameGraph.RootNode;
        await EnlargeNode(node, false, state.VerticalOffset, state.HorizontalOffset);
        break;
      }
      case FlameGraphStateKind.ChangeRootNode: {
        RootNodeCleared?.Invoke(this, EventArgs.Empty);
        await ChangeRootNode(state.Node, false);
        GraphViewer.RestoreFixedMarkedNodes();
        break;
      }
      default: {
        SetMaxWidth(state.MaxGraphWidth, false);
        GraphHost.ScrollToHorizontalOffset(state.HorizontalOffset);
        GraphHost.ScrollToVerticalOffset(state.VerticalOffset);
        break;
      }
    }
  }

  public void SetMaxWidth(double maxWidth, bool animate = true, double duration = ZoomAnimationDuration) {
    if (!IsInitialized) {
      return;
    }

    if (Math.Abs(maxWidth - GraphViewer.MaxGraphWidth) < double.Epsilon) {
      return;
    }

    if (animate) {
      var animation = new DoubleAnimation(GraphViewer.MaxGraphWidth, maxWidth, TimeSpan.FromMilliseconds(duration));
      animation.Completed += (sender, args) => {
        widthAnimation_ = null;
        MaxWidthChanged?.Invoke(this, GraphViewer.MaxGraphWidth);
      };

      widthAnimation_ = animation;
      BeginAnimation(FlameGraphWidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
    else {
      CancelWidthAnimation();
      GraphViewer.UpdateMaxWidth(maxWidth);
      MaxWidthChanged?.Invoke(this, GraphViewer.MaxGraphWidth);
    }
  }

  public void ResetWidth(bool animate = true) {
    // Hack due to possible WPF bug that forces a re-layout of the flame graph
    // so that the scroll bars are displayed if needed.
    Dispatcher.Invoke(() => {
      GraphViewer.InvalidateMeasure();
      GraphViewer.InvalidateVisual();

      // If there is a vertical scroll bar, resize the flame graph to fit
      // the view and not show a horizontal scroll bar initially.
      Dispatcher.Invoke(() => {
        ResetWidthImpl(false);
      }, DispatcherPriority.ContextIdle);
    }, DispatcherPriority.Normal);
  }

  private void ResetWidthImpl(bool animate = true) {
    //? TODO: Buttons should be disabled
    if (!GraphViewer.IsInitialized) {
      return;
    }

    SetMaxWidth(GraphAreaWidth, animate);
    ScrollToVerticalOffset(0, animate);
    ScrollToHorizontalOffset(0, animate);
    ResetHighlightedNodes();
  }

  public void ZoomIn() {
    ZoomIn(CenterZoomPointX);
  }

  public void ZoomOut() {
    ZoomOut(CenterZoomPointX);
  }

  public async Task NavigateToParentNode() {
    if (enlargedNode_ != null && enlargedNode_.Parent != null) {
      SelectNode(enlargedNode_.Parent);
      await EnlargeNode(enlargedNode_.Parent);
    }
    else if (rootNode_ != null && rootNode_.Parent != null) {
      await ChangeRootNode(rootNode_.Parent);
    }
  }

  public void Reset() {
    callTree_ = null;
    ScrollToOffsets(0, 0);

    if (!GraphViewer.IsInitialized) {
      return;
    }

    ResetHighlightedNodes();
    GraphViewer.Reset();
  }

  public void SelectNode(FlameGraphNode node, bool fitSize = false, bool bringIntoView = true) {
    GraphViewer.SelectNode(node);

    if (bringIntoView) {
      BringNodeIntoView(node, fitSize);
    }
  }

  public void SelectNode(ProfileCallTreeNode node, bool fitSize = false, bool bringIntoView = true) {
    if (!IsInitialized) {
      return;
    }

    var fgNode = GraphViewer.SelectNode(node);

    if (bringIntoView) {
      BringNodeIntoView(fgNode, fitSize);
    }
  }

  public void SelectNodes(List<ProfileCallTreeNode> nodes, bool fitSize = false, bool bringIntoView = true) {
    if (!IsInitialized) {
      return;
    }

    var fgNodes = GraphViewer.SelectNodes(nodes);

    if(bringIntoView && fgNodes.Count > 0) {
      BringNodeIntoView(fgNodes[0], fitSize);
    }
  }

  public void MarkFunctions(List<ProfileCallTreeNode> nodes, HighlightingStyle style) {
    if (!IsInitialized) {
      return;
    }

    GraphViewer.MarkNodes(nodes, style, false);
  }

  public void MarkFunctionInstances(IRTextFunction func, HighlightingStyle style) {
    if (!IsInitialized) {
      return;
    }

    var nodes = callTree_.GetCallTreeNodes(func);
    GraphViewer.MarkNodes(nodes, style, true);
  }

  public void ClearMarkedFunctions(bool clearFixedNodes = false) {
    if (!IsInitialized) {
      return;
    }

    GraphViewer.ResetMarkedNodes(clearFixedNodes);
  }

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
    if (EqualityComparer<T>.Default.Equals(field, value)) return false;
    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }

  private async Task SelectFunctionInPanel(ToolPanelKind panelKind) {
    if (GraphViewer.SelectedNode is {HasFunction: true}) {
      await Session.SelectProfileFunctionInPanel(GraphViewer.SelectedNode.CallTreeNode, panelKind);
    }
  }

  private void SetupEvents() {
    GraphHost.SizeChanged += (sender, args) => {
      if (IsInitialized && args.NewSize.Width > GraphViewer.MaxGraphWidth) {
        SetMaxWidth(args.NewSize.Width, false);
      }
    };

    GraphHost.PreviewMouseWheel += OnPreviewMouseWheel;
    GraphHost.PreviewMouseDown += OnPreviewMouseDown;

    // Setup events for the flame graph area.
    MouseLeftButtonDown += OnMouseLeftButtonDown;
    MouseLeftButtonUp += OnMouseLeftButtonUp;
    MouseRightButtonDown += OnMouseRightButtonDown;
    MouseDoubleClick += OnMouseDoubleClick;
    MouseMove += OnMouseMove;
    MouseDown += OnMouseDown; // Handles back button.
    KeyDown += HostOnKeyDown;
    KeyUp += HostOnKeyUp;

    SetupPreviewPopup();
  }

  private void SetupPreviewPopup() {
    if (nodeHoverPreview_ != null) {
      nodeHoverPreview_.Unregister();
      nodeHoverPreview_ = null;
    }

    if(!settings_.ShowNodePopup) {
      return;
    }

    nodeHoverPreview_ = new PopupHoverPreview(GraphViewer,
                                              TimeSpan.FromMilliseconds(settings_.NodePopupDuration),
                                              (mousePoint, previewPoint) => {
                                                var pointedNode = GraphViewer.FindPointedNode(mousePoint);
                                                var callNode = pointedNode?.CallTreeNode;

                                                if (callNode != null) {
                                                  // If popup already opened for this node reuse the instance.
                                                  if (nodeHoverPreview_.PreviewPopup is CallTreeNodePopup
                                                    popup) {
                                                    popup.UpdatePosition(previewPoint, GraphViewer);
                                                  }
                                                  else {
                                                    popup = new CallTreeNodePopup(
                                                      callNode, this, previewPoint, GraphViewer, Session);
                                                  }

                                                  popup.UpdateNode(callNode);
                                                  return popup;
                                                }

                                                return null;
                                              },
                                              (mousePoint, popup) => {
                                                if (popup is CallTreeNodePopup previewPopup) {
                                                  // Hide if not over the same node anymore.
                                                  var pointedNode = GraphViewer.FindPointedNode(mousePoint);
                                                  return previewPopup.CallTreeNode.CallTreeNode !=
                                                         pointedNode?.CallTreeNode;
                                                }

                                                return true;
                                              },
                                              popup => {
                                                Session.RegisterDetachedPanel(popup);
                                              });
  }

  private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) {
    HidePreviewPopup();

    if (IsMouseOutsideViewport(e)) {
      return;
    }

    var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

    if (pointedNode == null) {
      GraphViewer.ClearSelection(); // Click outside graph is captured here.
    }
  }

  private void HidePreviewPopup() {
    if (nodeHoverPreview_ != null) {
      nodeHoverPreview_.Hide();
    }
  }

  private async void OnMouseDown(object sender, MouseButtonEventArgs e) {
    if (e.ChangedButton == MouseButton.XButton1) {
      await RestorePreviousState();
    }
  }

  private async void OnMouseDoubleClick(object sender, MouseButtonEventArgs e) {
    if (IsMouseOutsideViewport(e)) {
      return;
    }

    var point = e.GetPosition(GraphViewer);
    var pointedNode = GraphViewer.FindPointedNode(point);

    if (pointedNode != null) {
      if (Utils.IsControlModifierActive()) {
        await OpenFunction(pointedNode);
      }
      else {
        await EnlargeNode(pointedNode);
      }
    }
    else {
      double zoomPointX = e.GetPosition(GraphViewer).X;

      if (Utils.IsShiftModifierActive()) {
        ZoomOut(zoomPointX, false);
      }
      else {
        ZoomIn(zoomPointX, false);
      }
    }
  }

  private async Task OpenFunction(FlameGraphNode node) {
    await OpenFunction(node.CallTreeNode);
  }

  private async Task OpenFunction(ProfileCallTreeNode node) {
    if (node != null && node.Function.HasSections) {
      var openMode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTabDockRight : OpenSectionKind.ReplaceCurrent;
      await Session.OpenProfileFunction(node, openMode);
    }
  }

  private DoubleAnimation ScrollToHorizontalOffset(double offset, bool animate = true,
                                                   double duration = ZoomAnimationDuration) {
    if (animate) {
      var animation = new DoubleAnimation(GraphHost.HorizontalOffset, offset, TimeSpan.FromMilliseconds(duration));
      BeginAnimation(FlameGraphHorizontalOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
      return animation;
    }
    else {
      GraphHost.ScrollToHorizontalOffset(offset);
      return null;
    }
  }

  private DoubleAnimation ScrollToVerticalOffset(double offset, bool animate = true,
                                                 double duration = ZoomAnimationDuration) {
    if (animate) {
      var animation1 = new DoubleAnimation(GraphHost.VerticalOffset, offset, TimeSpan.FromMilliseconds(duration));
      BeginAnimation(FlameGraphVerticalOffsetProperty, animation1, HandoffBehavior.SnapshotAndReplace);
      return animation1;
    }
    else {
      GraphHost.ScrollToVerticalOffset(offset);
      return null;
    }
  }

  private void BringNodeIntoViewZoom(FlameGraphNode node) {
    const double MinNodeWidth = 10;
    var bounds = GraphViewer.ComputeNodeBounds(node);
    double zoomX = 0;

    if (bounds.Width < MinNodeWidth) {
      zoomX = MinNodeWidth - bounds.Width;
    }
    else if (bounds.Width > GraphAreaWidth) {
      zoomX = GraphAreaWidth - bounds.Width;
    }
    else {
      return;
    }

    double zoomPointX = bounds.Left;
    double nodeRation = GraphViewer.FlameGraph.InverseScaleWeight(node.Weight);
    double zoomAmount = zoomX * nodeRation;
    AdjustZoom(zoomAmount, zoomPointX, true, ZoomAnimationDuration);
  }

  private async Task EnlargeNode(FlameGraphNode node, bool saveState = true,
                                 double verticalOffset = double.NaN,
                                 double horizontalOffset = double.NaN) {
    if (Utils.IsAltModifierActive()) {
      await ChangeRootNode(node);
      return;
    }

    // Update the undo stack.
    if (saveState) {
      SaveCurrentState(FlameGraphStateKind.EnlargeNode);
    }

    // How wide the entire graph needs to be so that the node fils the view.
    double ratio = GraphViewer.FlameGraph.InverseScaleWeight(node.Weight);
    double newMaxWidth = GraphAreaWidth * ratio;
    double prevRatio = GraphViewer.MaxGraphWidth / GraphAreaWidth;
    double offsetRatio = prevRatio > 0 ? ratio / prevRatio : ratio;

    enlargedNode_ = node;
    initialWidth_ = newMaxWidth;
    initialOffsetX_ = GraphHost.HorizontalOffset;
    endOffsetX_ = GraphViewer.ComputeNodeBounds(node).X * offsetRatio;

    if (!double.IsNaN(horizontalOffset)) {
      endOffsetX_ = horizontalOffset;
    }

    SetMaxWidth(newMaxWidth, true, EnlargeAnimationDuration);
    var horizontalAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(EnlargeAnimationDuration));
    BeginAnimation(FlameGraphEnlargeNodeProperty, horizontalAnim, HandoffBehavior.SnapshotAndReplace);

    if (!double.IsNaN(verticalOffset)) {
      var verticalAnim = new DoubleAnimation(GraphHost.VerticalOffset, verticalOffset,
                                             TimeSpan.FromMilliseconds(EnlargeAnimationDuration));
      BeginAnimation(FlameGraphVerticalOffsetProperty, verticalAnim, HandoffBehavior.SnapshotAndReplace);
    }
  }

  private void SaveCurrentState(FlameGraphStateKind changeKind) {
    var state = new FlameGraphState {
      Kind = changeKind,
      MaxGraphWidth = GraphViewer.MaxGraphWidth,
      HorizontalOffset = GraphHost.HorizontalOffset,
      VerticalOffset = GraphHost.VerticalOffset
    };

    switch (changeKind) {
      case FlameGraphStateKind.EnlargeNode: {
        state.Node = enlargedNode_;
        break;
      }
      case FlameGraphStateKind.ChangeRootNode: {
        state.Node = GraphViewer.FlameGraph.RootNode;
        break;
      }
    }

    stateStack_.Push(state);
  }

  private void ResetHighlightedNodes() {
    GraphViewer.ResetNodeHighlighting();
  }

  private void OnMouseMove(object sender, MouseEventArgs e) {
    if (dragging_) {
      var offset = draggingViewStart_ - (e.GetPosition(GraphHost) - draggingStart_);
      GraphHost.ScrollToHorizontalOffset(offset.X);
      GraphHost.ScrollToVerticalOffset(offset.Y);
      CaptureMouse();
      e.Handled = true;
    }
  }

  private async void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
    if (dragging_) {
      EndMouseDragging();
      e.Handled = true;
    }
  }

  private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    // Start dragging the graph only if the click starts inside the scroll area,
    // excluding the scroll bars, and it in an empty spot.
    if (IsMouseOutsideViewport(e)) {
      return;
    }

    if (!HandleNodeSelection(e)) {
      StartMouseDragging(e);
      e.Handled = true;
    }
  }

  private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
    HandleNodeSelection(e);
  }

  private bool HandleNodeSelection(MouseButtonEventArgs e) {
    if (IsMouseOutsideViewport(e)) {
      return false;
    }

    var point = e.GetPosition(GraphViewer);
    var pointedNode = GraphViewer.FindPointedNode(point);

    if (pointedNode == null) {
      GraphViewer.ClearSelection(); // Click outside graph is captured here.
      NodesDeselected?.Invoke(this, EventArgs.Empty);
      return false;
    }
    else {
      NodeSelected?.Invoke(this, pointedNode);
      return true;
    }
  }

  private void StartMouseDragging(MouseButtonEventArgs e) {
    //? TODO: Code here is identical to GraphPanel
    HidePreviewPopup();
    dragging_ = true;
    draggingStart_ = e.GetPosition(GraphHost);
    draggingViewStart_ = new Point(GraphHost.HorizontalOffset, GraphHost.VerticalOffset);
    Cursor = Cursors.SizeAll;
    CaptureMouse();
  }

  private bool IsMouseOutsideViewport(MouseButtonEventArgs e) {
    var point = e.GetPosition(GraphHost);
    return point.X < 0 ||
           point.Y < 0 ||
           point.X >= GraphHost.ViewportWidth ||
           point.Y >= GraphHost.ViewportHeight;
  }

  private async void HostOnKeyDown(object sender, KeyEventArgs e) {
    switch (e.Key) {
      case Key.Return: {
        if (GraphViewer.SelectedNode != null) {
          if (Utils.IsControlModifierActive()) {
            await OpenFunction(GraphViewer.SelectedNode);
            e.Handled = true;
          }
          else if (Utils.IsAltModifierActive()) {
            PreviewFunctionCommand.Execute(GraphViewer.SelectedNode);
            e.Handled = true;
          }
          else {
            await EnlargeNode(GraphViewer.SelectedNode);
            e.Handled = true;
          }
        }

        break;
      }
      case Key.Left: {
        ScrollToRelativeOffsets(-PanOffset, 0);
        e.Handled = true;
        break;
      }
      case Key.Right: {
        ScrollToRelativeOffsets(PanOffset, 0);
        e.Handled = true;
        break;
      }
      case Key.Up: {
        if (Utils.IsControlModifierActive()) {
          await NavigateToParentNode();
        }
        else {
          ScrollToRelativeOffsets(0, -PanOffset);
        }

        e.Handled = true;
        break;
      }
      case Key.Down: {
        if (Utils.IsControlModifierActive()) {
          await NavigateToChildNode();
        }
        else {
          ScrollToRelativeOffsets(0, PanOffset);
        }

        e.Handled = true;
        break;
      }
      case Key.OemPlus:
      case Key.Add: {
        if (Utils.IsControlModifierActive()) {
          ZoomIn(CenterZoomPointX);
          e.Handled = true;
        }

        break;
      }
      case Key.OemMinus:
      case Key.Subtract: {
        if (Utils.IsControlModifierActive()) {
          ZoomOut(CenterZoomPointX);
          e.Handled = true;
        }

        break;
      }
      case Key.D0:
      case Key.NumPad0: {
        if (Utils.IsControlModifierActive()) {
          ResetWidth();
          e.Handled = true;
        }

        break;
      }
      case Key.Back: {
        await RestorePreviousState();
        e.Handled = true;
        break;
      }
    }

    if (e.Handled) {
      HidePreviewPopup();
    }
  }

  private void ScrollToRelativeOffsets(double adjustmentX, double adjustmentY) {
    double offsetX = GraphHost.HorizontalOffset;
    double offsetY = GraphHost.VerticalOffset;
    ScrollToOffsets(offsetX + adjustmentX, offsetY + adjustmentY);
  }

  private void ScrollToOffsets(double offsetX, double offsetY) {
    GraphHost.ScrollToHorizontalOffset(offsetX);
    GraphHost.ScrollToVerticalOffset(offsetY);
  }

  private void CancelWidthAnimation() {
    if (widthAnimation_ != null) {
      widthAnimation_.BeginTime = null;
      BeginAnimation(FlameGraphWidthProperty, widthAnimation_);
      widthAnimation_ = null;
    }
  }

  private void CancelZoomAnimation() {
    if (zoomAnimation_ != null) {
      zoomAnimation_.BeginTime = null;
      BeginAnimation(FlameGraphWidthProperty, zoomAnimation_);
      zoomAnimation_ = null;
    }
  }

  private void AdjustMaxWidth(double amount, bool animate = true, double duration = ZoomAnimationDuration) {
    double newWidth = Math.Max(GraphAreaWidth, GraphViewer.MaxGraphWidth + amount);
    SetMaxWidth(newWidth, animate, duration);
  }

  private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
    HidePreviewPopup();

    if (Utils.IsShiftModifierActive()) {
      // Turn vertical scrolling into horizontal scrolling.
      GraphHost.ScrollToHorizontalOffset(GraphHost.HorizontalOffset - e.Delta);
      e.Handled = true;
      return;
    }

    if (!(Utils.IsKeyboardModifierActive() ||
          e.LeftButton == MouseButtonState.Pressed)) {
      // Zoom when Ctrl/Alt/Shift or left mouse button are pressed.
      return;
    }

    // Cancel ongoing dragging operation.
    if (dragging_) {
      EndMouseDragging();
    }

    // Disable animation if scrolling without interruption.
    var time = DateTime.UtcNow;
    var timeElapsed = time - lastWheelZoomTime_;
    bool animate = timeElapsed.TotalMilliseconds > ScrollWheelZoomAnimationDuration;

    double amount = ScrollWheelZoomAmount * GraphZoomRatio; // Keep step consistent.
    double step = amount * Math.CopySign(1 + e.Delta / 1000.0, e.Delta);
    double zoomPointX = e.GetPosition(GraphViewer).X;
    AdjustZoom(step, zoomPointX, animate, ScrollWheelZoomAnimationDuration);

    lastWheelZoomTime_ = time;
    e.Handled = true;
  }

  private void EndMouseDragging() {
    dragging_ = false;
    Cursor = Cursors.Arrow;
    ReleaseMouseCapture();
  }

  private void AdjustZoom(double step, double zoomPointX, bool animate = false, double duration = 0.0) {
    double initialWidth = GraphViewer.MaxGraphWidth;
    double initialOffsetX = GraphHost.HorizontalOffset;
    AdjustMaxWidth(step, animate, duration);

    // Maintain the zoom point under the mouse by adjusting the horizontal offset.
    if (animate) {
      zoomPointX_ = zoomPointX;
      initialWidth_ = initialWidth;
      initialOffsetX_ = initialOffsetX;
      var animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(duration));

      animation.Completed += (sender, args) => {
        zoomAnimation_ = null;
      };

      zoomAnimation_ = animation;
      BeginAnimation(FlameGraphZoomProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
    else {
      CancelWidthAnimation();
      CancelZoomAnimation();
      AdjustGraphOffset(zoomPointX, initialWidth, initialOffsetX);
    }
  }

  private void AdjustGraphOffset(double zoomPointX, double initialWidth, double initialOffsetX) {
    double zoom = GraphViewer.MaxGraphWidth / initialWidth;
    double offsetAdjustment = initialOffsetX / zoom + zoomPointX;
    GraphHost.ScrollToHorizontalOffset(offsetAdjustment * zoom - zoomPointX);
  }

  private void AdjustEnlargedNodeOffset(double progress) {
    double offset = Lerp(initialOffsetX_, endOffsetX_, progress);
    GraphHost.ScrollToHorizontalOffset(offset);
  }

  private void AdjustZoomPointOffset(double progress) {
    double zoom = GraphViewer.MaxGraphWidth / initialWidth_;
    double offsetAdjustment = initialOffsetX_ / zoom + zoomPointX_;
    GraphHost.ScrollToHorizontalOffset(offsetAdjustment * zoom - zoomPointX_);
  }

  private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
    ZoomIn(CenterZoomPointX);
  }

  private void ZoomIn(double zoomPointX, bool considerSelectedNode = true) {
    if (considerSelectedNode && GraphViewer.SelectedNode != null) {
      var bounds = GraphViewer.ComputeNodeBounds(GraphViewer.SelectedNode);
      zoomPointX = bounds.Left + bounds.Width / 2;
    }

    AdjustZoom(ZoomAmount * GraphZoomRatio, zoomPointX, true, ZoomAnimationDuration);
  }

  private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
    ZoomOut(CenterZoomPointX);
  }

  private void ZoomOut(double zoomPointX, bool considerSelectedNode = true) {
    if (considerSelectedNode && GraphViewer.SelectedNode != null) {
      var bounds = GraphViewer.ComputeNodeBounds(GraphViewer.SelectedNode);
      zoomPointX = bounds.Left + bounds.Width / 2;
    }

    AdjustZoom(-ZoomAmount * GraphZoomRatio, zoomPointX, true, ZoomAnimationDuration);
  }

  private void GraphHost_OnScrollChanged(object sender, ScrollChangedEventArgs e) {
    if (!GraphViewer.IsInitialized) {
      return;
    }

    GraphViewer.UpdateVisibleArea(GraphVisibleArea);

    if (!ignoreScrollEvent_) {
      HorizontalOffsetChanged?.Invoke(this, GraphHost.HorizontalOffset);
    }
    else {
      ignoreScrollEvent_ = false;
    }
  }

  private async Task NavigateToChildNode() {
    if (enlargedNode_ != null && enlargedNode_.HasChildren) {
      GraphViewer.SelectNode(enlargedNode_.Children[0]);
      await EnlargeNode(enlargedNode_.Children[0]);
    }
    else if (rootNode_ != null && rootNode_.HasChildren) {
      await ChangeRootNode(rootNode_.Children[0]);
    }
  }

  private async void SelectFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
    await SelectFunctionInPanel(ToolPanelKind.Section);
  }

  private async void OpenFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
    await OpenFunction(GraphViewer.SelectedNode, OpenSectionKind.ReplaceCurrent);
  }

  private async Task OpenFunction(FlameGraphNode node, OpenSectionKind openMode) {
    if (node is {HasFunction: true}) {
      await Session.OpenProfileFunction(node.CallTreeNode, openMode);
    }
  }

  private async void OpenFunctionInNewTab(object sender, ExecutedRoutedEventArgs e) {
    await OpenFunction(GraphViewer.SelectedNode, OpenSectionKind.NewTabDockRight);
  }

  private async void ChangeRootNodeExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GraphViewer.SelectedNode != null) {
      await ChangeRootNode(GraphViewer.SelectedNode);
    }
  }

  private void MarkAllInstancesExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GraphViewer.SelectedNode != null &&
        GraphViewer.SelectedNode.HasFunction) {
      MarkFunctionInstances(GraphViewer.SelectedNode.Function,
                            GraphViewer.MarkedNodeStyle);
    }
  }

  private void MarkInstanceExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GraphViewer.SelectedNode != null &&
        GraphViewer.SelectedNode.HasFunction) {
      GraphViewer.MarkNode(GraphViewer.SelectedNode, GraphViewer.MarkedNodeStyle);
    }
  }

  private void ClearMarkedNodesExecuted(object sender, ExecutedRoutedEventArgs e) {
    ClearMarkedFunctions(true);
  }

  private async void EnlargeNodeExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GraphViewer.SelectedNode != null) {
      await EnlargeNode(GraphViewer.SelectedNode);
    }
  }

    #region Animation support

  public static DependencyProperty FlameGraphWidthProperty =
    DependencyProperty.Register(nameof(FlameGraphWidth), typeof(double), typeof(FlameGraphHost),
                                new PropertyMetadata(0.0, FlameGraphWidthChanged));
  public static DependencyProperty FlameGraphZoomProperty =
    DependencyProperty.Register(nameof(FlameGraphOffset), typeof(double), typeof(FlameGraphHost),
                                new PropertyMetadata(0.0, FlameGraphOffsetChanged));
  public static DependencyProperty FlameGraphEnlargeNodeProperty =
    DependencyProperty.Register(nameof(FlameGraphEnlargeNodeProperty), typeof(double), typeof(FlameGraphHost),
                                new PropertyMetadata(0.0, FlameGraphOffsetChanged));
  public static DependencyProperty FlameGraphHorizontalOffsetProperty =
    DependencyProperty.Register(nameof(FlameGraphHorizontalOffset), typeof(double), typeof(FlameGraphHost),
                                new PropertyMetadata(0.0, FlameGraphHorizontalOffsetChanged));
  public static DependencyProperty FlameGraphVerticalOffsetProperty =
    DependencyProperty.Register(nameof(FlameGraphVerticalOffset), typeof(double), typeof(FlameGraphHost),
                                new PropertyMetadata(0.0, FlameGraphVerticalOffsetChanged));

  private static void FlameGraphWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
    if (d is FlameGraphHost panel) {
      panel.FlameGraphWidth = (double)e.NewValue;
    }
  }

  private static void FlameGraphOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
    if (d is FlameGraphHost panel) {
      if (e.Property == FlameGraphZoomProperty) {
        panel.AdjustZoomPointOffset((double)e.NewValue);
      }
      else {
        panel.AdjustEnlargedNodeOffset((double)e.NewValue);
      }
    }
  }

  private static void FlameGraphVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
    if (d is FlameGraphHost panel) {
      panel.GraphHost.ScrollToVerticalOffset((double)e.NewValue);
    }
  }

  private static void FlameGraphHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
    if (d is FlameGraphHost panel) {
      panel.GraphHost.ScrollToHorizontalOffset((double)e.NewValue);
    }
  }

  public double FlameGraphWidth {
    get => GraphViewer.MaxGraphWidth;
    set {
      GraphViewer.UpdateMaxWidth(value);
      MaxWidthChanged?.Invoke(this, value);
    }
  }

  public double FlameGraphOffset {
    get => 0;
    set { }
  }

  public double FlameGraphVerticalOffset {
    get => 0;
    set { }
  }

  public double FlameGraphHorizontalOffset {
    get => 0;
    set { }
  }

    #endregion

  public async Task ClearRootNode() {
    // Undo all states until a root node change is found.
    while (stateStack_.TryPop(out var state)) {
      if (state.Kind == FlameGraphStateKind.ChangeRootNode) {
        RootNodeCleared?.Invoke(this, EventArgs.Empty);
        GraphViewer.RestoreFixedMarkedNodes();
        await ChangeRootNode(state.Node, false); // May enable a previous root node.
        break;
      }
    }
  }

  public void SettingsUpdated(FlameGraphSettings value) {
    settings_ = value;
    GraphViewer.SettingsUpdated(value);
    SetupPreviewPopup();
  }
}