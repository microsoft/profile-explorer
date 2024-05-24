// Copyright (c) Microsoft Corporation
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
using IRExplorerUI.OptionsPanels;
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
  private FlameGraphNode rootNode_;
  private OptionsPanelHostPopup optionsPanelPopup_;

  public FlameGraphPanel() {
    InitializeComponent();
    settings_ = App.Settings.FlameGraphSettings;
    SetupEvents();
    DataContext = this;
    CallTree = null;
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

  public bool HasEnabledMarkedFunctions =>
    MarkingSettings.UseFunctionColors && MarkingSettings.FunctionColors.Count > 0;
  public bool HasEnabledMarkedModules => MarkingSettings.UseModuleColors && MarkingSettings.ModuleColors.Count > 0;
  public FunctionMarkingSettings MarkingSettings => App.Settings.MarkingSettings;

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
      return; //? TODO: Maybe do the init now?
    }

    GraphHost.ClearSelection();
    var groupNode = callTree_.GetCombinedCallTreeNode(function);

    if (groupNode is {Nodes.Count: > 0}) {
      GraphHost.SelectNode(groupNode, false, bringIntoView);
      await NodeDetailsPanel.ShowWithDetailsAsync(groupNode);
    }
  }

  public async Task SelectFunction(ProfileCallTreeNode node, bool bringIntoView = true, bool showDetails = true) {
    if (!HasCallTree) {
      return; //? TODO: Maybe do the init now?
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
    GraphHost.ResetWidth(false);
  }

  private void GraphHostOnRootNodeChanged(object sender, FlameGraphNode node) {
    HasRootNode = true;
    RootNode = node;
    GraphHost.ResetWidth(false);
  }

  public FlameGraphNode RootNode {
    get => rootNode_;
    set => SetField(ref rootNode_, value);
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
    if (node.HasFunction) {
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
  }

  private void NodeDetailsPanel_NodesSelected(object sender, List<ProfileCallTreeNode> e) {
    GraphHost.SelectNodes(e);
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
      var openMode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent;
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
    var prevSearchResultNodes = searchResultNodes_;
    searchResultNodes_ = null;

    if (prevSearchResultNodes != null) {
      bool redraw = text.Length <= 1; // Prevent flicker by redrawing once when search is done.
      GraphHost.GraphViewer.ResetSearchResultNodes(prevSearchResultNodes, redraw);
    }

    if (text.Length > 1) {
      bool caseInsensitive = !App.Settings.SectionSettings.FunctionSearchCaseSensitive;
      searchResultNodes_ = await Task.Run(() => GraphHost.FlameGraph.SearchNodes(text, caseInsensitive));
      GraphHost.GraphViewer.MarkSearchResultNodes(searchResultNodes_);
      UpdateSearchResultText();

      searchResultIndex_ = -1;
      SelectNextSearchResult();
      ShowSearchSection = true;
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

  private void SelectPreviousSearchResult() {
    if (searchResultNodes_ != null && searchResultIndex_ > 0) {
      searchResultIndex_--;
      UpdateSearchResultText();
      GraphHost.SelectNode(searchResultNodes_[searchResultIndex_], true);
    }
  }

  private void SelectNextSearchResult() {
    if (searchResultNodes_ != null && searchResultIndex_ < searchResultNodes_.Count - 1) {
      searchResultIndex_++;
      UpdateSearchResultText();
      GraphHost.SelectNode(searchResultNodes_[searchResultIndex_], true);
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
    var initialMarkingSettings = MarkingSettings.Clone();
    FrameworkElement relativeControl = settings_.ShowDetailsPanel ? NodeDetailsPanel : GraphHost;
    optionsPanelPopup_ = OptionsPanelHostPopup.Create<FlameGraphOptionsPanel, FlameGraphSettings>(
      settings_.Clone(), relativeControl, Session,
      async (newSettings, commit) => {
        if (!newSettings.Equals(settings_) ||
            !initialMarkingSettings.Equals(MarkingSettings)) {
          Settings = newSettings;
          NodeDetailsPanel.Settings = App.Settings.CallTreeNodeSettings;
          App.Settings.FlameGraphSettings = newSettings;

          if (commit) {
            App.SaveApplicationSettings();
          }

          initialMarkingSettings = MarkingSettings.Clone();
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

  private async void ClearModulesButton_Click(object sender, RoutedEventArgs e) {
    MarkingSettings.ModuleColors.Clear();
    await ReloadSettings();
  }

  private async void ClearFunctionsButton_Click(object sender, RoutedEventArgs e) {
    MarkingSettings.FunctionColors.Clear();
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

  private async void ModuleMenu_OnSubmenuOpened(object sender, RoutedEventArgs e) {
    await ProfilingUtils.PopulateMarkedModulesMenu(ModuleMenu, MarkingSettings, Session,
                                                   e.OriginalSource, ReloadSettings);
  }

  private async void FunctionMenu_OnSubmenuOpened(object sender, RoutedEventArgs e) {
    await ProfilingUtils.PopulateMarkedFunctionsMenu(FunctionMenu, MarkingSettings, Session,
                                                     e.OriginalSource, ReloadSettings);
  }

  public void UpdateMarkedFunctions(bool externalCall) {
    GraphHost.Redraw();
    OnPropertyChanged(nameof(HasEnabledMarkedModules));
    OnPropertyChanged(nameof(HasEnabledMarkedFunctions));
    NodeDetailsPanel.UpdateMarkedFunctions();
  }

  private async void CopyMarkedFunctionMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.CopyFunctionMarkingsAsHtml(Session);
  }

  private async void ExportMarkedFunctionsHtmlMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.ExportFunctionMarkingsAsHtmlFile(Session);
  }

  private async void ExportMarkedFunctionsMarkdownMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.ExportFunctionMarkingsAsMarkdownFile(Session);
  }

  private async void CopyMarkedModulesMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.CopyModuleMarkingsAsHtml(Session);
  }

  private async void ExportMarkedModulesHtmlMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.ExportModuleMarkingsAsHtmlFile(Session);
  }

  private async void ExportMarkedModulesMarkdownMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.ExportModuleMarkingsAsMarkdownFile(Session);
  }
}
