// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ProfileExplorerCore2;
using ProfileExplorerCore2.Analysis;
using ProfileExplorerCore2.Graph;
using ProfileExplorerCore2.IR;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.OptionsPanels;
using ProfileExplorer.UI.Panels;
using ProtoBuf;
using ProfileExplorerCore2.Utilities;
using ProfileExplorerCore2.IR.Tags;

namespace ProfileExplorer.UI;

// ScrollViewer that ignores click events so they get passed
// to the hosted control, allowing for dragging the graphs for ex.
public class ScrollViewerClickable : ScrollViewer {
  protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
    Keyboard.Focus(this);
  }

  protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { }

  protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) {
    Keyboard.Focus(this);
  }

  protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) { }
}

[ProtoContract]
public class GraphPanelState {
  [ProtoMember(3)]
  public double HorizontalOffset;
  [ProtoMember(2)]
  public double VerticalOffset;
  [ProtoMember(1)]
  public double ZoomLevel;
}

public partial class GraphPanel : ToolPanelControl {
  private const double FastPanOffset = 80;
  private const double FastZoomFactor = 4;
  private const double MaxZoomLevel = 2.0;
  private const double MinZoomLevel = 0.25;
  private const double PanOffset = 20;
  private const double ZoomAdjustment = 0.05;
  private const double HorizontalViewMargin = 50;
  private const double VerticalViewMargin = 100;
  private bool delayFitSize_;
  private bool delayRestoreState_;
  private bool dragging_;
  private Point draggingStart_;
  private Point draggingViewStart_;
  private Graph graph_;
  private OptionsPanelHostPopup optionsPanelPopup_;
  private GraphNode hoveredNode_;
  private bool ignoreNextHover_;
  private CancelableTaskInstance loadTask_;
  private IRDocumentPopup previewPopup_;
  private GraphQueryInfo queryInfo_;
  private bool queryPanelVisible_;
  private DelayedAction removeHoveredAction_;
  private bool restoredState_;

  public GraphPanel() {
    InitializeComponent();
    GraphViewer.HostPanel = this;
    GraphViewer.MaxZoomLevel = MaxZoomLevel;
    SetupEvents();

    //? TODO: No context menu for expr graph yet, don't show CFG one.
    if (PanelKind == ToolPanelKind.ExpressionGraph) {
      GraphViewer.ContextMenu = null;
    }

    SetupCommands();
  }

  public IRElement SelectedElement {
    get => GraphViewer.SelectedElement;
    set {
      GraphViewer.SelectElement(value);

      if (!HasPinnedContent && Settings.BringNodesIntoView) {
        BringIntoView(value);
      }
    }
  }

  public IRTextSection Section => Document?.Section;
  public override ToolPanelKind PanelKind => ToolPanelKind.FlowGraph;
  public override HandledEventKind HandledEvents =>
    HandledEventKind.ElementSelection | HandledEventKind.ElementHighlighting;
  public GraphSettings Settings =>
    PanelKind == ToolPanelKind.ExpressionGraph
      ? App.Settings.ExpressionGraphSettings
      : App.Settings.FlowGraphSettings;

  public void BringIntoView(IRElement element) {
    var node = GraphViewer.FindElementNode(element);

    if (node == null) {
      return;
    }

    var position = GraphViewer.GetNodePosition(node);
    double offsetX = GraphHost.HorizontalOffset;
    double offsetY = GraphHost.VerticalOffset;

    if (position.X < offsetX || position.X > offsetX + GraphHost.ActualWidth) {
      GraphHost.ScrollToHorizontalOffset(Math.Max(0, position.X - HorizontalViewMargin));
    }

    if (position.Y < offsetY || position.Y > offsetY + GraphHost.ActualHeight) {
      GraphHost.ScrollToVerticalOffset(Math.Max(0, position.Y - VerticalViewMargin));
    }
  }

  public void DisplayGraph(Graph graph) {
    graph_ = graph;
    GraphViewer.ShowGraph(graph, Session.CompilerInfo);
    FitGraphIntoView();

    if (delayRestoreState_) {
      Dispatcher.BeginInvoke(() => LoadSavedState(), DispatcherPriority.Render);
    }

    HasPinnedContent = false;
    EnablePanel();
  }

  public void HideGraph() {
    if (graph_ == null) {
      return;
    }

    GraphViewer.HideGraph();
    Document = null;
    graph_ = null;
    hoveredNode_ = null;
    DisablePanel();
  }

  private void Highlight(IRHighlightingEventArgs info) {
    if (!Settings.SyncMarkedNodes && info.Type == HighlighingType.Marked) {
      return;
    }

    GraphViewer.Highlight(info);
  }

  public void InitializeFromDocument(IRDocument document) {
#if DEBUG
    Trace.TraceInformation(
      $"Graph panel {ObjectTracker.Track(this)}: initialize with doc {ObjectTracker.Track(document)}");
#endif
    Document = document;
  }

  public async Task<CancelableTask> OnGenerateGraphStart(IRTextSection section) {
    var animation = new DoubleAnimation(0.25, TimeSpan.FromSeconds(0.5));
    animation.BeginTime = TimeSpan.FromSeconds(1);
    GraphViewer.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    LongOperationView.Opacity = 0.0;
    LongOperationView.Visibility = Visibility.Visible;
    var animation2 = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.5));
    animation2.BeginTime = TimeSpan.FromSeconds(2);
    LongOperationView.BeginAnimation(OpacityProperty, animation2, HandoffBehavior.SnapshotAndReplace);

    return await CreateGraphLoadTask();
  }

  public void OnGenerateGraphDone(CancelableTask task, bool failed = false) {
    if (!failed) {
      GraphHost.ScrollToHorizontalOffset(0);
      GraphHost.ScrollToVerticalOffset(0);
      var animation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(0.2));
      GraphViewer.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    var animation2 = new DoubleAnimation(0, TimeSpan.FromSeconds(0.2));
    animation2.Completed += Animation2_Completed;
    LongOperationView.BeginAnimation(OpacityProperty, animation2, HandoffBehavior.SnapshotAndReplace);
    CompleteGraphLoadTask(task);
  }

  public void RemoveHighlighting(IRElement element) {
    var node = GraphViewer.FindElementNode(element);

    if (node != null) {
      GraphViewer.ResetMarkedNode(node, element);
    }
  }

  public void RemoveAllHighlighting(HighlighingType type) {
    GraphViewer.ResetHighlightedNodes(type);
  }

  private void SetupEvents() {
    GraphViewer.BlockSelected += GraphViewer_BlockSelected;
    PreviewMouseWheel += GraphPanel_PreviewMouseWheel;
    PreviewMouseLeftButtonDown += GraphPanel_PreviewMouseLeftButtonDown;
    PreviewMouseRightButtonDown += GraphPanel_PreviewMouseRightButtonDown;
    MouseLeftButtonDown += GraphPanel_MouseLeftButtonDown;
    MouseLeftButtonUp += GraphPanel_MouseLeftButtonUp;
    MouseMove += GraphPanel_MouseMove;
    MouseLeave += GraphPanel_MouseLeave;
    PreviewKeyDown += GraphPanel_PreviewKeyDown;
    GraphHost.ScrollChanged += GraphHost_ScrollChanged;

    var hover = new MouseHoverLogic(this);
    hover.MouseHover += Hover_MouseHover;
    hover.MouseHoverStopped += Hover_MouseHoverStopped;
  }

  private void GraphPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    var position = e.GetPosition(GraphViewer);
    hoveredNode_ = GraphViewer.FindPointedNode(position);

    if (hoveredNode_ != null) {
      // Select A/B nodes in the query panel.
      if (Keyboard.IsKeyDown(Key.A) ||
          Keyboard.IsKeyDown(Key.NumPad1) ||
          Keyboard.IsKeyDown(Key.D1)) {
        SelectQueryBlock1Executed(this, null);
        e.Handled = true;
        return;
      }

      if (Keyboard.IsKeyDown(Key.B) ||
          Keyboard.IsKeyDown(Key.NumPad2) ||
          Keyboard.IsKeyDown(Key.D2)) {
        SelectQueryBlock2Executed(this, null);
        e.Handled = true;
        return;
      }

      Focus();
    }
    else {
      GraphViewer.ResetHighlightedNodes(HighlighingType.Selected);
      GraphViewer.ResetHighlightedNodes(HighlighingType.Hovered);
    }
  }

  private void GraphPanel_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
    hoveredNode_ = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

    if (hoveredNode_ != null) {
      Focus();
    }
  }

  private void FitGraphIntoView() {
    var viewBounds = GetGraphBounds();

    if (Math.Abs(viewBounds.Width) < double.Epsilon || Math.Abs(viewBounds.Height) < double.Epsilon) {
      // Panel is not visible, set the graph size when it becomes visible.
      delayFitSize_ = true;
    }
    else if (!restoredState_) {
      GraphViewer.FitWidthToSize(viewBounds);
      delayFitSize_ = false;
    }
  }

  private Size GetGraphBounds() {
    return new Size(Math.Max(0, RenderSize.Width - SystemParameters.VerticalScrollBarWidth),
                    RenderSize.Height);
  }

  private async Task<CancelableTask> CreateGraphLoadTask() {
    return await loadTask_.CancelCurrentAndCreateTaskAsync();
  }

  private void CompleteGraphLoadTask(CancelableTask task) {
    loadTask_.CompleteTask();
  }

  private void AddCommand(RoutedCommand command, ExecutedRoutedEventHandler handler) {
    var binding = new CommandBinding(command);
    binding.Executed += handler;
    CommandBindings.Add(binding);
  }

  private void AdjustZoom(double value) {
    if (Utils.IsShiftModifierActive()) {
      value *= FastZoomFactor;
    }

    SetZoom(GraphViewer.ZoomLevel + value);
  }

  private void Animation2_Completed(object sender, EventArgs e) {
    LongOperationView.Visibility = Visibility.Collapsed;
  }

  private Size AvailableGraphSize() {
    return new Size(GraphHost.RenderSize.Width -
                    SystemParameters.VerticalScrollBarWidth -
                    SystemParameters.BorderWidth * 2, GraphHost.RenderSize.Height);
  }

  private void Button_MouseDown(object sender, MouseButtonEventArgs e) {
    AdjustZoom(-ZoomAdjustment);
  }

  private void Button_MouseDown_1(object sender, MouseButtonEventArgs e) {
    AdjustZoom(ZoomAdjustment);
  }

  private void ClearAllMarkedExecuted(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.ResetAllMarkedNodes();
  }

  private void ClearMarkedExecuted(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.ResetSelectedNode();
  }

  private void ExecuteGraphFitAll(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.FitToSize(AvailableGraphSize());
  }

  private void ExecuteGraphFitWidth(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.FitWidthToSize(AvailableGraphSize());
  }

  private void ExecuteGraphResetWidth(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.ZoomLevel = 1;
  }

  private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
    AdjustZoom(ZoomAdjustment);
  }

  private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
    AdjustZoom(-ZoomAdjustment);
  }

  private double GetPanOffset() {
    return Utils.IsKeyboardModifierActive() ? FastPanOffset : PanOffset;
  }

  private HighlightingStyle GetSelectedColorStyle(ExecutedRoutedEventArgs e) {
    return Utils.GetSelectedColorStyle(e.Parameter as SelectedColorEventArgs, GraphViewer.DefaultBoldPen);
  }

  private void GraphHost_ScrollChanged(object sender, ScrollChangedEventArgs e) {
    HidePreviewPopup();
  }

  private void GraphPanel_MouseLeave(object sender, MouseEventArgs e) {
    HidePreviewPopupDelayed();
  }

  private void GraphPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    // Start dragging the graph only if the click starts inside the scroll area,
    // excluding the scroll bars, and it in an empty spot.
    var point = e.GetPosition(GraphHost);
    Focus();

    if (point.X < 0 ||
        point.Y < 0 ||
        point.X >= GraphHost.ViewportWidth ||
        point.Y >= GraphHost.ViewportHeight) {
      return;
    }

    //? TODO: Also don't handle if over query panel
    var pointedNode = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

    if (pointedNode != null) {
      return;
    }

    StartMouseDragging(e);
    e.Handled = true;
  }

  private void StartMouseDragging(MouseButtonEventArgs e) {
    HidePreviewPopup();
    dragging_ = true;
    draggingStart_ = e.GetPosition(GraphHost);
    draggingViewStart_ = new Point(GraphHost.HorizontalOffset, GraphHost.VerticalOffset);
    Cursor = Cursors.SizeAll;
    CaptureMouse();
  }

  private void GraphPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
    if (dragging_) {
      dragging_ = false;
      Cursor = Cursors.Arrow;
      ReleaseMouseCapture();
      e.Handled = true;
    }
  }

  private void GraphPanel_MouseMove(object sender, MouseEventArgs e) {
    if (dragging_) {
      var offset = draggingViewStart_ - (e.GetPosition(GraphHost) - draggingStart_);
      GraphHost.ScrollToHorizontalOffset(offset.X);
      GraphHost.ScrollToVerticalOffset(offset.Y);
      e.Handled = true;
    }
  }

  private void GraphPanel_PreviewKeyDown(object sender, KeyEventArgs e) {
    double offsetX = GraphHost.HorizontalOffset;
    double offsetY = GraphHost.VerticalOffset;

    switch (e.Key) {
      case Key.Right: {
        offsetX += GetPanOffset();
        e.Handled = true;
        break;
      }
      case Key.Left: {
        offsetX -= GetPanOffset();
        e.Handled = true;
        break;
      }
      case Key.Up: {
        offsetY -= GetPanOffset();
        e.Handled = true;
        break;
      }
      case Key.Down: {
        offsetY += GetPanOffset();
        e.Handled = true;
        break;
      }
      case Key.OemPlus:
      case Key.Add: {
        if (Utils.IsControlModifierActive()) {
          AdjustZoom(ZoomAdjustment);
          e.Handled = true;
        }

        return;
      }
      case Key.OemMinus:
      case Key.Subtract: {
        if (Utils.IsControlModifierActive()) {
          e.Handled = true;
          AdjustZoom(-ZoomAdjustment);
        }

        return;
      }
    }

    if (e.Handled) {
      HidePreviewPopup();
      GraphHost.ScrollToHorizontalOffset(offsetX);
      GraphHost.ScrollToVerticalOffset(offsetY);
    }
  }

  private void GraphPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
    if (!Utils.IsKeyboardModifierActive()) {
      return;
    }

    SetZoom(GraphViewer.ZoomLevel * Math.Abs(1 + e.Delta / 1000.0));
    e.Handled = true;
  }

  private void GraphViewer_BlockSelected(object sender, IRElementEventArgs e) {
    ignoreNextHover_ = true;
  }

  private void HidePreviewPopup(bool force = false) {
    if (previewPopup_ != null && (force || !previewPopup_.IsMouseOver)) {
      previewPopup_.ClosePopup();
      previewPopup_ = null;
    }
  }

  private async void Hover_MouseHover(object sender, MouseEventArgs e) {
    if (!Settings.ShowPreviewPopup ||
        Settings.ShowPreviewPopupWithModifier && !Utils.IsKeyboardModifierActive()) {
      return;
    }

    if (ignoreNextHover_) {
      // Don't show the block preview if the user jumped to it.
      ignoreNextHover_ = false;
      return;
    }

    var node = GraphViewer.FindPointedNode(e.GetPosition(GraphViewer));

    if (node?.NodeInfo.ElementData != null) {
      await ShowPreviewPopup(node);
      hoveredNode_ = node;
    }
  }

  private void Hover_MouseHoverStopped(object sender, MouseEventArgs e) {
    HidePreviewPopupDelayed();
  }

  private void HidePreviewPopupDelayed() {
    removeHoveredAction_ = DelayedAction.StartNew(() => {
      if (removeHoveredAction_ != null) {
        removeHoveredAction_ = null;
        HidePreviewPopup();
      }
    });

    ignoreNextHover_ = false;
  }

  private void MarkBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.MarkSelectedNode(GetSelectedColorStyle(e));
  }

  private void MarkGroupExecuted(object sender, ExecutedRoutedEventArgs e) {
    var style = GetSelectedColorStyle(e);
    GraphViewer.MarkSelectedNode(style);
    GraphViewer.MarkSelectedNodeSuccessors(style);
    GraphViewer.MarkSelectedNodePredecessors(style);
  }

  private void MarkLoopExecuted(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.MarkSelectedNodeLoop(GetSelectedColorStyle(e));
  }

  private void MarkLoopNestExecuted(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.MarkSelectedNodeLoopNest(GetSelectedColorStyle(e));
  }

  private void MarkPredecessorsExecuted(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.MarkSelectedNodePredecessors(GetSelectedColorStyle(e));
  }

  private void MarkSuccessorsExecuted(object sender, ExecutedRoutedEventArgs e) {
    GraphViewer.MarkSelectedNodeSuccessors(GetSelectedColorStyle(e));
  }

  private async void MarkDominatorsExecuted(object sender, ExecutedRoutedEventArgs e) {
    await GraphViewer.MarkSelectedNodeDominatorsAsync(GetSelectedColorStyle(e)).ConfigureAwait(true);
  }

  private async void MarkPostDominatorsExecuted(object sender, ExecutedRoutedEventArgs e) {
    await GraphViewer.MarkSelectedNodePostDominatorsAsync(GetSelectedColorStyle(e)).ConfigureAwait(true);
  }

  private async void MarkDominanceFrontierExecuted(object sender, ExecutedRoutedEventArgs e) {
    await GraphViewer.MarkSelectedNodeDominanceFrontierAsync(GetSelectedColorStyle(e)).ConfigureAwait(true);
  }

  private async void MarkPostDominanceFrontierExecuted(object sender, ExecutedRoutedEventArgs e) {
    await GraphViewer.MarkSelectedNodePostDominanceFrontierAsync(GetSelectedColorStyle(e)).ConfigureAwait(true);
  }

  private void SelectQueryBlock1Executed(object sender, ExecutedRoutedEventArgs e) {
    if (hoveredNode_ != null) {
      if (hoveredNode_.NodeInfo.ElementData is BlockIR block) {
        SetQueryBlock1(block);
      }
    }
  }

  private void SelectQueryBlock2Executed(object sender, ExecutedRoutedEventArgs e) {
    if (hoveredNode_ != null) {
      if (hoveredNode_.NodeInfo.ElementData is BlockIR block) {
        SetQueryBlock2(block);
      }
    }
  }

  private async void SwapQueryBlocksExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!queryPanelVisible_) {
      return;
    }

    queryInfo_.SwapBlocks();
    await UpdateQueryResult();
  }

  private void CloseQueryPanelExecuted(object sender, ExecutedRoutedEventArgs e) {
    HideQueryPanel();
  }

  private async void ShowReachablePathExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (queryPanelVisible_ && queryInfo_.Reaches) {
      var cache = FunctionAnalysisCache.Get(Document.Function);

      var pathBlocks =
        (await cache.GetReachabilityAsync()).FindPath(queryInfo_.Block1, queryInfo_.Block2);

      foreach (var block in pathBlocks) {
        GraphViewer.Mark(block, Colors.Gold);
      }
    }
  }

  private void PanelToolbarTray_DuplicateClicked(object sender, DuplicateEventArgs e) {
    Session.DuplicatePanel(this, e.Kind);
  }

  private void PanelToolbarTray_PinnedChanged(object sender, PinEventArgs e) {
    HasPinnedContent = e.IsPinned;
  }

  private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
    ShowOptionsPanel();
  }

  private async void SetQueryBlock1(BlockIR block) {
    ShowQueryPanel();

    if (queryInfo_.Block1 != block) {
      queryInfo_.Block1 = block;
      queryInfo_.Block1Name = Utils.MakeBlockDescription(block);
      await UpdateQueryResult();
    }
  }

  private async void SetQueryBlock2(BlockIR block) {
    ShowQueryPanel();

    if (queryInfo_.Block2 != block) {
      queryInfo_.Block2 = block;
      queryInfo_.Block2Name = Utils.MakeBlockDescription(block);
      await UpdateQueryResult();
    }
  }

  private async Task UpdateQueryResult() {
    QueryPanel.DataContext = null;

    if (queryInfo_.Block1 != null && queryInfo_.Block2 != null) {
      var cache = FunctionAnalysisCache.Get(Document.Function);
      await cache.CacheAll();

      queryInfo_.Dominates =
        (await cache.GetDominatorsAsync()).Dominates(queryInfo_.Block1, queryInfo_.Block2);

      queryInfo_.PostDominates =
        (await cache.GetPostDominatorsAsync()).Dominates(queryInfo_.Block1, queryInfo_.Block2);

      queryInfo_.Reaches =
        (await cache.GetReachabilityAsync()).Reaches(queryInfo_.Block1, queryInfo_.Block2);
    }

    QueryPanel.DataContext = queryInfo_;
  }

  private void ShowQueryPanel() {
    if (queryPanelVisible_) {
      return;
    }

    queryInfo_ = new GraphQueryInfo();
    QueryPanel.DataContext = queryInfo_;
    QueryPanel.Visibility = Visibility.Visible;
    queryPanelVisible_ = true;
  }

  private void HideQueryPanel() {
    if (!queryPanelVisible_) {
      return;
    }

    QueryPanel.DataContext = null;
    QueryPanel.Visibility = Visibility.Collapsed;
    queryPanelVisible_ = false;
  }

  private void ShowOptionsPanel() {
    if (optionsPanelPopup_ != null) {
      optionsPanelPopup_.ClosePopup();
      optionsPanelPopup_ = null;
      return;
    }

    if (PanelKind == ToolPanelKind.ExpressionGraph) {
      optionsPanelPopup_ = OptionsPanelHostPopup.Create<ExpressionGraphOptionsPanel, ExpressionGraphSettings>(
        Settings.Clone(), GraphHost, Session,
        async (newSettings, commit) => {
          if (!newSettings.Equals(Settings)) {
            App.Settings.ExpressionGraphSettings = newSettings;
            ReloadSettings();

            if (commit) {
              App.SaveApplicationSettings();
            }
          }

          return newSettings.Clone();
        },
        () => optionsPanelPopup_ = null);
    }
    else {
      optionsPanelPopup_ = OptionsPanelHostPopup.Create<FlowGraphOptionsPanel, FlowGraphSettings>(
        Settings.Clone(), GraphHost, Session,
        async (newSettings, commit) => {
          if (!newSettings.Equals(Settings)) {
            App.Settings.FlowGraphSettings = newSettings;
            ReloadSettings();

            if (commit) {
              App.SaveApplicationSettings();
            }
          }

          return newSettings.Clone();
        },
        () => optionsPanelPopup_ = null);
    }
  }

  private void SetupCommands() {
    AddCommand(GraphCommand.MarkBlock, MarkBlockExecuted);
    AddCommand(GraphCommand.MarkPredecessors, MarkPredecessorsExecuted);
    AddCommand(GraphCommand.MarkSuccessors, MarkSuccessorsExecuted);
    AddCommand(GraphCommand.MarkDominators, MarkDominatorsExecuted);
    AddCommand(GraphCommand.MarkPostDominators, MarkPostDominatorsExecuted);
    AddCommand(GraphCommand.MarkDominanceFrontier, MarkDominanceFrontierExecuted);
    AddCommand(GraphCommand.MarkPostDominanceFrontier, MarkPostDominanceFrontierExecuted);
    AddCommand(GraphCommand.MarkGroup, MarkGroupExecuted);
    AddCommand(GraphCommand.MarkLoop, MarkLoopExecuted);
    AddCommand(GraphCommand.MarkLoopNest, MarkLoopNestExecuted);
    AddCommand(GraphCommand.ClearMarked, ClearMarkedExecuted);
    AddCommand(GraphCommand.ClearAllMarked, ClearAllMarkedExecuted);
    AddCommand(GraphCommand.SelectQueryBlock1, SelectQueryBlock1Executed);
    AddCommand(GraphCommand.SelectQueryBlock2, SelectQueryBlock2Executed);
    AddCommand(GraphCommand.SwapQueryBlocks, SwapQueryBlocksExecuted);
    AddCommand(GraphCommand.CloseQueryPanel, CloseQueryPanelExecuted);
    AddCommand(GraphCommand.ShowReachablePath, ShowReachablePathExecuted);
  }

  private void SetZoom(double value) {
    double currentZoom = GraphViewer.ZoomLevel;
    double centerX = GraphHost.ViewportWidth / 2;
    double centerY = GraphHost.ViewportHeight / 2;
    double offsetX = (GraphHost.HorizontalOffset + centerX) / currentZoom;
    double offsetY = (GraphHost.VerticalOffset + centerY) / currentZoom;
    double zoom = value;
    zoom = Math.Min(Math.Max(MinZoomLevel, zoom), MaxZoomLevel);
    GraphViewer.ZoomLevel = zoom;
    UpdateLayout();
    GraphHost.ScrollToHorizontalOffset(offsetX * zoom - centerX);
    GraphHost.ScrollToVerticalOffset(offsetY * zoom - centerY);
  }

  private async Task ShowPreviewPopup(GraphNode node) {
    if (previewPopup_ != null) {
      if (hoveredNode_ == node) {
        return;
      }

      HidePreviewPopup();
    }

    if (node == null) {
      return;
    }

    if (removeHoveredAction_ != null) {
      removeHoveredAction_.Cancel();
      removeHoveredAction_ = null;
    }

    var position = Mouse.GetPosition(GraphHost).AdjustForMouseCursor();
    previewPopup_ = await IRDocumentPopup.CreateNew(Document, node.NodeInfo.ElementData, position,
                                                    GraphHost,
                                                    App.Settings.
                                                      GetElementPreviewPopupSettings(ToolPanelKind.FlowGraph),
                                                    "Block ");
    previewPopup_.PopupDetached += Popup_PopupDetached;
    previewPopup_.ShowPopup();
  }

  private void Popup_PopupDetached(object sender, EventArgs e) {
    var popup = (IRDocumentPopup)sender;

    if (popup == previewPopup_) {
      previewPopup_ = null; // Prevent automatic closing.
    }
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private async void CancelButton_Click(object sender, RoutedEventArgs e) {
    await loadTask_.CancelTaskAndWaitAsync();
  }

  private void QueryPanel_MouseEnter(object sender, MouseEventArgs e) {
    var animation = new DoubleAnimation(1, TimeSpan.FromSeconds(0.1));
    QueryPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
  }

  private void QueryPanel_MouseLeave(object sender, MouseEventArgs e) {
    var animation = new DoubleAnimation(0.5, TimeSpan.FromSeconds(0.3));
    QueryPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
  }

  private void PanelToolbarTray_BindMenuItemSelected(object sender, BindMenuItem e) {
    Session.BindToDocument(this, e);
  }

  private void PanelToolbarTray_BindMenuOpen(object sender, BindMenuItemsArgs e) {
    Session.PopulateBindMenu(this, e);
  }

  public override void OnRegisterPanel() {
    IsPanelEnabled = false;
    ReloadSettings();
  }

  public override void OnRedrawPanel() {
    GraphViewer.RedrawCurrentGraph();
  }

  public override async Task OnReloadSettings() {
    ReloadSettings();
  }

  private void ReloadSettings() {
    GraphViewer.Settings = Settings;
    GraphHost.Background = ColorBrushes.GetBrush(Settings.BackgroundColor);
  }

  public override async Task OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
    var previousSection = Document?.Section;
    InitializeFromDocument(document);

    if (document.DuringSectionLoading) {
#if DEBUG
      Trace.TraceInformation(
        $"Graph panel {ObjectTracker.Track(this)}: Ignore graph reload during section switch");
#endif

      delayRestoreState_ = !restoredState_;
      return;
    }

    //? TODO: Implement switching for expressions
    if (PanelKind == ToolPanelKind.ExpressionGraph ||
        PanelKind == ToolPanelKind.CallGraph) {
      HideGraph();
      return;
    }

    if (section != null && section != previousSection) {
      // User switched between two sections, reload the proper graph.
      delayRestoreState_ = !delayRestoreState_;
      await Session.SwitchGraphsAsync(this, section, document);
      return;
    }

    if (!restoredState_) {
      delayRestoreState_ = true;
    }
    else {
      Dispatcher.BeginInvoke(() => LoadSavedState(), DispatcherPriority.Render);
    }
  }

  public override void OnSessionStart() {
    base.OnSessionStart();
    loadTask_ = new CancelableTaskInstance(false, Session.SessionState.RegisterCancelableTask,
                                           Session.SessionState.UnregisterCancelableTask);
  }

  public override void OnSessionEnd() {
    base.OnSessionEnd();
    HideGraph();
  }

  private void LoadSavedState() {
    //? TODO: This can happen for the expression graph, which does not support switching.
    if (Document == null) {
      return;
    }

    byte[] data = Session.LoadPanelState(this, Document.Section) as byte[];
    var state = StateSerializer.Deserialize<GraphPanelState>(data, Document.Function);

    if (state != null) {
      SetZoom(state.ZoomLevel);
      GraphHost.ScrollToHorizontalOffset(state.HorizontalOffset);
      GraphHost.ScrollToVerticalOffset(state.VerticalOffset);
      restoredState_ = true;
    }
    else {
      GraphHost.ScrollToHorizontalOffset(0);
      GraphHost.ScrollToVerticalOffset(0);
      restoredState_ = false;
    }

    delayRestoreState_ = false;
  }

  public override async Task OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
    if (Section != section) {
      return;
    }

    HidePreviewPopup(true);
    HideQueryPanel();
    DisablePanel();
    restoredState_ = false;

    var state = new GraphPanelState();
    state.ZoomLevel = GraphViewer.ZoomLevel;
    state.HorizontalOffset = GraphHost.HorizontalOffset;
    state.VerticalOffset = GraphHost.VerticalOffset;
    byte[] data = StateSerializer.Serialize(state, document.Function);
    Session.SavePanelState(data, this, section);

    // Clear references to IR objects that would keep the previous function alive.
#if DEBUG
    Trace.TraceInformation($"Graph panel {ObjectTracker.Track(this)}: unloaded doc {ObjectTracker.Track(Document)}");
#endif

    Document = null;
    graph_ = null;
    hoveredNode_ = null;
  }

  private void EnablePanel() {
    IsPanelEnabled = true;
    Utils.EnableControl(GraphHost);
    Utils.EnableControl(ToolbarHost);
  }

  private void DisablePanel() {
    Utils.DisableControl(ToolbarHost);
    Utils.DisableControl(GraphHost, 0.5);
    IsPanelEnabled = false;
  }

  public override void OnElementSelected(IRElementEventArgs e) {
    if (!Settings.SyncSelectedNodes) {
      return;
    }

    if (e.Element is BlockIR block) {
      SelectedElement = block;
    }
  }

  public override void OnElementHighlighted(IRHighlightingEventArgs e) {
    if (!Settings.SyncSelectedNodes) {
      return;
    }

    if (e.Action == HighlightingEventAction.ReplaceHighlighting ||
        e.Action == HighlightingEventAction.AppendHighlighting) {
      Highlight(e);
    }
    else if (e.Action == HighlightingEventAction.RemoveHighlighting) {
      if (e.Element != null) {
        RemoveHighlighting(e.Element);
      }
      else {
        RemoveAllHighlighting(e.Type);
      }
    }
  }

  public override void OnActivatePanel() {
    if (delayFitSize_ && GraphViewer.IsGraphLoaded) {
      FitGraphIntoView();
    }
  }

  public override async void ClonePanel(IToolPanel sourcePanel) {
    var sourceGraphPanel = sourcePanel as GraphPanel;
    InitializeFromDocument(sourceGraphPanel.Document);

    // Rebuild the graph, otherwise the visual nodes
    // point to the same instance.
    var newGraph = await Session.ComputeGraphAsync(sourceGraphPanel.graph_.Kind,
                                                   Section, Document);
    DisplayGraph(newGraph);
  }

  private class BlockTooltipInfo {
    public BlockTooltipInfo(BlockIR block) {
      PredecessorCount = block.Predecessors.Count;
      SuccessorCount = block.Successors.Count;
      var loopTag = block.GetTag<LoopBlockTag>();

      if (loopTag != null) {
        InLoop = true;
        LoopNesting = loopTag.NestingLevel;
        LoopBlocks = loopTag.Loop.Blocks.Count;
        NestedLoops = loopTag.Loop.NestedLoops.Count;
      }
    }

    public bool InLoop { get; set; }
    public int LoopBlocks { get; set; }
    public int LoopNesting { get; set; }
    public int NestedLoops { get; set; }
    public int PredecessorCount { get; set; }
    public int SuccessorCount { get; set; }
  }

  private async void PanelToolbarTray_OnHelpClicked(object sender, EventArgs e) {
    await HelpPanel.DisplayPanelHelp(PanelKind, Session);
  }
}

class GraphQueryInfo {
  public BlockIR Block1 { get; set; }
  public BlockIR Block2 { get; set; }
  public string Block1Name { get; set; }
  public string Block2Name { get; set; }
  public bool Dominates { get; set; }
  public bool PostDominates { get; set; }
  public bool Reaches { get; set; }
  public bool ControlDependent { get; set; }
  public bool OnDomFrontier { get; set; }

  public void SwapBlocks() {
    var tempBlock = Block1;
    string tempBlockName = Block1Name;
    Block1 = Block2;
    Block1Name = Block2Name;
    Block2 = tempBlock;
    Block2Name = tempBlockName;
  }
}