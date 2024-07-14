// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aga.Controls.Tree;
using IRExplorerCore;
using IRExplorerUI.Controls;
using IRExplorerUI.OptionsPanels;
using IRExplorerUI.Panels;
using IRExplorerUI.Utilities;
using ProtoBuf.WellKnownTypes;

namespace IRExplorerUI.Profile;

public enum CallTreeListItemKind {
  Root,
  ChildrenPlaceholder,
  CallerNode,
  CalleeNode,
  CallTreeNode,
  Header
}

//? TODO: Replace all with RelayCommand pattern.
public static class CallTreeCommand {
  public static readonly RoutedCommand ExpandHottestCallPath = new("ExpandHottestCallPath", typeof(FrameworkElement));
  public static readonly RoutedCommand CollapseCallPath = new("CollapseCallPath", typeof(FrameworkElement));
  public static readonly RoutedCommand SelectFunction = new("SelectFunction", typeof(FrameworkElement));
  public static readonly RoutedCommand OpenFunction = new("OpenFunction", typeof(FrameworkElement));
  public static readonly RoutedCommand OpenFunctionInNewTab = new("OpenFunctionInNewTab", typeof(FrameworkElement));
  public static readonly RoutedCommand FocusSearch = new("FocusSearch", typeof(FrameworkElement));
  public static readonly RoutedCommand ClearSearch = new("ClearSearch", typeof(FrameworkElement));
  public static readonly RoutedCommand PreviousSearchResult = new("PreviousSearchResult", typeof(FrameworkElement));
  public static readonly RoutedCommand NextSearchResult = new("NextSearchResult", typeof(FrameworkElement));
  public static readonly RoutedCommand GoBack = new("GoBack", typeof(FrameworkElement));
  public static readonly RoutedCommand CollapseNodes = new("CollapseNodes", typeof(FrameworkElement));

  // FlameGraph specific commands.
  public static readonly RoutedCommand EnlargeNode = new("EnlargeNode", typeof(FrameworkElement));
  public static readonly RoutedCommand ChangeRootNode = new("ChangeRootNode", typeof(FrameworkElement));
  public static readonly RoutedCommand MarkAllInstances = new("MarkAllInstances", typeof(FrameworkElement));
  public static readonly RoutedCommand MarkInstance = new("MarkInstance", typeof(FrameworkElement));
  public static readonly RoutedCommand ClearMarkedNodes = new("ClearMarkedNodes", typeof(FrameworkElement));

  // Timeline specific commands.
  public static readonly RoutedCommand RemoveFilters = new("RemoveFilters", typeof(FrameworkElement));
  public static readonly RoutedCommand RemoveThreadFilters = new("RemoveThreadFilters", typeof(FrameworkElement));
  public static readonly RoutedCommand RemoveAllFilters = new("RemoveAllFilters", typeof(FrameworkElement));
}

public class CallTreeListItem : SearchableProfileItem, ITreeModel {
  private string cacheFunctionName_;
  private Brush functionBackColor_;
  private Brush moduleBackColor_;

  public CallTreeListItem(CallTreeListItemKind kind, CallTreePanel owner,
                          FunctionNameFormatter funcNameFormatter = null) :
    base(funcNameFormatter) {
    Children = new List<CallTreeListItem>();
    Kind = kind;
    Owner = owner;
  }

  public CallTreePanel Owner { get; set; }
  public IRTextFunction Function { get; set; }
  public ProfileCallTreeNode CallTreeNode { get; set; }
  public Brush TextColor { get; set; }
  public Brush BackColor { get; set; }
  public Brush BackColor2 { get; set; }

  public Brush FunctionBackColor {
    get => functionBackColor_;
    set => SetAndNotify(ref functionBackColor_, value);
  }

  public Brush ModuleBackColor {
    get => moduleBackColor_;
    set => SetAndNotify(ref moduleBackColor_, value);
  }

  public CallTreeListItem Parent { get; set; }
  public List<CallTreeListItem> Children { get; set; }
  public long Time { get; set; }
  public CallTreeListItemKind Kind { get; set; }
  public bool HasCallTreeNode => CallTreeNode?.Function != null;
  public override TimeSpan Weight => HasCallTreeNode ? CallTreeNode.Weight : TimeSpan.Zero;
  public override TimeSpan ExclusiveWeight => HasCallTreeNode ? CallTreeNode.ExclusiveWeight : TimeSpan.Zero;
  public override string ModuleName =>
    CallTreeNode is {HasFunction: true} ? CallTreeNode.ModuleName : null;
  public bool HasAnyChildren => Children is {Count: > 0};

  public void AddChild(CallTreeListItem child) {
    Children ??= new();
    Children.Add(child);
  }

  public void ClearChildren() {
    Children = null;
  }

  public override string FunctionName {
    get {
      string name = base.FunctionName;

      if (Kind != CallTreeListItemKind.Header) {
        if (cacheFunctionName_ == null) {
          cacheFunctionName_ = $"{name} ({Percentage.AsPercentageString()})";
        }

        return cacheFunctionName_;
      }

      return name;
    }
    set => base.FunctionName = value;
  }

  public TreeNode TreeNode { get; set; } // Associated UI tree node.

  public IEnumerable GetChildren(object node) {
    if (node == null) {
      return Children;
    }

    var parentNode = (CallTreeListItem)node;
    return parentNode.Children;
  }

  public bool HasChildren(object node) {
    if (node == null)
      return false;
    var parentNode = (CallTreeListItem)node;
    return parentNode.Children != null && parentNode.Children.Count > 0;
  }

  protected override string GetFunctionName() {
    return CallTreeNode is {HasFunction: true} ? CallTreeNode.FunctionName : null;
  }

  protected override bool ShouldPrependModule() {
    return Kind != CallTreeListItemKind.Header &&
           Owner.Settings.PrependModuleToFunction;
  }
}

public partial class CallTreePanel : ToolPanelControl, IFunctionProfileInfoProvider, INotifyPropertyChanged {
  public static readonly DependencyProperty ShowToolbarProperty =
    DependencyProperty.Register("ShowToolbar", typeof(bool), typeof(CallTreePanel));
  private IRTextFunction function_;
  private PopupHoverPreview nodeHoverPreview_;
  private ProfileCallTree callTree_;
  private CallTreeListItem callTreeEx_;
  private List<CallTreeListItem> searchResultNodes_;
  private int searchResultIndex_;
  private CallTreeSettings settings_;
  private CancelableTaskInstance searchTask_;
  private Stack<IRTextFunction> stateStack_;
  private Dictionary<ProfileCallTreeNode, CallTreeListItem> callTreeNodeToNodeExMap_;
  private bool ignoreNextSelectionEvent_;
  private bool showSearchSection_;
  private string searchResultText_;
  private OptionsPanelHostPopup optionsPanelPopup_;
  private CancelableTaskInstance loadTask_;
  private double profileDurationReciprocal_;

  public CallTreePanel() {
    InitializeComponent();
    Settings = PanelKind == ToolPanelKind.CallTree ?
      App.Settings.CallTreeSettings :
      App.Settings.CallerCalleeSettings;
    searchTask_ = new CancelableTaskInstance(false);
    callTreeNodeToNodeExMap_ = new Dictionary<ProfileCallTreeNode, CallTreeListItem>();
    stateStack_ = new Stack<IRTextFunction>();
    loadTask_ = new CancelableTaskInstance(false);
    DataContext = this;
    SetupEvents();
  }

  public CallTreeSettings Settings {
    get => settings_;
    set {
      settings_ = value;
      settings_.TreeListColumns.RestoreColumnsState(CallTreeList);
      SetupPreviewPopup();
      OnPropertyChanged();
    }
  }

  public bool HasEnabledMarkedFunctions =>
    MarkingSettings.UseFunctionColors && MarkingSettings.FunctionColors.Count > 0;
  public bool HasEnabledMarkedModules => MarkingSettings.UseModuleColors && MarkingSettings.ModuleColors.Count > 0;
  public FunctionMarkingSettings MarkingSettings => App.Settings.MarkingSettings;
  public event PropertyChangedEventHandler PropertyChanged;

  //? TODO: Replace all other commands with RelayCommand.
  public RelayCommand<object> SelectFunctionCallTreeCommand => new(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.CallTree);
  });
  public RelayCommand<object> SelectFunctionFlameGraphCommand => new(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.FlameGraph);
  });
  public RelayCommand<object> SelectFunctionTimelineCommand => new(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.Timeline);
  });
  public RelayCommand<object> SelectFunctionSourceCommand => new(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.Source);
  });
  public RelayCommand<object> CopyFunctionNameCommand => new(async obj => {
    if (CallTreeList.SelectedItem is TreeNode node && node.Tag is CallTreeListItem item) {
      string text = Session.CompilerInfo.NameProvider.GetFunctionName(item.Function);
      Clipboard.SetText(text);
    }
  });
  public RelayCommand<object> CopyDemangledFunctionNameCommand => new(async obj => {
    if (CallTreeList.SelectedItem is TreeNode node && node.Tag is CallTreeListItem item) {
      var options = FunctionNameDemanglingOptions.Default;
      string text = Session.CompilerInfo.NameProvider.DemangleFunctionName(item.Function, options);
      Clipboard.SetText(text);
    }
  });
  public RelayCommand<object> CopyFunctionDetailsCommand => new(async obj => {
    if (CallTreeList.SelectedItems.Count > 0) {
      var funcList = new List<SearchableProfileItem>();

      foreach (TreeNode node in CallTreeList.SelectedItems) {
        if (node.Tag is CallTreeListItem {HasCallTreeNode: true} item) {
          funcList.Add(item);
        }
      }

      SearchableProfileItem.CopyFunctionListAsHtml(funcList);
    }
  });
  public RelayCommand<object> PreviewFunctionCommand => new(async obj => {
    if (CallTreeList.SelectedItem is TreeNode node && node.Tag is CallTreeListItem item) {
      var brush = GetMarkedNodeColor(item);
      await IRDocumentPopupInstance.ShowPreviewPopup(item.Function, "",
                                                     CallTreeList, Session, null, false, brush);
    }
  });
  public RelayCommand<object> PreviewFunctionInstanceCommand => new(async obj => {
    if (CallTreeList.SelectedItem is TreeNode node && node.Tag is CallTreeListItem item) {
      var filter = new ProfileSampleFilter(item.CallTreeNode);
      var brush = GetMarkedNodeColor(item);
      await IRDocumentPopupInstance.ShowPreviewPopup(item.Function, "",
                                                     CallTreeList, Session, filter, false, brush);
    }
  });

  private Brush GetMarkedNodeColor(CallTreeListItem node) {
    return App.Settings.MarkingSettings.
      GetMarkedNodeBrush(node.FunctionName, node.ModuleName);
  }

  public RelayCommand<object> OpenInstanceCommand => new(async obj => {
    if (CallTreeList.SelectedItem is TreeNode node) {
      var item = node.Tag as CallTreeListItem;
      var mode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent;
      await OpenFunctionInstance(item, mode);
    }
  });
  public RelayCommand<object> OpenInstanceInNewTabCommand => new(async obj => {
    if (CallTreeList.SelectedItem is TreeNode node) {
      var item = node.Tag as CallTreeListItem;
      await OpenFunctionInstance(item, OpenSectionKind.NewTabDockRight);
    }
  });

  public ProfileCallTree CallTree {
    get => callTree_;
    set {
      SetField(ref callTree_, value);

      if (Session?.ProfileData == null) {
        return;
      }

      profileDurationReciprocal_ = 1.0 / Session.ProfileData.ProfileWeight.Ticks;
      OnPropertyChanged(nameof(HasCallTree));
    }
  }

  public bool IsCallerCalleePanel => PanelKind == ToolPanelKind.CallerCallee;
  public bool HasCallTree => callTree_ != null;

  private async Task UpdateCallTree() {
    callTreeEx_ = null;

    if (IsCallerCalleePanel) {
      await DisplayProfileCallerCalleeTree(function_);
    }
    else {
      await DisplayProfileCallTree();
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

  public bool HasPreviousState => stateStack_.Count > 0;

  private static void SortCallTreeNodes(CallTreeListItem node) {
    // Sort children in descending order,
    // since that is not yet supported by the TreeListView control.
    if (!node.HasAnyChildren) {
      return;
    }

    node.Children.Sort((a, b) => {
      int result = b.Time.CompareTo(a.Time);
      return result != 0 ? result : string.Compare(a.FunctionName, a.FunctionName, StringComparison.Ordinal);
    });
  }

  private static void ExpandPathToNode(CallTreeListItem nodeEx, bool markPathNodes) {
    // Expansion must be done starting from the root node,
    // because the TreeNode is created on-demand by the control
    // when a node is expanded, so walk parents recursively.
    if (nodeEx.Parent != null) {
      ExpandPathToNode(nodeEx.Parent, markPathNodes);
    }

    if (nodeEx.TreeNode != null) {
      nodeEx.TreeNode.IsExpanded = true;
      nodeEx.IsMarked = markPathNodes;
    }
  }

  public async Task DisplayProfileCallTree() {
    if (callTreeEx_ != null) {
      Reset();
    }

    CallTree = Session.ProfileData.CallTree;
    callTreeEx_ = await Task.Run(() => CreateProfileCallTree());
    CallTreeList.Model = callTreeEx_;
    await UpdateMarkedFunctions(true);

    if (settings_.ExpandHottestPath) {
      ExpandHottestFunctionPath();
    }
  }

  public async Task DisplayProfileCallerCalleeTree(IRTextFunction function) {
    using var task = await loadTask_.CancelCurrentAndCreateTaskAsync();
    function_ = function;
    CallTree = Session.ProfileData.CallTree;
    ignoreNextSelectionEvent_ = true; // Prevent deselection even to be triggered.

    callTreeEx_ = await Task.Run(() => CreateProfileCallerCalleeTree(function));
    CallTreeList.Model = callTreeEx_;
    await UpdateMarkedFunctions(true);
    ExpandCallTreeTop();
    ignoreNextSelectionEvent_ = false;
  }

  public void Reset() {
    CallTreeList.Model = null;
    function_ = null;
    CallTree = null;
    callTreeEx_ = null;
    callTreeNodeToNodeExMap_.Clear();
    stateStack_.Clear();
    OnPropertyChanged(nameof(HasPreviousState));
  }

  public void SelectFunction(IRTextFunction function) {
    var nodeList = callTree_.GetSortedCallTreeNodes(function);

    if (nodeList != null && nodeList.Count > 0) {
      SelectFunction(nodeList[0], false);
    }
  }

  public void SelectFunction(ProfileCallTreeNode node, bool markPath = true) {
    if (node is ProfileCallTreeGroupNode nodeGroup) {
      foreach (var groupNode in nodeGroup.Nodes) {
        SelectFunction(groupNode, markPath);
        return; //? TODO: Should it select everything instead?
      }
    }

    if (!callTreeNodeToNodeExMap_.TryGetValue(node, out var nodeEx)) {
      return;
    }

    ExpandPathToNode(nodeEx, markPath);
    BringIntoView(nodeEx);

    if (CallTreeList.SelectedItem != nodeEx.TreeNode) {
      ignoreNextSelectionEvent_ = true;
      CallTreeList.SelectedItem = nodeEx.TreeNode;
    }
  }

  public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
    return callTree_.GetBacktrace(node);
  }

  public (List<ProfileCallTreeNode>, List<ModuleProfileInfo> Modules) GetTopFunctionsAndModules(
    ProfileCallTreeNode node) {
    return callTree_.GetTopFunctionsAndModules(node);
  }

  protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
    if (EqualityComparer<T>.Default.Equals(field, value)) return false;
    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  private async Task SelectFunctionInPanel(object target, ToolPanelKind panelKind) {
    if (target is TreeNode node) {
      var item = node.Tag as CallTreeListItem;

      if (item?.CallTreeNode != null) {
        await Session.SelectProfileFunctionInPanel(item.CallTreeNode, panelKind);
      }
    }
  }

  private void SetupEvents() {
    CallTreeList.NodeExpanded += CallTreeOnNodeExpanded;
    PreviewMouseDown += OnPreviewMouseDown;
    SetupPreviewPopup();
  }

  private async void OnPreviewMouseDown(object sender, MouseButtonEventArgs e) {
    if (IsCallerCalleePanel &&
        e.ChangedButton == MouseButton.XButton1) {
      e.Handled = true;
      await RestorePreviousState();
      return;
    }
  }

  private void SetupPreviewPopup() {
    if (nodeHoverPreview_ != null) {
      nodeHoverPreview_.Unregister();
      nodeHoverPreview_ = null;
    }

    if (!settings_.ShowNodePopup) {
      return;
    }

    nodeHoverPreview_ = new PopupHoverPreview(CallTreeList,
                                              TimeSpan.FromMilliseconds(settings_.NodePopupDuration),
                                              (mousePoint, previewPoint) => {
                                                var element =
                                                  (UIElement)CallTreeList.GetObjectAtPoint<ListViewItem>(
                                                    mousePoint);

                                                if (element is not TreeListItem treeItem) {
                                                  return null;
                                                }

                                                var funcNode = treeItem.Node?.Tag as CallTreeListItem;
                                                var callNode = funcNode?.CallTreeNode;

                                                if (callNode is {HasFunction: true}) {
                                                  // If popup already opened for this node reuse the instance.
                                                  if (nodeHoverPreview_.PreviewPopup is CallTreeNodePopup
                                                    popup) {
                                                    popup.UpdatePosition(previewPoint, CallTreeList);
                                                    popup.UpdateNode(callNode);
                                                    return popup;
                                                  }

                                                  return new CallTreeNodePopup(
                                                    callNode, this, previewPoint, CallTreeList, Session);
                                                }

                                                return null;
                                              },
                                              (mousePoint, popup) => true,
                                              popup => {
                                                if (popup.IsDetached) {
                                                  Session.RegisterDetachedPanel(popup);
                                                }
                                              });
  }

  private void CallTreeOnNodeExpanded(object sender, TreeNode node) {
    if (node.Tag is not CallTreeListItem funcNode) {
      return;
    }

    // If children not populated yet, there is a single dummy node.
    if (funcNode.HasAnyChildren &&
        funcNode.Children.Count == 1 &&
        funcNode.Children[0].Kind == CallTreeListItemKind.ChildrenPlaceholder) {
      var callNode = funcNode.CallTreeNode;
      var visitedNodes = new HashSet<ProfileCallTreeNode>();

      // Remove the dummy node and add the real children.
      // If the children have children on their own, new dummy nodes will be used.
      funcNode.ClearChildren();
      CallTreeListItem firstNodeEx = null;

      if (funcNode.Kind == CallTreeListItemKind.CalleeNode && callNode.HasChildren) {
        foreach (var childNode in callNode.Children) {
          firstNodeEx ??= CreateProfileCallTree(childNode, funcNode, funcNode.Kind, visitedNodes);
        }
      }
      else if (funcNode.Kind == CallTreeListItemKind.CallerNode && callNode.HasCallers) {
        foreach (var childNode in callNode.Callers) {
          firstNodeEx = CreateProfileCallTree(childNode, funcNode, funcNode.Kind, visitedNodes);
        }
      }

      if (firstNodeEx != null) {
        BringIntoView(firstNodeEx);
      }
    }
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private Func<TimeSpan, double> PickPercentageFunction(TimeSpan totalWeight) {
    return weight => weight.Ticks / (double)totalWeight.Ticks;
  }

  private void BringIntoView(CallTreeListItem nodeEx) {
    if (nodeEx.TreeNode != null) {
      CallTreeList.ScrollIntoView(nodeEx.TreeNode);
    }
  }

  private List<ProfileCallTreeNode> GetCallTreeNodes(IRTextFunction function, ProfileCallTree callTree) {
    if (settings_.CombineInstances) {
      var combinedNode = callTree.GetCombinedCallTreeNode(function);
      return combinedNode == null ? null : new List<ProfileCallTreeNode> {combinedNode};
    }

    return callTree.GetCallTreeNodes(function);
  }

  private ProfileCallTreeNode GetChildCallTreeNode(ProfileCallTreeNode childNode, ProfileCallTreeNode parentNode,
                                                   ProfileCallTree callTree) {
    if (settings_.CombineInstances) {
      return callTree.GetCombinedCallTreeNode(childNode.Function, parentNode);
    }

    return childNode;
  }

  private CallTreeListItem CreateProfileCallerCalleeTree(IRTextFunction function) {
    var visitedNodes = new HashSet<ProfileCallTreeNode>();
    var rootNode = new CallTreeListItem(CallTreeListItemKind.Root, this);
    rootNode.Children = new List<CallTreeListItem>();
    var nodeList = GetCallTreeNodes(function, callTree_);

    if (nodeList == null) {
      return null;
    }

    int index = 1;
    var combinedWeight = callTree_.GetCombinedCallTreeNodeWeight(function);

    foreach (var instance in nodeList) {
      bool isSelf = nodeList.Count == 1;
      string name = isSelf ? "Function" : $"Function instance {index++}";
      string funcName = Session.CompilerInfo.NameProvider.FormatFunctionName(instance.Function);

      if (isSelf && !string.IsNullOrEmpty(funcName)) {
        name = funcName;
      }

      var percentageFunc = PickPercentageFunction(combinedWeight);
      var instanceNode = CreateProfileCallTreeInstance(name, instance, percentageFunc);

      if (isSelf) {
        instanceNode.Time = long.MaxValue; // Ensure Self is on top.
      }

      rootNode.AddChild(instanceNode);

      if (instance.HasCallers) {
        // Percentage relative to entire profile for callers.
        var callersNode = CreateProfileCallTreeHeader(CallTreeListItemKind.Header, "Callers", 2);

        if (nodeList.Count > 1) {
          instanceNode.AddChild(callersNode);
        }
        else {
          rootNode.AddChild(callersNode);
        }

        foreach (var callerNode in instance.Callers) {
          CreateProfileCallTree(callerNode, callersNode, instanceNode,
                                CallTreeListItemKind.CallerNode, visitedNodes);
        }

        visitedNodes.Clear();
      }

      if (instance.HasChildren) {
        // Percentage relative to current function callers.
        //percentageFunc = PickPercentageFunction(instance.Weight);
        var (childrenWeight, childrentExcWeight) = instance.ChildrenWeight;
        var childrenNode =
          CreateProfileCallTreeHeader("Calling", childrenWeight, childrentExcWeight, percentageFunc, 1);

        if (nodeList.Count > 1) {
          instanceNode.AddChild(childrenNode);
        }
        else {
          rootNode.AddChild(childrenNode);
        }

        foreach (var childNode in instance.Children) {
          CreateProfileCallTree(childNode, childrenNode, instanceNode,
                                CallTreeListItemKind.CalleeNode, visitedNodes, percentageFunc);
        }

        visitedNodes.Clear();
      }

      SortCallTreeNodes(instanceNode);
    }

    SortCallTreeNodes(rootNode);
    return rootNode;
  }

  private CallTreeListItem CreateProfileCallTree() {
    var visitedNodes = new HashSet<ProfileCallTreeNode>();
    var rootNode = new CallTreeListItem(CallTreeListItemKind.Root, this);
    rootNode.Children = new List<CallTreeListItem>();

    foreach (var node in callTree_.RootNodes) {
      visitedNodes.Clear();
      CreateProfileCallTree(node, rootNode, CallTreeListItemKind.CallTreeNode, visitedNodes);
    }

    return rootNode;
  }

  private CallTreeListItem CreateProfileCallTree(ProfileCallTreeNode node, CallTreeListItem parentNodeEx,
                                                 CallTreeListItemKind kind,
                                                 HashSet<ProfileCallTreeNode> visitedNodes,
                                                 Func<TimeSpan, double> percentageFunc = null) {
    return CreateProfileCallTree(node, parentNodeEx, parentNodeEx, kind, visitedNodes, percentageFunc);
  }

  private CallTreeListItem CreateProfileCallTree(ProfileCallTreeNode node, CallTreeListItem parentNodeEx,
                                                 CallTreeListItem actualParentNode, CallTreeListItemKind kind,
                                                 HashSet<ProfileCallTreeNode> visitedNodes,
                                                 Func<TimeSpan, double> percentageFunc = null) {
    bool newFunc = visitedNodes.Add(node);
    var nodeEx = CreateProfileCallTreeChild(node, kind, percentageFunc, parentNodeEx);
    parentNodeEx.AddChild(nodeEx);

    if (!newFunc) {
      return nodeEx; // Recursion in the call graph.
    }

    if (kind == CallTreeListItemKind.CalleeNode) {
      //? TODO: This is still not quite right, the selected nodes
      //? shoud be found on a path that has the current stack frame as a prefix in theirs.
      //? actualParentNode is just the last in that list
      node = GetChildCallTreeNode(node, actualParentNode.CallTreeNode, callTree_);
      nodeEx.CallTreeNode = node;
    }
    else if (kind == CallTreeListItemKind.CallerNode) {
      node = GetChildCallTreeNode(node, null, callTree_);
      nodeEx.CallTreeNode = node;
    }

    switch (kind) {
      case CallTreeListItemKind.CallTreeNode when node.HasChildren: {
        foreach (var childNode in node.Children) {
          CreateProfileCallTree(childNode, nodeEx, nodeEx, kind, visitedNodes, percentageFunc);
        }

        break;
      }
      case CallTreeListItemKind.CalleeNode when node.HasChildren: {
        // For caller-callee mode, use a placeholder than when the tree gets expanded,
        // gets replaced by the real callee nodes.
        var dummyChildNode = CreateProfileCallTreeHeader(CallTreeListItemKind.ChildrenPlaceholder, "Placeholder", 0);
        dummyChildNode.CallTreeNode = node;
        nodeEx.AddChild(dummyChildNode);
        break;
      }
      case CallTreeListItemKind.CallerNode when node.HasCallers: {
        // For caller-callee mode, use a placeholder than when tree gets expanded,
        // gets replaced by the real caller (backtrace) nodes.
        var dummyChildNode = CreateProfileCallTreeHeader(CallTreeListItemKind.ChildrenPlaceholder, "Placeholder", 0);
        dummyChildNode.CallTreeNode = node;
        nodeEx.AddChild(dummyChildNode);
        break;
      }
    }

    SortCallTreeNodes(parentNodeEx);
    visitedNodes.Remove(node);
    return nodeEx;
  }

  private void ExpandCallTreeTop() {
    if (CallTreeList.Nodes.Count > 0) {
      foreach (var childNode in CallTreeList.Nodes) {
        childNode.IsExpanded = true;
      }
    }
  }

  private double ComputeNodePercentage(TimeSpan weight, Func<TimeSpan, double> percentageFunc) {
    if (percentageFunc != null) {
      return percentageFunc(weight);
    }

    return weight.Ticks * profileDurationReciprocal_;
  }

  private CallTreeListItem CreateProfileCallTreeChild(ProfileCallTreeNode node, CallTreeListItemKind kind,
                                                      Func<TimeSpan, double> percentageFunc,
                                                      CallTreeListItem parentNodeEx = null) {
    double weightPercentage = ComputeNodePercentage(node.Weight, percentageFunc);
    double exclusiveWeightPercentage = ComputeNodePercentage(node.ExclusiveWeight, percentageFunc);

    var result = new CallTreeListItem(kind, this, Session.CompilerInfo.NameProvider.FormatFunctionName) {
      Function = node.Function,
      ModuleName = node.ModuleName,
      Time = node.Weight.Ticks,
      CallTreeNode = node,
      Parent = parentNodeEx,
      Percentage = weightPercentage,
      ExclusivePercentage = exclusiveWeightPercentage,
      TextColor = Brushes.Black,
      BackColor = App.Settings.DocumentSettings.ProfileMarkerSettings.PickBrushForPercentage(weightPercentage),
      BackColor2 = App.Settings.DocumentSettings.ProfileMarkerSettings.PickBrushForPercentage(exclusiveWeightPercentage)
    };

    callTreeNodeToNodeExMap_[node] = result;
    return result;
  }

  private CallTreeListItem CreateProfileCallTreeHeader(string name, TimeSpan weight, TimeSpan exclusiveWeight,
                                                       Func<TimeSpan, double> percentageFunc, int priority) {
    double weightPercentage = ComputeNodePercentage(weight, percentageFunc);
    double exclusiveWeightPercentage = ComputeNodePercentage(exclusiveWeight, percentageFunc);
    return new CallTreeListItem(CallTreeListItemKind.Header, this) {
      CallTreeNode = new ProfileCallTreeNode(null, null) {Weight = weight, ExclusiveWeight = exclusiveWeight},
      Time = TimeSpan.MaxValue.Ticks - priority,
      FunctionName = name,
      Percentage = weightPercentage,
      ExclusivePercentage = exclusiveWeightPercentage,
      TextColor = Brushes.Black,
      BackColor = App.Settings.DocumentSettings.ProfileMarkerSettings.PickBrushForPercentage(weightPercentage),
      BackColor2 =
        App.Settings.DocumentSettings.ProfileMarkerSettings.PickBrushForPercentage(exclusiveWeightPercentage),
      IsMarked = true
    };
  }

  private CallTreeListItem CreateProfileCallTreeHeader(CallTreeListItemKind kind, string name, int priority) {
    return new CallTreeListItem(kind, this)
      {Time = TimeSpan.MaxValue.Ticks - priority, FunctionName = name, TextColor = Brushes.Black, IsMarked = true};
  }

  private CallTreeListItem CreateProfileCallTreeInstance(string name, ProfileCallTreeNode node,
                                                         Func<TimeSpan, double> percentageFunc) {
    var result = CreateProfileCallTreeChild(node, CallTreeListItemKind.Header, percentageFunc);
    result.FunctionName = name;
    result.IsMarked = true;
    return result;
  }

  private async void ChildDoubleClick(object sender, MouseButtonEventArgs e) {
    var item = ((ListViewItem)sender).Content as CallTreeListItem;

    if (item != null) {
      if (Utils.IsControlModifierActive()) {
        var openMode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent;
        await OpenFunction(item, openMode);
      }
      else {
        await SwitchFunction(item);

        if (IsCallerCalleePanel) {
          await DisplayProfileCallerCalleeTree(item.Function);
        }
      }
    }
  }

  private void ExpandHottestFunctionPath() {
    if (CallTreeList.Nodes.Count > 0) {
      ExpandHottestFunctionPath(CallTreeList.Nodes[0]);
    }
  }

  private void UnmarkAllFunctions() {
    foreach (var funcEx in callTreeNodeToNodeExMap_.Values) {
      funcEx.IsMarked = false;
    }
  }

  private void ExpandHottestFunctionPath(TreeNode node) {
    UnmarkAllFunctions();
    ExpandHottestFunctionPathImpl(node);
  }

  private void ExpandHottestFunctionPathImpl(TreeNode node, int depth = 0) {
    var item = node.Tag as CallTreeListItem;
    item.IsMarked = true;

    if (node.HasChildren && depth <= 10) {
      node.IsExpanded = true;
      ExpandHottestFunctionPathImpl(node.Nodes[0], depth + 1);
    }
  }

  private void CollapseAllFunctionPaths() {
    foreach (var node in CallTreeList.Nodes) {
      CollapseFunctionPath(node);
    }
  }

  private void CollapseFunctionPath(TreeNode node) {
    foreach (var child in node.Nodes) {
      CollapseFunctionPath(child);
    }

    node.IsExpanded = false;
  }

  private void ExpandHottestCallPathExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (CallTreeList.SelectedItem is TreeNode node) {
      // Expand hotteest path starting with the node.
      ExpandHottestFunctionPath(node);
    }
    else {
      // Expand hotteest path in the tree.
      ExpandHottestFunctionPath();
    }
  }

  private void CollapseCallPathExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (CallTreeList.SelectedItem is TreeNode node) {
      CollapseFunctionPath(node);
    }
  }

  private async void SelectFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (CallTreeList.SelectedItem is TreeNode node) {
      var item = node.Tag as CallTreeListItem;
      await SwitchFunction(item);
    }
  }

  private async void OpenFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (CallTreeList.SelectedItem is TreeNode node) {
      var item = node.Tag as CallTreeListItem;
      var mode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent;
      await OpenFunction(item, mode);
    }
  }

  private async void OpenFunctionInNewTab(object sender, ExecutedRoutedEventArgs e) {
    if (CallTreeList.SelectedItem is TreeNode node) {
      var item = node.Tag as CallTreeListItem;
      await OpenFunction(item, OpenSectionKind.NewTabDockRight);
    }
  }

  private async void GoBackExecuted(object sender, ExecutedRoutedEventArgs e) {
    await RestorePreviousState();
  }

  private async Task OpenFunction(CallTreeListItem item, OpenSectionKind openMode) {
    if (item.HasCallTreeNode) {
      await Session.OpenProfileFunction(item.CallTreeNode, openMode);
    }
  }

  private async Task OpenFunctionInstance(CallTreeListItem item, OpenSectionKind openMode) {
    if (item.HasCallTreeNode) {
      var filter = new ProfileSampleFilter(item.CallTreeNode);
      await Session.OpenProfileFunction(item.CallTreeNode, openMode, filter);
    }
  }

  private async Task SwitchFunction(CallTreeListItem item) {
    if (item.HasCallTreeNode) {
      if (function_ != null) {
        stateStack_.Push(function_);
        OnPropertyChanged(nameof(HasPreviousState));
      }

      await Session.SwitchActiveProfileFunction(item.CallTreeNode);
    }
  }

  private async void FunctionFilter_TextChanged(object sender, TextChangedEventArgs e) {
    string text = FunctionFilter.Text.Trim();
    await SearchCallTree(text);
  }

  private async Task SearchCallTree(string text) {
    using var cancelableTask = await searchTask_.CancelCurrentAndCreateTaskAsync();

    if (searchResultNodes_ != null) {
      // Clear previous search results.
      foreach (var node in searchResultNodes_) {
        node.SearchResult = null;
        node.ResetCachedName();
      }
    }

    if (text.Length > 1) {
      searchResultNodes_ = new List<CallTreeListItem>();
      var searcher = new TextSearcher(text, !App.Settings.SectionSettings.FunctionSearchCaseSensitive);
      await Task.Run(() => SearchCallTree(text, callTreeEx_, searcher, searchResultNodes_));

      if (cancelableTask.IsCanceled) {
        return;
      }

      foreach (var node in searchResultNodes_) {
        node.ResetCachedName();
      }

      searchResultIndex_ = -1;
      SelectNextSearchResult();
      ShowSearchSection = true;
    }
    else {
      searchResultNodes_ = null;
      ShowSearchSection = false;
    }
  }

  private void UpdateSearchResultText() {
    SearchResultText = searchResultNodes_ is {Count: > 0} ? $"{searchResultIndex_ + 1} / {searchResultNodes_.Count}"
      : "Not found";
  }

  private void SearchCallTree(string text, CallTreeListItem node, TextSearcher searcher,
                              List<CallTreeListItem> matchingNodes) {
    var result = searcher.FirstIndexOf(node.FunctionName);

    if (result.HasValue) {
      node.SearchResult = result;
      matchingNodes.Add(node);
    }

    foreach (var child in node.Children) {
      SearchCallTree(text, child, searcher, matchingNodes);
    }
  }

  private void FocusSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
    FunctionFilter.Focus();
    FunctionFilter.SelectAll();
  }

  private void ClearSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
    ((TextBox)e.Parameter).Text = string.Empty;
  }

  private async void CallTree_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (ignoreNextSelectionEvent_) {
      ignoreNextSelectionEvent_ = false;
      return;
    }

    if (CallTreeList.SelectedItem is TreeNode node &&
        node.Tag is CallTreeListItem funcEx &&
        funcEx.HasCallTreeNode) {
      if (settings_.SyncSourceFile) {
        // Load the source file and scroll to the hottest line.
        await Session.OpenProfileSourceFile(funcEx.CallTreeNode);
      }

      if (settings_.SyncSelection) {
        await Session.ProfileFunctionSelected(funcEx.CallTreeNode, PanelKind);
      }
    }
    else if (CallTreeList.SelectedItem == null && settings_.SyncSelection) {
      await Session.ProfileFunctionDeselected();
    }
  }

  private async Task RestorePreviousState() {
    if (stateStack_.TryPop(out var prevFunc)) {
      await Session.SwitchActiveFunction(prevFunc);
      OnPropertyChanged(nameof(HasPreviousState));
    }
  }

  private void PreviousSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
    SelectPreviousSearchResult();
  }

  private void NextSearchResultExecuted(object sender, ExecutedRoutedEventArgs e) {
    SelectNextSearchResult();
  }

  private void SelectPreviousSearchResult() {
    if (searchResultNodes_ != null && searchResultIndex_ > 0) {
      searchResultIndex_--;
      UpdateSearchResultText();
      SelectFunction(searchResultNodes_[searchResultIndex_].CallTreeNode);
    }
  }

  private void SelectNextSearchResult() {
    if (searchResultNodes_ != null && searchResultIndex_ < searchResultNodes_.Count - 1) {
      searchResultIndex_++;
      UpdateSearchResultText();
      SelectFunction(searchResultNodes_[searchResultIndex_].CallTreeNode);
    }
  }

  private void CollapseNodesExecuted(object sender, ExecutedRoutedEventArgs e) {
    CollapseAllFunctionPaths();
    ExpandHottestFunctionPath();
  }

  private async void PanelToolbarTray_OnHelpClicked(object sender, EventArgs e) {
    await HelpPanel.DisplayPanelHelp(PanelKind, Session);
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
    FrameworkElement relativeControl = CallTreeList;
    optionsPanelPopup_ = OptionsPanelHostPopup.Create<CallTreeOptionsPanel, CallTreeSettings>(
      settings_.Clone(), relativeControl, Session,
      async (newSettings, commit) => {
        if (!newSettings.Equals(settings_) ||
            !initialMarkingSettings.Equals(MarkingSettings)) {
          Settings = newSettings;

          if (IsCallerCalleePanel) {
            App.Settings.CallerCalleeSettings = newSettings;
          }
          else {
            App.Settings.CallTreeSettings = newSettings;
          }

          await UpdateCallTree();
          UpdateMarkedFunctions();

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

  public override async Task OnReloadSettings() {
    Settings = PanelKind == ToolPanelKind.CallTree ?
      App.Settings.CallTreeSettings :
      App.Settings.CallerCalleeSettings;
  }

  #region IToolPanel

  public override ToolPanelKind PanelKind => ToolPanelKind.CallTree;
  public override bool SavesStateToFile => false;

  public override void OnSessionEnd() {
    base.OnSessionEnd();
    Settings.TreeListColumns.SaveColumnsState(CallTreeList.View as GridView);
    Reset();
  }

    #endregion

  private void PanelToolbarTray_OnSettingsClicked(object sender, EventArgs e) {
    ShowOptionsPanel();
  }

  private async void ToggleButton_Click(object sender, RoutedEventArgs e) {
    await UpdateCallTree();
    await UpdateMarkedFunctions();
  }

  private async void ClearModulesButton_Click(object sender, RoutedEventArgs e) {
    MarkingSettings.ModuleColors.Clear();
    await UpdateMarkedFunctions();
  }

  private async void ClearFunctionsButton_Click(object sender, RoutedEventArgs e) {
    MarkingSettings.FunctionColors.Clear();
    await UpdateMarkedFunctions();
  }

  private async void ModuleMenu_OnSubmenuOpened(object sender, RoutedEventArgs e) {
    await ProfilingUtils.PopulateMarkedModulesMenu(ModuleMenu, MarkingSettings, Session,
                                                   e.OriginalSource, () => UpdateMarkedFunctions());
  }

  private async void FunctionMenu_OnSubmenuOpened(object sender, RoutedEventArgs e) {
    await ProfilingUtils.PopulateMarkedFunctionsMenu(FunctionMenu, MarkingSettings, Session,
                                                     e.OriginalSource, () => UpdateMarkedFunctions());
  }

  public RelayCommand<object> MarkModuleCommand => new(async obj => {
    var markingSettings = App.Settings.MarkingSettings;
    MarkSelectedNodes(obj, (node, color) =>
                        markingSettings.AddModuleColor(node.ModuleName, color));
    markingSettings.UseModuleColors = true;
    await UpdateMarkedFunctions();
  });
  public RelayCommand<object> MarkFunctionCommand => new(async obj => {
    var markingSettings = App.Settings.MarkingSettings;
    MarkSelectedNodes(obj, (node, color) => {
      markingSettings.AddFunctionColor(node.FunctionName, color);
    });
    markingSettings.UseFunctionColors = true;
    await UpdateMarkedFunctions();
  });

  private void MarkSelectedNodes(object obj, Action<CallTreeListItem, Color> action) {
    if (obj is SelectedColorEventArgs e) {
      foreach (TreeNode node in CallTreeList.SelectedItems) {
        if (node.Tag is CallTreeListItem item) {
          action(item, e.SelectedColor);
        }
      }
    }
  }

  public async Task UpdateMarkedFunctions(bool externalCall = false) {
    using var task = await loadTask_.WaitAndCreateTaskAsync();
    
    if (callTreeNodeToNodeExMap_ != null) {
      UpdateMarkedFunctionsImpl();
      OnPropertyChanged(nameof(HasEnabledMarkedModules));
      OnPropertyChanged(nameof(HasEnabledMarkedFunctions));

      if (!externalCall) {
        await Session.FunctionMarkingChanged(PanelKind);
      }
    }
  }

  private void UpdateMarkedFunctionsImpl() {
    var fgSettings = App.Settings.MarkingSettings;

    foreach (var f in callTreeNodeToNodeExMap_.Values) {
      f.ModuleBackColor = null;
      f.FunctionBackColor = null;
    }

    if (!fgSettings.UseAutoModuleColors &&
        !fgSettings.UseModuleColors &&
        !fgSettings.UseFunctionColors) {
      return;
    }

    foreach (var item in callTreeNodeToNodeExMap_.Values) {
      if (fgSettings.UseModuleColors &&
          fgSettings.GetModuleBrush(item.ModuleName, out var brush)) {
        item.ModuleBackColor = brush;
      }
      else if (fgSettings.UseAutoModuleColors) {
        item.ModuleBackColor = fgSettings.GetAutoModuleBrush(item.ModuleName);
      }

      if (fgSettings.UseFunctionColors &&
          fgSettings.GetFunctionColor(item.FunctionName, out var color)) {
        item.FunctionBackColor = color.AsBrush();
      }
    }
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