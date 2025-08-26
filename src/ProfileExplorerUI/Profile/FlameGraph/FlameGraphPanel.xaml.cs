﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ProfileExplorer.Core;
using ProfileExplorer.UI.OptionsPanels;
using ProfileExplorer.UI.Panels;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.Core.Profile.CallTree;
using ProfileExplorer.Core.Profile.Data;

namespace ProfileExplorer.UI.Profile;

public partial class FlameGraphPanel : ToolPanelControl, IFunctionProfileInfoProvider, INotifyPropertyChanged {
  private FlameGraphSettings settings_;
  private bool panelVisible_;
  private ProfileCallTree callTree_;
  private ProfileCallTree pendingCallTree_; // Tree to show when panel becomes visible.
  private List<FlameGraphNode> searchResultNodes_;
  private int searchResultIndex_;
  private bool showSearchSection_;
  private string searchResultText_;
  private bool hasRootNode;
  private FlameGraphNode rootNode_;
  private OptionsPanelHostPopup optionsPanelPopup_;
  private CancelableTaskInstance searchTask_;

  public FlameGraphPanel() {
    InitializeComponent();
    settings_ = App.Settings.FlameGraphSettings;
    searchTask_ = new CancelableTaskInstance(false);
    SetupEvents();
    DataContext = this;
    CallTree = null;
  }

  public override ToolPanelKind PanelKind => ToolPanelKind.FlameGraph;

  public override IUISession Session {
    get => base.Session;
    set {
      base.Session = value;
      GraphHost.Session = value;
      NodeDetailsPanel.Initialize(value, this);
    }
  }

  public FlameGraphSettings Settings {
    get => settings_;
    set {
      settings_ = value;
      ReloadSettings();
      OnPropertyChanged();
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

  public FunctionMarkingSettings MarkingSettings => App.Settings.MarkingSettings;

  public FlameGraphNode RootNode {
    get => rootNode_;
    set => SetField(ref rootNode_, value);
  }

  public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
    return callTree_.GetBacktrace(node);
  }

  public (List<ProfileCallTreeNode>, List<ModuleProfileInfo> Modules) GetTopFunctionsAndModules(
    ProfileCallTreeNode node) {
    return callTree_.GetTopFunctionsAndModules(node);
  }

  public event PropertyChangedEventHandler PropertyChanged;

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
    NodeDetailsPanel.SaveListColumnSettings();
    NodeDetailsPanel.Reset();
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
      return;
    }

    GraphHost.ClearSelection();
    var groupNode = callTree_.GetCombinedCallTreeNode(function);

    if (groupNode is {Nodes.Count: > 0}) {
      GraphHost.SelectNode(groupNode, true, bringIntoView);
      await NodeDetailsPanel.ShowWithDetailsAsync(groupNode);
    }
  }

  public async Task SelectFunction(ProfileCallTreeNode node, bool bringIntoView = true, bool showDetails = true) {
    if (!HasCallTree) {
      return;
    }

    if (node is ProfileCallTreeGroupNode groupNode) {
      node = groupNode.Nodes[0];
    }

    GraphHost.ClearSelection();
    GraphHost.SelectNode(node, true, bringIntoView);

    if (showDetails) {
      await NodeDetailsPanel.ShowWithDetailsAsync(node);
    }
  }

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
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
      }, DispatcherPriority.Render);

      pendingCallTree_ = null;
    }
  }

  private async Task InitializeCallTree(ProfileCallTree callTree) {
    CallTree = callTree;
    NodeDetailsPanel.Reset();
    await GraphHost.InitializeFlameGraph(callTree);
  }

  private void SetupEvents() {
    GraphHost.NodeSelected += GraphHost_NodeSelected;
    GraphHost.NodesDeselected += GraphHost_NodesDeselected;
    GraphHost.RootNodeChanged += GraphHostOnRootNodeChanged;
    GraphHost.RootNodeCleared += GraphHostOnRootNodeCleared;
    GraphHost.MarkingChanged += (sender, args) => UpdateMarkingUI();

    // Setup events for the node details view.
    NodeDetailsPanel.NodeInstanceChanged += NodeDetailsPanel_NodeInstanceChanged;
    NodeDetailsPanel.BacktraceNodeClick += NodeDetailsPanel_NodeClick;
    NodeDetailsPanel.BacktraceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
    NodeDetailsPanel.InstanceNodeClick += NodeDetailsPanel_NodeClick;
    NodeDetailsPanel.InstanceNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
    NodeDetailsPanel.FunctionNodeClick += NodeDetailsPanel_NodeClick;
    NodeDetailsPanel.FunctionNodeDoubleClick += NodeDetailsPanel_NodeDoubleClick;
    NodeDetailsPanel.NodesSelected += NodeDetailsPanel_NodesSelected;
    NodeDetailsPanel.MarkingChanged += (sender, args) => UpdateMarkingUI();
  }

  private void GraphHostOnRootNodeCleared(object sender, EventArgs e) {
    HasRootNode = false;
    RootNode = null;
    GraphHost.ResetWidth();
  }

  private void GraphHostOnRootNodeChanged(object sender, FlameGraphNode node) {
    HasRootNode = true;
    RootNode = node;
    GraphHost.ResetWidth();
  }

  private async Task UpdateNodeDetailsPanel() {
    var selectedNodes = GraphHost.SelectedNodes;

    if (selectedNodes.Count == 0) {
      NodeDetailsPanel.Reset();
    }
    else {
      var callTreeNodes = new List<ProfileCallTreeNode>();

      foreach (var node in selectedNodes) {
        if (node.HasFunction) {
          callTreeNodes.Add(node.CallTreeNode);
        }
      }

      // Display the a combined view of all selected nodes.
      var combinedNode = await Task.Run(() => ProfileCallTree.CombinedCallTreeNodes(callTreeNodes));
      await NodeDetailsPanel.ShowWithDetailsAsync(combinedNode);
    }
  }

  private async void GraphHost_NodesDeselected(object sender, EventArgs e) {
    await UpdateNodeDetailsPanel();

    if (settings_.SyncSelection) {
      await Session.ProfileFunctionDeselected();
    }

    if (GraphHost.SelectedNodes.Count == 0) {
      Session.SetApplicationStatus("");
    }
  }

  private async void GraphHost_NodeSelected(object sender, FlameGraphNode node) {
    if (!node.HasFunction) {
      return;
    }

    await UpdateNodeDetailsPanel();

    if (settings_.SyncSourceFile) {
      // Load the source file and scroll to the hottest line.
      await Session.OpenProfileSourceFile(node.CallTreeNode);
    }

    if (settings_.SyncSelection) {
      await Session.ProfileFunctionSelected(node.CallTreeNode, PanelKind);
    }

    // When selecting multiple nodes, display the weight sum in the status bar.
    var selectedNodes = GraphHost.SelectedNodes;

    if (selectedNodes.Count > 1) {
      var nodes = new List<ProfileCallTreeNode>();

      foreach (var selectedNode in selectedNodes) {
        if (selectedNode.HasFunction) {
          nodes.Add(selectedNode.CallTreeNode);
        }
      }

      var selectionWeight = ProfileCallTree.CombinedCallTreeNodesWeight(nodes);
      double weightPercentage = 0;

      if (rootNode_ != null) {
        // Scale based on the current root node, which may be overriden.
        weightPercentage = selectionWeight.Ticks / (double)rootNode_.Weight.Ticks;
      }
      else {
        weightPercentage = Session.ProfileData.ScaleFunctionWeight(selectionWeight);
      }

      string text =
        $"Selected {nodes.Count}: {weightPercentage.AsPercentageString()} ({selectionWeight.AsMillisecondsString()})";
      Session.SetApplicationStatus(text, "Sum of selected flame graph nodes");
    }
    else {
      Session.SetApplicationStatus("");
    }
  }

  private void NodeDetailsPanel_NodesSelected(object sender, List<ProfileCallTreeNode> e) {
    GraphHost.SelectNodes(e, true);
  }

  private void NodeDetailsPanel_NodeInstanceChanged(object sender, ProfileCallTreeNode e) {
    GraphHost.SelectNode(e, true);
  }

  private async void NodeDetailsPanel_NodeClick(object sender, ProfileCallTreeNode e) {
    if (settings_.SyncSelection) {
      await Session.ProfileFunctionSelected(e, ToolPanelKind.FlameGraph);
    }
  }

  private async void NodeDetailsPanel_NodeDoubleClick(object sender, ProfileCallTreeNode e) {
    await OpenFunction(e);
  }

  private async Task OpenFunction(ProfileCallTreeNode node) {
    if (node is {HasFunction: true} && node.Function.HasSections) {
      var openMode = Utils.IsControlModifierActive() ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent;
      await Session.OpenProfileFunction(node, openMode);
    }
  }

  private void ExecuteGraphResetWidth(object sender, ExecutedRoutedEventArgs e) {
    GraphHost.ResetWidth();
  }

  private void ExecuteGraphZoomIn(object sender, ExecutedRoutedEventArgs e) {
    GraphHost.ZoomIn();
  }

  private void ExecuteGraphZoomOut(object sender, ExecutedRoutedEventArgs e) {
    GraphHost.ZoomOut();
  }

  private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
    ShowOptionsPanel();
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private async void UndoButton_Click(object sender, RoutedEventArgs e) {
    await GraphHost.RestorePreviousState();
  }

  private async void FunctionFilter_OnTextChanged(object sender, TextChangedEventArgs e) {
    string text = FunctionFilter.Text.Trim();
    await SearchFlameGraph(text);
  }

  private async Task SearchFlameGraph(string text) {
    // Wait for a potentially ongoing search to finish.
    using var task = await searchTask_.CancelCurrentAndCreateTaskAsync();

    // Reset marking of previous results.
    var prevSearchResultNodes = searchResultNodes_;
    searchResultNodes_ = null;

    if (prevSearchResultNodes != null) {
      GraphHost.GraphViewer.ResetSearchResultNodes(prevSearchResultNodes);
    }

    if (text.Length > 1) {
      bool caseInsensitive = !App.Settings.SectionSettings.FunctionSearchCaseSensitive;
      searchResultNodes_ = await Task.Run(() => GraphHost.FlameGraph.SearchNodes(text, caseInsensitive));
      GraphHost.GraphViewer.MarkSearchResultNodes(searchResultNodes_);
      UpdateSearchResultText();

      // Update search result navigation.
      searchResultIndex_ = -1;
      ShowSearchSection = true;
      SelectNextSearchResultNoLock();
    }
    else {
      ShowSearchSection = false;
    }
  }

  private void UpdateSearchResultText() {
    SearchResultText = searchResultNodes_ is {Count: > 0} ?
      $"{searchResultIndex_ + 1} / {searchResultNodes_.Count}"
      : "Not found";
  }

  private async Task SelectPreviousSearchResult() {
    using var task = await searchTask_.CancelCurrentAndCreateTaskAsync();

    if (searchResultNodes_ != null && searchResultIndex_ > 0) {
      searchResultIndex_--;
      UpdateSearchResultText();
      GraphHost.SelectNode(searchResultNodes_[searchResultIndex_], true);
    }
  }

  private async Task SelectNextSearchResult() {
    using var task = await searchTask_.CancelCurrentAndCreateTaskAsync();
    SelectNextSearchResultNoLock();
  }

  private void SelectNextSearchResultNoLock() {
    if (searchResultNodes_ != null && searchResultIndex_ < searchResultNodes_.Count - 1) {
      searchResultIndex_++;
      UpdateSearchResultText();
      GraphHost.SelectNode(searchResultNodes_[searchResultIndex_], true);
    }
  }

  private async void PreviousSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
    await SelectPreviousSearchResult();
  }

  private async void NextSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
    await SelectNextSearchResult();
  }

  private void ClearSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
    ((TextBox)e.Parameter).Text = string.Empty;
  }

  private void FocusSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
    FunctionFilter.Focus();
    FunctionFilter.SelectAll();
  }

  private async void PanelToolbarTray_OnHelpClicked(object sender, EventArgs e) {
    await HelpPanel.DisplayPanelHelp(PanelKind, Session);
  }

  private async void RootNodeResetButton_OnClick(object sender, RoutedEventArgs e) {
    await GraphHost.ClearRootNode();
  }

  private void ShowOptionsPanel() {
    if (optionsPanelPopup_ != null) {
      optionsPanelPopup_.ClosePopup();
      optionsPanelPopup_ = null;
      return;
    }

    //? TODO: Redesign settings change detection, doesn't work well
    //? when a panel shows multiple settings objects.
    optionsPanelPopup_ = OptionsPanelHostPopup.Create<FlameGraphOptionsPanel, FlameGraphSettings>(
      settings_.Clone(), GraphDetailsPanelHost, Session,
      async (newSettings, commit) => {
        if (!newSettings.Equals(settings_)) {
          Settings = newSettings;
          NodeDetailsPanel.Settings = App.Settings.CallTreeNodeSettings;
          App.Settings.FlameGraphSettings = newSettings;

          if (commit) {
            App.SaveApplicationSettings();
          }

          return settings_.Clone();
        }

        return null;
      },
      () => optionsPanelPopup_ = null);
  }

  private async void ToggleButton_Click(object sender, RoutedEventArgs e) {
    // Force an update for toolbar buttons.
    await ReloadSettings();
  }

  private async Task ReloadSettings() {
    GraphHost.SettingsUpdated(settings_);
    UpdateMarkingUI();
  }

  private void UpdateMarkingUI() {
    UpdateMarkedFunctions(false);
    Session.FunctionMarkingChanged(ToolPanelKind.FlameGraph);
  }

  public override async Task OnReloadSettings() {
    Settings = App.Settings.FlameGraphSettings;
  }

  public void UpdateMarkedFunctions(bool externalCall) {
    GraphHost.Redraw();
    NodeDetailsPanel.UpdateMarkedFunctions();
  }
}