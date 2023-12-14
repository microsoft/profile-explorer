﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerUI.Controls;
using IRExplorerUI.Panels;

namespace IRExplorerUI.Profile;

public partial class FlameGraphPanel : ToolPanelControl, IFunctionProfileInfoProvider, INotifyPropertyChanged {
  private FlameGraphSettings settings_;
  private bool dragging_;
  private Point draggingStart_;
  private Point draggingViewStart_;
  private bool panelVisible_;
  private ProfileCallTree callTree_;
  private ProfileCallTree pendingCallTree_; // Tree to show when panel becomes visible.
  private FlameGraphNode enlargedNode_;
  private DateTime lastWheelZoomTime_;
  private List<FlameGraphNode> searchResultNodes_;
  private int searchResultIndex_;
  private bool showSearchSection_;
  private string searchResultText_;
  private bool hasRootNode;
  private bool showNodePanel_;
  private FlameGraphNode rootNode_;

  public FlameGraphPanel() {
    InitializeComponent();
    settings_ = App.Settings.FlameGraphSettings;
    SetupEvents();
    DataContext = this;
    CallTree = null;
    ShowNodePanel = true;
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public override ToolPanelKind PanelKind => ToolPanelKind.FlameGraph;

  public override ISession Session {
    get => base.Session;
    set {
      base.Session = value;
      GraphHost.Session = value;
      NodeDetailsPanel.Initialize(value, this);
    }
  }

  public ProfileCallTree CallTree {
    get => callTree_;
    set {
      SetField(ref callTree_, value);
      OnPropertyChanged(nameof(HasCallTree));
    }
  }

  public bool HasCallTree => callTree_ != null;

  public bool PrependModuleToFunction {
    get => settings_.PrependModuleToFunction;
    set {
      if (value != settings_.PrependModuleToFunction) {
        settings_.PrependModuleToFunction = value;
        GraphHost.GraphViewer.SettingsUpdated(settings_);
        OnPropertyChanged();
      }
    }
  }

  public bool SyncSourceFile {
    get => settings_.SyncSourceFile;
    set {
      //? TODO: Use SetField everywhere
      if (value != settings_.SyncSourceFile) {
        settings_.SyncSourceFile = value;
        OnPropertyChanged();
      }
    }
  }

  public bool SyncSelection {
    get => settings_.SyncSelection;
    set {
      if (value != settings_.SyncSelection) {
        settings_.SyncSelection = value;
        OnPropertyChanged();
      }
    }
  }

  public bool UseCompactMode {
    get => settings_.UseCompactMode;
    set {
      if (value != settings_.UseCompactMode) {
        settings_.UseCompactMode = value;
        GraphHost.GraphViewer.SettingsUpdated(settings_);
        OnPropertyChanged();
      }
    }
  }

  public bool ShowSearchSection {
    get => showSearchSection_;
    set {
      if (showSearchSection_ != value) {
        showSearchSection_ = value;
        OnPropertyChanged();
      }
    }
  }

  public string SearchResultText {
    get => searchResultText_;
    set {
      if (searchResultText_ != value) {
        searchResultText_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool HasRootNode {
    get => hasRootNode;
    set {
      if (hasRootNode != value) {
        hasRootNode = value;
        OnPropertyChanged();
      }
    }
  }

  public bool ShowNodePanel {
    get => showNodePanel_;
    set => SetField(ref showNodePanel_, value);
  }

  public override async void OnShowPanel() {
    base.OnShowPanel();
    panelVisible_ = true;
    await InitializePendingCallTree();
  }

  public override async void OnSessionStart() {
    base.OnSessionStart();
    await InitializePendingCallTree();
  }

  public async Task DisplayFlameGraph() {
    var callTree = Session.ProfileData.CallTree;
    await SchedulePendingCallTree(callTree);
  }

  public override void OnSessionEnd() {
    base.OnSessionEnd();
    GraphHost.Reset();
    CallTree = null;
    pendingCallTree_ = null;
  }

  public void MarkFunctions(List<ProfileCallTreeNode> nodes) {
    GraphHost.MarkFunctions(nodes, GraphHost.GraphViewer.SelectedNodeStyle);
  }

  public void ClearMarkedFunctions() {
    GraphHost.ClearMarkedFunctions();
  }

  public async Task SelectFunction(IRTextFunction function, bool bringIntoView = true) {
    if (!HasCallTree) {
      return; //? TODO: Maybe do the init now?
    }

    var nodeList = callTree_.GetSortedCallTreeNodes(function);

    if (nodeList != null && nodeList.Count > 0) {
      await SelectFunction(nodeList[0], bringIntoView);
    }
  }

  public async Task SelectFunction(ProfileCallTreeNode node, bool bringIntoView = true) {
    if (!HasCallTree) {
      return; //? TODO: Maybe do the init now?
    }

    GraphHost.SelectNode(node, false, bringIntoView);
    await NodeDetailsPanel.ShowWithDetailsAsync(node);
  }

  public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
    return callTree_.GetBacktrace(node);
  }

  public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
    return callTree_.GetTopModules(node);
  }

  public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node) {
    return callTree_.GetTopFunctions(node);
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

  private async Task SchedulePendingCallTree(ProfileCallTree callTree) {
    // Display flame graph once the panel is visible and visible area is valid.
    if (pendingCallTree_ == null) {
      pendingCallTree_ = callTree;
      await InitializePendingCallTree();
    }
  }

  private async Task InitializePendingCallTree() {
    if (pendingCallTree_ != null && panelVisible_) {
      // Delay the initialization to ensure the panel is actually visible
      // and the available area is valid.
      await Dispatcher.BeginInvoke(async () => {
        await InitializeCallTree(pendingCallTree_);
      }, DispatcherPriority.Background);

      pendingCallTree_ = null;
    }
  }

  private async Task InitializeCallTree(ProfileCallTree callTree) {
    CallTree = callTree;
    await GraphHost.InitializeFlameGraph(callTree);
  }

  private void SetupEvents() {
    GraphHost.NodeSelected += GraphHost_NodeSelected;
    GraphHost.NodesDeselected += GraphHost_NodesDeselected;
    GraphHost.RootNodeChanged += GraphHostOnRootNodeChanged;
    GraphHost.RootNodeCleared += GraphHostOnRootNodeCleared;

    // Setup events for the node details view.
    NodeDetailsPanel.NodeInstanceChanged += NodeDetailsPanel_NodeInstanceChanged;
    NodeDetailsPanel.BacktraceNodeClick += NodeDetailsPanel_NodeClick;
    NodeDetailsPanel.BacktraceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
    NodeDetailsPanel.InstanceNodeClick += NodeDetailsPanel_NodeClick;
    NodeDetailsPanel.InstanceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
    NodeDetailsPanel.FunctionNodeClick += NodeDetailsPanel_NodeClick;
    NodeDetailsPanel.FunctionNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
    NodeDetailsPanel.NodesSelected += NodeDetailsPanel_NodesSelected;
  }

  private void GraphHostOnRootNodeCleared(object sender, EventArgs e) {
    HasRootNode = false;
    RootNode = null;
  }

  private void GraphHostOnRootNodeChanged(object sender, FlameGraphNode node) {
    HasRootNode = true;
    RootNode = node;
  }

  public FlameGraphNode RootNode {
    get => rootNode_;
    set => SetField(ref rootNode_, value);
  }

  private async void GraphHost_NodesDeselected(object sender, EventArgs e) {
    if (SyncSelection) {
      await Session.ProfileFunctionDeselected();
    }
  }

  private async void GraphHost_NodeSelected(object sender, FlameGraphNode node) {
    if (node.HasFunction) {
      await NodeDetailsPanel.ShowWithDetailsAsync(node.CallTreeNode);

      if (SyncSourceFile) {
        // Load the source file and scroll to the hottest line.
        await Session.OpenProfileSourceFile(node.CallTreeNode);
      }

      if (SyncSelection) {
        await Session.ProfileFunctionSelected(node.CallTreeNode, PanelKind);
      }
    }
  }

  private void NodeDetailsPanel_NodesSelected(object sender, List<ProfileCallTreeNode> e) {
    var nodes = GraphHost.GraphViewer.SelectNodes(e);

    if (nodes.Count > 0) {
      GraphHost.BringNodeIntoView(nodes[0], false);
    }
  }

  private void NodeDetailsPanel_NodeInstanceChanged(object sender, ProfileCallTreeNode e) {
    var node = GraphHost.GraphViewer.SelectNode(e);
    GraphHost.BringNodeIntoView(node);
  }

  private void NodeDetailsPanel_NodeClick(object sender, ProfileCallTreeNode e) {
    var nodes = GraphHost.GraphViewer.SelectNodes(e);

    if (nodes.Count > 0) {
      GraphHost.BringNodeIntoView(nodes[0], false);
    }
  }

  private async void NodeDetailsPanel_NodeDoubleClick(object sender, ProfileCallTreeNode e) {
    await OpenFunction(e);
  }

  private async Task OpenFunction(ProfileCallTreeNode node) {
    if (node != null && node.Function.HasSections) {
      var openMode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTabDockRight : OpenSectionKind.ReplaceCurrent;
      await Session.OpenProfileFunction(node, openMode);
    }
  }

  private void ExecuteGraphResetWidth(object sender, ExecutedRoutedEventArgs e) {
    //? TODO: Buttons should be disabled
    GraphHost.ResetWidth();
  }

  private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
    GraphHost.ZoomIn();
  }

  private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
    GraphHost.ZoomOut();
  }

  private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private async void UndoButtoon_Click(object sender, RoutedEventArgs e) {
    await GraphHost.RestorePreviousState();
  }

  private async void FunctionFilter_OnTextChanged(object sender, TextChangedEventArgs e) {
    string text = FunctionFilter.Text.Trim();
    await SearchFlameGraph(text);
  }

  private async Task SearchFlameGraph(string text) {
    var prevSearchResultNodes = searchResultNodes_;
    searchResultNodes_ = null;

    if (prevSearchResultNodes != null) {
      bool redraw = text.Length <= 1; // Prevent flicker by redrawing once when search is done.
      GraphHost.GraphViewer.ResetSearchResultNodes(prevSearchResultNodes, redraw);
    }

    if (text.Length > 1) {
      searchResultNodes_ = await Task.Run(() => GraphHost.GraphViewer.FlameGraph.SearchNodes(text));
      GraphHost.GraphViewer.MarkSearchResultNodes(searchResultNodes_);

      searchResultIndex_ = -1;
      SelectNextSearchResult();
      ShowSearchSection = true;
    }
    else {
      ShowSearchSection = false;
    }
  }

  private void UpdateSearchResultText() {
    SearchResultText = searchResultNodes_ is {Count: > 0} ? $"{searchResultIndex_ + 1} / {searchResultNodes_.Count}"
      : "Not found";
  }

  private void SelectPreviousSearchResult() {
    if (searchResultNodes_ != null && searchResultIndex_ > 0) {
      searchResultIndex_--;
      UpdateSearchResultText();
      GraphHost.SelectNode(searchResultNodes_[searchResultIndex_]);
    }
  }

  private void SelectNextSearchResult() {
    if (searchResultNodes_ != null && searchResultIndex_ < searchResultNodes_.Count - 1) {
      searchResultIndex_++;
      UpdateSearchResultText();
      GraphHost.SelectNode(searchResultNodes_[searchResultIndex_]);
    }
  }

  private void PreviousSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
    SelectPreviousSearchResult();
  }

  private void NextSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
    SelectNextSearchResult();
  }
  private void ClearSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
    ((TextBox)e.Parameter).Text = string.Empty;
  }

  private async void PanelToolbarTray_OnHelpClicked(object sender, EventArgs e) {
    await HelpPanel.DisplayPanelHelp(PanelKind, Session);
  }

  private async void RootNodeResetButton_OnClick(object sender, RoutedEventArgs e) {
    await GraphHost.ClearRootNode();
  }
}