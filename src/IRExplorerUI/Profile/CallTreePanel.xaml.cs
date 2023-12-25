// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aga.Controls.Tree;
using IRExplorerCore;
using IRExplorerUI.Panels;
using IRExplorerUI.Utilities;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace IRExplorerUI.Profile;

public enum ChildFunctionExKind {
  Root,
  ChildrenPlaceholder,
  CallerNode,
  CalleeNode,
  CallTreeNode,
  Header
}

//? TODO: Replace all with RelayCommand pattern.
public static class CallTreeCommand {
  public static readonly RoutedCommand ExpandHottestCallPath =
    new RoutedCommand("ExpandHottestCallPath", typeof(FrameworkElement));
  public static readonly RoutedCommand CollapseCallPath =
    new RoutedCommand("CollapseCallPath", typeof(FrameworkElement));
  public static readonly RoutedCommand SelectFunction =
    new RoutedCommand("SelectFunction", typeof(FrameworkElement));
  public static readonly RoutedCommand OpenFunction =
    new RoutedCommand("OpenFunction", typeof(FrameworkElement));
  public static readonly RoutedCommand OpenFunctionInNewTab =
    new RoutedCommand("OpenFunctionInNewTab", typeof(FrameworkElement));
  public static readonly RoutedCommand FocusSearch =
    new RoutedCommand("FocusSearch", typeof(FrameworkElement));
  public static readonly RoutedCommand ClearSearch =
    new RoutedCommand("ClearSearch", typeof(FrameworkElement));
  public static readonly RoutedCommand PreviousSearchResult =
    new RoutedCommand("PreviousSearchResult", typeof(FrameworkElement));
  public static readonly RoutedCommand NextSearchResult =
    new RoutedCommand("NextSearchResult", typeof(FrameworkElement));
  public static readonly RoutedCommand GoBack =
    new RoutedCommand("GoBack", typeof(FrameworkElement));
  public static readonly RoutedCommand CollapseNodes =
    new RoutedCommand("CollapseNodes", typeof(FrameworkElement));

  // FlameGraph specific commands.
  public static readonly RoutedCommand EnlargeNode =
    new RoutedCommand("EnlargeNode", typeof(FrameworkElement));
  public static readonly RoutedCommand ChangeRootNode =
    new RoutedCommand("ChangeRootNode", typeof(FrameworkElement));
  public static readonly RoutedCommand MarkAllInstances =
    new RoutedCommand("MarkAllInstances", typeof(FrameworkElement));
  public static readonly RoutedCommand MarkInstance =
    new RoutedCommand("MarkInstance", typeof(FrameworkElement));
  public static readonly RoutedCommand ClearMarkedNodes =
    new RoutedCommand("ClearMarkedNodes", typeof(FrameworkElement));

  // Timeline specific commands.
  public static readonly RoutedCommand RemoveFilters =
    new RoutedCommand("RemoveFilters", typeof(FrameworkElement));
  public static readonly RoutedCommand RemoveThreadFilters =
    new RoutedCommand("RemoveThreadFilters", typeof(FrameworkElement));
}

public class ChildFunctionEx : SearchableProfileItem, ITreeModel {
  public ChildFunctionEx(ChildFunctionExKind kind, CallTreePanel owner, FunctionNameFormatter funcNameFormatter = null) :
    base(funcNameFormatter) {
    Children = new List<ChildFunctionEx>();
    Kind = kind;
    Owner = owner;
  }

  public CallTreePanel Owner { get; set; }
  public IRTextFunction Function { get; set; }
  public ProfileCallTreeNode CallTreeNode { get; set; }
  public Brush TextColor { get; set; }
  public Brush BackColor { get; set; }
  public Brush BackColor2 { get; set; }
  public ChildFunctionEx Parent { get; set; }
  public List<ChildFunctionEx> Children { get; set; }
  public long Time { get; set; }
  public ChildFunctionExKind Kind { get; set; }
  public bool HasCallTreeNode => CallTreeNode?.Function != null;
  public override TimeSpan Weight => HasCallTreeNode ? CallTreeNode.Weight : TimeSpan.Zero;
  public override TimeSpan ExclusiveWeight => HasCallTreeNode ? CallTreeNode.ExclusiveWeight : TimeSpan.Zero;
  public override string ModuleName =>
    CallTreeNode != null && CallTreeNode.Function != null ? CallTreeNode.ModuleName : null;
  public TreeNode TreeNode { get; set; } // Associated UI tree node.

  public IEnumerable GetChildren(object node) {
    if (node == null) {
      return Children;
    }

    var parentNode = (ChildFunctionEx)node;
    return parentNode.Children;
  }

  public bool HasChildren(object node) {
    if (node == null)
      return false;
    var parentNode = (ChildFunctionEx)node;
    return parentNode.Children != null && parentNode.Children.Count > 0;
  }

  protected override string GetFunctionName() {
    return CallTreeNode != null && CallTreeNode.Function != null ? CallTreeNode.FunctionName : null;
  }

  protected override bool ShouldPrependModule() {
    return Kind != ChildFunctionExKind.Header &&
           Owner.Settings.PrependModuleToFunction;
  }
}

public partial class CallTreePanel : ToolPanelControl, IFunctionProfileInfoProvider, INotifyPropertyChanged {
  public static readonly DependencyProperty ShowToolbarProperty =
    DependencyProperty.Register("ShowToolbar", typeof(bool), typeof(CallTreePanel));
  private IRTextFunction function_;
  private PopupHoverPreview stackHoverPreview_;
  private ProfileCallTree callTree_;
  private ChildFunctionEx callTreeEx_;
  private List<ChildFunctionEx> searchResultNodes_;
  private int searchResultIndex_;
  private CallTreeSettings settings_;
  private CancelableTaskInstance searchTask_;
  private Stack<IRTextFunction> stateStack_;
  private Dictionary<ProfileCallTreeNode, ChildFunctionEx> callTreeNodeToNodeExMap_;
  private bool ignoreNextSelectionEvent_;
  private bool showSearchSection_;
  private string searchResultText_;

  public CallTreePanel() {
    InitializeComponent();
    settings_ = PanelKind == ToolPanelKind.CallTree ?
                App.Settings.CallTreeSettings :
                App.Settings.CallerCalleeSettings;
    searchTask_ = new CancelableTaskInstance(false);
    callTreeNodeToNodeExMap_ = new Dictionary<ProfileCallTreeNode, ChildFunctionEx>();
    stateStack_ = new Stack<IRTextFunction>();
    DataContext = this;
    SetupEvents();
  }

  public CallTreePanel(ISession session) : this() {
    Session = session;
  }

  public CallTreeSettings Settings => settings_;

  public event PropertyChangedEventHandler PropertyChanged;

  //? TODO: Replace all other commands with RelayCommand.
  public RelayCommand<object> SelectFunctionCallTreeCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.CallTree);
  });
  public RelayCommand<object> SelectFunctionFlameGraphCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.FlameGraph);
  });
  public RelayCommand<object> SelectFunctionTimelineCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.Timeline);
  });
  public RelayCommand<object> SelectFunctionSourceCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.Source);
  });
  public RelayCommand<object> CopyFunctionNameCommand => new RelayCommand<object>(async obj => {
    if (obj is TreeNode node && node.Tag is ChildFunctionEx childInfo) {
      string text = Session.CompilerInfo.NameProvider.GetFunctionName(childInfo.Function);
      Clipboard.SetText(text);
    }
  });
  public RelayCommand<object> CopyDemangledFunctionNameCommand => new RelayCommand<object>(async obj => {
    if (obj is TreeNode node && node.Tag is ChildFunctionEx childInfo) {
      var options = FunctionNameDemanglingOptions.Default;
      string text = Session.CompilerInfo.NameProvider.DemangleFunctionName(childInfo.Function, options);
      Clipboard.SetText(text);
    }
  });

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
        OnPropertyChanged();
        callTreeEx_ = null;

        if (IsCallerCalleePanel) {
          DisplayProfileCallerCalleeTree(function_);
        }
        else {
          DisplayProfileCallTree();
        }
      }
    }
  }

  public bool SyncSelection {
    get => settings_.SyncSelection;
    set {
      //? TODO: Use SetField everywhere
      if (value != settings_.SyncSelection) {
        settings_.SyncSelection = value;
        OnPropertyChanged();
      }
    }
  }

  public bool SyncSourceFile {
    get => settings_.SyncSourceFile;
    set {
      if (value != settings_.SyncSourceFile) {
        settings_.SyncSourceFile = value;
        OnPropertyChanged();
      }
    }
  }

  public bool IsCallerCalleePanel => PanelKind == ToolPanelKind.CallerCallee;

  public bool CombineNodes {
    get => settings_.CombineInstances;
    set {
      if (settings_.CombineInstances != value) {
        settings_.CombineInstances = value;
        OnPropertyChanged();

        if (function_ != null) {
          DisplayProfileCallerCalleeTree(function_);
        }
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

  private static void SortCallTreeNodes(ChildFunctionEx node) {
    // Sort children in descending order,
    // since that is not yet supported by the TreeListView control.
    node.Children.Sort((a, b) => {
      int result = b.Time.CompareTo(a.Time);
      return result != 0 ? result : string.Compare(a.FunctionName, a.FunctionName, StringComparison.Ordinal);
    });
  }

  private static void ExpandPathToNode(ChildFunctionEx nodeEx, bool markPathNodes) {
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

    if (true) { //? TODO: Use option from UI settings
      ExpandHottestFunctionPath();
    }
  }

  public async Task DisplayProfileCallerCalleeTree(IRTextFunction function) {
    function_ = function;
    CallTree = Session.ProfileData.CallTree;
    ignoreNextSelectionEvent_ = true; // Prevent deselection even to be triggered.

    callTreeEx_ = await Task.Run(() => CreateProfileCallerCalleeTree(function));
    CallTreeList.Model = callTreeEx_;
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

  public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node) {
    return callTree_.GetTopFunctions(node);
  }

  public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
    return callTree_.GetTopModules(node);
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
      var childInfo = node.Tag as ChildFunctionEx;

      if (childInfo?.CallTreeNode != null) {
        await Session.SelectProfileFunctionInPanel(childInfo.CallTreeNode, panelKind);
      }
    }
  }

  private void SetupEvents() {
    CallTreeList.NodeExpanded += CallTreeOnNodeExpanded;

    stackHoverPreview_ = new PopupHoverPreview(CallTreeList,
                                               HoverPreview.LongHoverDuration,
                                               (mousePoint, previewPoint) => {
                                                 var element =
                                                   (UIElement)CallTreeList.GetObjectAtPoint<ListViewItem>(
                                                     mousePoint);

                                                 if (element is not TreeListItem treeItem) {
                                                   return null;
                                                 }

                                                 var funcNode = treeItem.Node?.Tag as ChildFunctionEx;
                                                 var callNode = funcNode?.CallTreeNode;

                                                 if (callNode != null && callNode.Function != null) {
                                                   // If popup already opened for this node reuse the instance.
                                                   if (stackHoverPreview_.PreviewPopup is CallTreeNodePopup
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
    if (node.Tag is not ChildFunctionEx funcNode) {
      return;
    }

    // If children not populated yet, there is a single dummy node.
    if (funcNode.Children.Count == 1 &&
        funcNode.Children[0].Kind == ChildFunctionExKind.ChildrenPlaceholder) {
      var callNode = funcNode.CallTreeNode;
      var visitedNodes = new HashSet<ProfileCallTreeNode>();

      // Remove the dummy node and add the real children.
      // If the children have children on their own, new dummy nodes will be used.
      funcNode.Children.Clear();
      ChildFunctionEx firstNodeEx = null;

      if (funcNode.Kind == ChildFunctionExKind.CalleeNode && callNode.HasChildren) {
        var percentageFunc = PickPercentageFunction(Session.ProfileData.ProfileWeight);

        foreach (var childNode in callNode.Children) {
          firstNodeEx ??= CreateProfileCallTree(childNode, funcNode, funcNode.Kind,
                                                visitedNodes, percentageFunc);
        }
      }
      else if (funcNode.Kind == ChildFunctionExKind.CallerNode && callNode.HasCallers) {
        var percentageFunc = PickPercentageFunction(Session.ProfileData.ProfileWeight);

        foreach (var childNode in callNode.Callers) {
          firstNodeEx = CreateProfileCallTree(childNode, funcNode, funcNode.Kind,
                                              visitedNodes, percentageFunc);
        }
      }

      if (firstNodeEx != null) {
        BringIntoView(firstNodeEx);
      }
    }
  }

  private void PanelToolbarTray_DuplicateClicked(object sender, DuplicateEventArgs e) {
    Session.DuplicatePanel(this, e.Kind);
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private Func<TimeSpan, double> PickPercentageFunction(TimeSpan totalWeight) {
    return weight => weight.Ticks / (double)totalWeight.Ticks;
  }

  private void BringIntoView(ChildFunctionEx nodeEx) {
    if (nodeEx.TreeNode != null) {
      CallTreeList.ScrollIntoView(nodeEx.TreeNode);
    }
  }

  private List<ProfileCallTreeNode> GetCallTreeNodes(IRTextFunction function, ProfileCallTree callTree) {
    if (CombineNodes) {
      var combinedNode = callTree.GetCombinedCallTreeNode(function);
      return combinedNode == null ? null : new List<ProfileCallTreeNode> {combinedNode};
    }

    return callTree.GetCallTreeNodes(function);
  }

  private ProfileCallTreeNode GetChildCallTreeNode(ProfileCallTreeNode childNode, ProfileCallTreeNode parentNode,
                                                   ProfileCallTree callTree) {
    if (CombineNodes) {
      return callTree.GetCombinedCallTreeNode(childNode.Function, parentNode);
    }

    return childNode;
  }

  private ChildFunctionEx CreateProfileCallerCalleeTree(IRTextFunction function) {
    var visitedNodes = new HashSet<ProfileCallTreeNode>();
    var rootNode = new ChildFunctionEx(ChildFunctionExKind.Root, this);
    rootNode.Children = new List<ChildFunctionEx>();
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

      rootNode.Children.Add(instanceNode);

      if (instance.HasCallers) {
        // Percentage relative to entire profile for callers.
        percentageFunc = PickPercentageFunction(Session.ProfileData.ProfileWeight);
        var callersNode = CreateProfileCallTreeHeader(ChildFunctionExKind.Header, "Callers", 2);

        if (nodeList.Count > 1) {
          instanceNode.Children.Add(callersNode);
        }
        else {
          rootNode.Children.Add(callersNode);
        }

        foreach (var callerNode in instance.Callers) {
          CreateProfileCallTree(callerNode, callersNode, instanceNode, ChildFunctionExKind.CallerNode,
                                visitedNodes, percentageFunc);
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
          instanceNode.Children.Add(childrenNode);
        }
        else {
          rootNode.Children.Add(childrenNode);
        }

        foreach (var childNode in instance.Children) {
          CreateProfileCallTree(childNode, childrenNode, instanceNode, ChildFunctionExKind.CalleeNode,
                                visitedNodes, percentageFunc);
        }

        visitedNodes.Clear();
      }

      SortCallTreeNodes(instanceNode);
    }

    SortCallTreeNodes(rootNode);
    return rootNode;
  }

  private ChildFunctionEx CreateProfileCallTree() {
    var visitedNodes = new HashSet<ProfileCallTreeNode>();
    var percentageFunc = PickPercentageFunction(Session.ProfileData.ProfileWeight);

    var rootNode = new ChildFunctionEx(ChildFunctionExKind.Root, this);
    rootNode.Children = new List<ChildFunctionEx>();

    foreach (var node in callTree_.RootNodes) {
      visitedNodes.Clear();
      CreateProfileCallTree(node, rootNode, ChildFunctionExKind.CallTreeNode,
                            visitedNodes, percentageFunc);
    }

    return rootNode;
  }

  private ChildFunctionEx CreateProfileCallTree(ProfileCallTreeNode node, ChildFunctionEx parentNodeEx,
                                                ChildFunctionExKind kind,
                                                HashSet<ProfileCallTreeNode> visitedNodes,
                                                Func<TimeSpan, double> percentageFunc) {
    return CreateProfileCallTree(node, parentNodeEx, parentNodeEx, kind, visitedNodes, percentageFunc);
  }

  private ChildFunctionEx CreateProfileCallTree(ProfileCallTreeNode node, ChildFunctionEx parentNodeEx,
                                                ChildFunctionEx actualParentNode, ChildFunctionExKind kind,
                                                HashSet<ProfileCallTreeNode> visitedNodes,
                                                Func<TimeSpan, double> percentageFunc) {
    bool newFunc = visitedNodes.Add(node);
    var nodeEx = CreateProfileCallTreeChild(node, kind, percentageFunc, parentNodeEx);
    parentNodeEx.Children.Add(nodeEx);

    if (!newFunc) {
      return nodeEx; // Recursion in the call graph.
    }

    if (kind == ChildFunctionExKind.CalleeNode) {
      //? TODO: This is still not quite right, the selected nodes
      //? shoud be found on a path that has the current stack frame as a prefix in theirs.
      //? actualParentNode is just the last in that list
      node = GetChildCallTreeNode(node, actualParentNode.CallTreeNode, callTree_);
      nodeEx.CallTreeNode = node;
    }
    else if (kind == ChildFunctionExKind.CallerNode) {
      node = GetChildCallTreeNode(node, null, callTree_);
      nodeEx.CallTreeNode = node;
    }

    switch (kind) {
      case ChildFunctionExKind.CallTreeNode when node.HasChildren: {
        foreach (var childNode in node.Children) {
          CreateProfileCallTree(childNode, nodeEx, nodeEx, kind, visitedNodes, percentageFunc);
        }

        break;
      }
      case ChildFunctionExKind.CalleeNode when node.HasChildren: {
        // For caller-callee mode, use a placeholder than when the tree gets expanded,
        // gets replaced by the real callee nodes.
        var dummyChildNode = CreateProfileCallTreeHeader(ChildFunctionExKind.ChildrenPlaceholder, "Placeholder", 0);
        dummyChildNode.CallTreeNode = node;
        nodeEx.Children.Add(dummyChildNode);
        break;
      }
      case ChildFunctionExKind.CallerNode when node.HasCallers: {
        // For caller-callee mode, use a placeholder than when tree gets expanded,
        // gets replaced by the real caller (backtrace) nodes.
        var dummyChildNode = CreateProfileCallTreeHeader(ChildFunctionExKind.ChildrenPlaceholder, "Placeholder", 0);
        dummyChildNode.CallTreeNode = node;
        nodeEx.Children.Add(dummyChildNode);
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

  private ChildFunctionEx CreateProfileCallTreeChild(ProfileCallTreeNode node, ChildFunctionExKind kind,
                                                     Func<TimeSpan, double> percentageFunc,
                                                     ChildFunctionEx parentNodeEx = null) {
    double weightPercentage = percentageFunc(node.Weight);
    double exclusiveWeightPercentage = percentageFunc(node.ExclusiveWeight);

    var result = new ChildFunctionEx(kind, this, Session.CompilerInfo.NameProvider.FormatFunctionName) {
      Function = node.Function,
      ModuleName = node.ModuleName,
      Time = node.Weight.Ticks,
      CallTreeNode = node,
      Parent = parentNodeEx,
      Percentage = weightPercentage,
      ExclusivePercentage = exclusiveWeightPercentage,
      TextColor = Brushes.Black,
      BackColor = ProfileDocumentMarkerSettings.Default.PickBrushForPercentage(weightPercentage),
      BackColor2 = ProfileDocumentMarkerSettings.Default.PickBrushForPercentage(exclusiveWeightPercentage)
    };

    callTreeNodeToNodeExMap_[node] = result;
    return result;
  }

  private ChildFunctionEx CreateProfileCallTreeHeader(string name, TimeSpan weight, TimeSpan exclusiveWeight,
                                                      Func<TimeSpan, double> percentageFunc, int priority) {
    double weightPercentage = percentageFunc(weight);
    double exclusiveWeightPercentage = percentageFunc(exclusiveWeight);
    return new ChildFunctionEx(ChildFunctionExKind.Header, this) {
      CallTreeNode = new ProfileCallTreeNode(null, null) {Weight = weight, ExclusiveWeight = exclusiveWeight},
      Time = TimeSpan.MaxValue.Ticks - priority,
      FunctionName = name,
      Percentage = weightPercentage,
      ExclusivePercentage = exclusiveWeightPercentage,
      TextColor = Brushes.Black,
      BackColor = ProfileDocumentMarkerSettings.Default.PickBrushForPercentage(weightPercentage),
      BackColor2 = ProfileDocumentMarkerSettings.Default.PickBrushForPercentage(exclusiveWeightPercentage),
      IsMarked = true
    };
  }

  private ChildFunctionEx CreateProfileCallTreeHeader(ChildFunctionExKind kind, string name, int priority) {
    return new ChildFunctionEx(kind, this)
      {Time = TimeSpan.MaxValue.Ticks - priority, FunctionName = name, TextColor = Brushes.Black, IsMarked = true};
  }

  private ChildFunctionEx CreateProfileCallTreeInstance(string name, ProfileCallTreeNode node,
                                                        Func<TimeSpan, double> percentageFunc) {
    var result = CreateProfileCallTreeChild(node, ChildFunctionExKind.Header, percentageFunc);
    result.FunctionName = name;
    result.IsMarked = true;
    return result;
  }

  private async void ChildDoubleClick(object sender, MouseButtonEventArgs e) {
    var childInfo = ((ListViewItem)sender).Content as ChildFunctionEx;

    if (childInfo != null) {
      if (Utils.IsControlModifierActive()) {
        var openMode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTabDockRight : OpenSectionKind.ReplaceCurrent;
        await OpenFunction(childInfo, openMode);
      }
      else {
        await SwitchFunction(childInfo);
      }
    }
  }

  private void ExpandHottestFunctionPath() {
    if (CallTreeList.Nodes.Count > 0) {
      ExpandHottestFunctionPath(CallTreeList.Nodes[0]);
    }
  }

  private void UnmarkAllFunctions() {
    Trace.WriteLine($"=> start unmark: {Environment.TickCount}");

    foreach (var funcEx in callTreeNodeToNodeExMap_.Values) {
      funcEx.IsMarked = false;
    }

    Trace.WriteLine($"=> done unmark: {Environment.TickCount}");
  }

  private void ExpandHottestFunctionPath(TreeNode node) {
    UnmarkAllFunctions();
    ExpandHottestFunctionPathImpl(node);
  }

  private void ExpandHottestFunctionPathImpl(TreeNode node, int depth = 0) {
    var childInfo = node.Tag as ChildFunctionEx;
    childInfo.IsMarked = true;

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
    if (e.Parameter is TreeNode node) {
      ExpandHottestFunctionPath(node);
    }
  }

  private void CollapseCallPathExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (e.Parameter is TreeNode node) {
      CollapseFunctionPath(node);
    }
  }

  private async void SelectFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (e.Parameter is TreeNode node) {
      var childInfo = node.Tag as ChildFunctionEx;
      await SwitchFunction(childInfo);
    }
  }

  private async void OpenFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (e.Parameter is TreeNode node) {
      var childInfo = node.Tag as ChildFunctionEx;
      await OpenFunction(childInfo, OpenSectionKind.ReplaceCurrent);
    }
  }

  private async void OpenFunctionInNewTab(object sender, ExecutedRoutedEventArgs e) {
    if (e.Parameter is TreeNode node) {
      var childInfo = node.Tag as ChildFunctionEx;
      await OpenFunction(childInfo, OpenSectionKind.NewTabDockRight);
    }
  }

  private async void GoBackExecuted(object sender, ExecutedRoutedEventArgs e) {
    await RestorePreviousState();
  }

  private async Task OpenFunction(ChildFunctionEx childInfo, OpenSectionKind openMode) {
    if (childInfo.HasCallTreeNode) {
      await Session.OpenProfileFunction(childInfo.CallTreeNode, openMode);
    }
  }

  private async Task SwitchFunction(ChildFunctionEx childInfo) {
    if (childInfo.HasCallTreeNode) {
      if (function_ != null) {
        stateStack_.Push(function_);
      }

      await Session.SwitchActiveProfileFunction(childInfo.CallTreeNode);
    }
  }

  private async void FunctionFilter_TextChanged(object sender, TextChangedEventArgs e) {
    string text = FunctionFilter.Text.Trim();
    await SearchCallTree(text);
  }

  private async Task SearchCallTree(string text) {
    using var cancelableTask = await searchTask_.CancelPreviousAndCreateTaskAsync();

    if (searchResultNodes_ != null) {
      // Clear previous search results.
      foreach (var node in searchResultNodes_) {
        node.SearchResult = null;
        node.ResetCachedName();
      }
    }

    if (text.Length > 1) {
      searchResultNodes_ = new List<ChildFunctionEx>();
      await Task.Run(() => SearchCallTree(text, callTreeEx_, searchResultNodes_));

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

  private void SearchCallTree(string text, ChildFunctionEx node, List<ChildFunctionEx> matchingNodes) {
    var result = TextSearcher.FirstIndexOf(node.FunctionName, text, 0, TextSearchKind.CaseInsensitive);

    if (result.HasValue) {
      node.SearchResult = result;
      matchingNodes.Add(node);
    }

    foreach (var child in node.Children) {
      SearchCallTree(text, child, matchingNodes);
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
        node.Tag is ChildFunctionEx funcEx &&
        funcEx.HasCallTreeNode) {
      if (settings_.SyncSourceFile) {
        // Load the source file and scroll to the hottest line.
        await Session.OpenProfileSourceFile(funcEx.CallTreeNode);
      }

      if (SyncSelection) {
        await Session.ProfileFunctionSelected(funcEx.CallTreeNode, PanelKind);
      }
    }
    else if (SyncSelection) {
      await Session.ProfileFunctionDeselected();
    }
  }

  private async Task RestorePreviousState() {
    if (stateStack_.TryPop(out var prevFunc)) {
      await Session.SwitchActiveFunction(prevFunc);
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

    #region IToolPanel

  public override ToolPanelKind PanelKind => ToolPanelKind.CallTree;
  public override bool SavesStateToFile => false;

  public override void OnSessionEnd() {
    base.OnSessionEnd();
    Reset();
  }

    #endregion
}