// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aga.Controls.Tree;
using IRExplorerUI.Document;
using IRExplorerCore;
using IRExplorerUI.Compilers.ASM;
using IRExplorerUI.Profile;
using Microsoft.Diagnostics.Tracing.Stacks;
using IRExplorerCore.Graph;
using static System.Net.Mime.MediaTypeNames;
using System.Windows.Media.Media3D;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.VisualBasic;
using IRExplorerUI.Controls;
using System.Security.Cryptography.Xml;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using IRExplorerCore.IR;
using OxyPlot;
using FontWeights = System.Windows.FontWeights;
using System.Diagnostics;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace IRExplorerUI {
    public static class CallTreeCommand {
        public static readonly RoutedUICommand ExpandHottestCallPath =
            new RoutedUICommand("ExpandHottestCallPath", "ExpandHottestCallPath", typeof(CallTreePanel));
        public static readonly RoutedUICommand CollapseCallPath =
            new RoutedUICommand("CollapseCallPath", "CollapseCallPath", typeof(CallTreePanel));
        public static readonly RoutedUICommand SelectFunction =
            new RoutedUICommand("SelectFunction", "SelectFunction", typeof(CallTreePanel));
        public static readonly RoutedUICommand OpenFunction =
            new RoutedUICommand("OpenFunction", "OpenFunction", typeof(CallTreePanel));
        public static readonly RoutedUICommand OpenFunctionInNewTab =
            new RoutedUICommand("OpenFunctionInNewTab", "OpenFunctionInNewTab", typeof(CallTreePanel));
        public static readonly RoutedUICommand FocusSearch =
            new RoutedUICommand("FocusSearch", "FocusSearch", typeof(CallTreePanel));
        public static readonly RoutedUICommand ClearSearch =
            new RoutedUICommand("ClearSearch", "ClearSearch", typeof(CallTreePanel));
    }

    public enum ChildFunctionExKind {
        Root,
        ChildrenPlaceholder,
        CallerNode,
        CalleeNode,
        CallTreeNode,
        Header
    }

    public class ChildFunctionEx : ITreeModel, INotifyPropertyChanged {
        private TextBlock name_;

        public TextBlock Name {
            get {
                if (name_ == null) {
                    name_ = CreateOnDemandName();
                }

                return name_;
            }
        }

        public ChildFunctionExKind Kind { get; set; }
        public bool IsMarked { get; set; }
        public TextSearchResult? SearchResult { get; set; }
        public IRTextFunction Function { get; set; } //? TODO: Could use CallTreeNode.Function
        public ProfileCallTreeNode CallTreeNode { get; set; }
        public TreeNode TreeNode { get; set; } // Associated UI tree node.
        public string FunctionName { get; set; }
        public string ModuleName { get; set; }
        public Brush TextColor { get; set; }
        public Brush BackColor { get; set; }
        public Brush BackColor2 { get; set; }
        public List<ChildFunctionEx> Children { get; set; }
        public long Time { get; set; }
        public double Percentage { get; set; }
        public double ExclusivePercentage { get; set; }

        public bool HasCallTreeNode => CallTreeNode != null;
        public TimeSpan Weight => HasCallTreeNode ? CallTreeNode.Weight : TimeSpan.Zero;
        public TimeSpan ExclusiveWeight => HasCallTreeNode ? CallTreeNode.ExclusiveWeight : TimeSpan.Zero;

        public ChildFunctionEx(ChildFunctionExKind kind) {
            Children = new List<ChildFunctionEx>();
            Kind = kind;
        }

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

        public void ResetCachedName() {
            name_ = null;
            OnPropertyChanged(nameof(Name));
        }

        private TextBlock CreateOnDemandName() {
            var textBlock = new TextBlock();
            var nameFontWeight = IsMarked ? FontWeights.Bold : FontWeights.SemiBold;

            if (IsMarked) {
                textBlock.FontWeight = FontWeights.Bold;
            }

            if (Kind != ChildFunctionExKind.Header &&
                App.Settings.CallTreeSettings.PrependModuleToFunction) {
                if (!string.IsNullOrEmpty(ModuleName)) {
                    textBlock.Inlines.Add(new Run(ModuleName) {
                        Foreground = Brushes.DimGray,
                        FontWeight = IsMarked ? FontWeights.DemiBold : FontWeights.Normal
                    });
                }

                textBlock.Inlines.Add("!");

                if (SearchResult.HasValue) {
                    CreateSearchResultName(textBlock, nameFontWeight);
                }
                else {
                    textBlock.Inlines.Add(new Run(FunctionName) {
                        FontWeight = nameFontWeight
                    });
                }
            }
            else {
                if (SearchResult.HasValue) {
                    CreateSearchResultName(textBlock, nameFontWeight);
                }
                else {
                    textBlock.Inlines.Add(new Run(FunctionName) {
                        FontWeight = nameFontWeight
                    });
                }
            }

            return textBlock;
        }

        private void CreateSearchResultName(TextBlock textBlock, FontWeight nameFontWeight) {
            if (SearchResult.Value.Offset > 0) {
                textBlock.Inlines.Add(new Run(FunctionName.Substring(0, SearchResult.Value.Offset)) {
                    FontWeight = nameFontWeight
                });
            }

            textBlock.Inlines.Add(new Run(FunctionName.Substring(SearchResult.Value.Offset, SearchResult.Value.Length)) {
                Background = Brushes.Khaki
            });

            int remainingLength = FunctionName.Length - (SearchResult.Value.Offset + SearchResult.Value.Length);

            if (remainingLength > 0) {
                textBlock.Inlines.Add(new Run(FunctionName.Substring(FunctionName.Length - remainingLength, remainingLength)) {
                    FontWeight = nameFontWeight
                });
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class CallTreePanel : ToolPanelControl, INotifyPropertyChanged {
        public static readonly DependencyProperty ShowToolbarProperty =
            DependencyProperty.Register("ShowToolbar", typeof(bool), typeof(CallTreePanel));

        private IRTextFunction function_;
        private DraggablePopupHoverPreview stackHoverPreview_;
        private ChildFunctionEx profileCallTree_;
        private List<ChildFunctionEx> searchResultNodes_;
        private CallTreeSettings settings_;

        public bool PrependModuleToFunction {
            get => settings_.PrependModuleToFunction;
            set {
                if (value != settings_.PrependModuleToFunction) {
                    settings_.PrependModuleToFunction = value;
                    OnPropertyChanged();
                    profileCallTree_ = null;

                    if (IsCallerCalleePanel) {
                        DisplaProfileCallerCalleeTree(function_);
                    }
                    else {
                        DisplayProfileCallTree();
                    }
                }
            }
        }

        public bool IsCallerCalleePanel => PanelKind == ToolPanelKind.CallerCallee;

        public CallTreePanel() {
            InitializeComponent();
            settings_ = App.Settings.CallTreeSettings;
            DataContext = this;
            SetupEvents();
        }

        private void SetupEvents() {
            CallTree.NodeExpanded += CallTreeOnNodeExpanded;
            stackHoverPreview_ = new DraggablePopupHoverPreview(CallTree, CreateBacktracePopup);
        }

        private DraggablePopup CreateBacktracePopup(Point mousePoint, Point previewPoint) {
            var element = (UIElement)CallTree.GetObjectAtPoint<ListViewItem>(mousePoint);

            if (element is not TreeListItem treeItem) {
                return null;
            }

            var funcNode = treeItem.Node?.Tag as ChildFunctionEx;
            var callNode = funcNode?.CallTreeNode;

            if (callNode != null) {
                //? TODO: Pass parent and stack trace
                return new CallTreeNodePopup(callNode, null, previewPoint, 500, 400, CallTree, Session);
            }

            return null;
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

                if (funcNode.Kind == ChildFunctionExKind.CalleeNode && callNode.HasChildren) {
                    var percentageFunc = PickPercentageFunction(callNode.Weight);

                    foreach (var childNode in callNode.Children) {
                        CreateProfileCallTree(childNode, funcNode, funcNode.Kind,
                            visitedNodes, percentageFunc);
                    }
                }
                else if (funcNode.Kind == ChildFunctionExKind.CallerNode && callNode.HasCallers) {
                    var percentageFunc = PickPercentageFunction(Session.ProfileData.ProfileWeight);

                    foreach (var childNode in callNode.Callers) {
                        CreateProfileCallTree(childNode, funcNode, funcNode.Kind,
                            visitedNodes, percentageFunc);
                    }
                }
            }
        }

        public CallTreePanel(ISession session) : this() {
            Session = session;
        }

        public bool CombineNodes {
            get => settings_.CombineInstances;
            set {
                if (settings_.CombineInstances != value) {
                    settings_.CombineInstances = value;
                    OnPropertyChanged();

                    if (function_ != null) {
                        DisplaProfileCallerCalleeTree(function_);
                    }
                }
            }
        }

        private bool showSearchSection_;
        public bool ShowSearchSection {
            get => showSearchSection_;
            set {
                if (showSearchSection_ != value) {
                    showSearchSection_ = value;
                    OnPropertyChanged();
                }
            }
        }

        private string searchResultText_;
        public string SearchResultText {
            get => searchResultText_;
            set {
                if (searchResultText_ != value) {
                    searchResultText_ = value;
                    OnPropertyChanged();
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
            return weight => (double)weight.Ticks / (double)totalWeight.Ticks;
        }

        public async Task DisplayProfileCallTree() {
            if (profileCallTree_ != null) {
                return;
            }

            profileCallTree_ = await Task.Run(() => CreateProfileCallTree());
            CallTree.Model = profileCallTree_;

            if (true) {
                //? TODO: Option
                ExpandHottestFunctionPath();
            }
        }

        public async Task DisplaProfileCallerCalleeTree(IRTextFunction function) {
            function_ = function;
            profileCallTree_ = await Task.Run(() => CreateProfileCallerCalleeTree(function));
            CallTree.Model = profileCallTree_;
            ExpandCallTreeTop();
        }

        public void Reset() {
            CallTree.Model = null;
            function_ = null;
        }

        private List<ProfileCallTreeNode> GetCallTreeNodes(IRTextFunction function, ProfileCallTree callTree) {
            if (CombineNodes) {
                var combinedNode = callTree.GetCombinedCallTreeNode(function);
                return combinedNode == null ? null : new List<ProfileCallTreeNode>() { combinedNode };
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
            var rootNode = new ChildFunctionEx(ChildFunctionExKind.Root);
            rootNode.Children = new List<ChildFunctionEx>();

            var callTree = Session.ProfileData.CallTree;
            var nodeList = GetCallTreeNodes(function, callTree);

            if (nodeList == null) {
                return null;
            }

            int index = 1;
            var combinedWeight = callTree.GetCombinedCallTreeNodeWeight(function);

            foreach (var instance in nodeList) {
                bool isSelf = nodeList.Count == 1;
                var name =  isSelf ? "Function" : $"Function instance {index++}";
                var percentageFunc = PickPercentageFunction(combinedWeight);
                var instanceNode = CreateProfileCallTreeInstance(name, instance, percentageFunc);

                if (isSelf) {
                    instanceNode.Time = Int64.MaxValue; // Ensure Self is on top.
                }

                rootNode.Children.Add(instanceNode);

                if (instance.HasChildren) {
                    // Percentage relative to current function callers.
                    //percentageFunc = PickPercentageFunction(instance.Weight);
                    var (childrenWeight, childrentExcWeight) = instance.ChildrenWeight;
                    var childrenNode = CreateProfileCallTreeHeader("Called", childrenWeight, childrentExcWeight, percentageFunc, 1);

                    if (nodeList.Count > 1) {
                        instanceNode.Children.Add(childrenNode);
                    }
                    else {
                        rootNode.Children.Add(childrenNode);
                    }

                    foreach (var childNode in instance.Children) {
                        CreateProfileCallTree(childNode, childrenNode, ChildFunctionExKind.CalleeNode,
                                              visitedNodes, percentageFunc);
                    }
                }

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

                    foreach (var n in instance.Callers) {
                        CreateProfileCallTree(n, callersNode, ChildFunctionExKind.CallerNode,
                                              visitedNodes, percentageFunc);
                    }
                }

                SortCallTreeNodes(instanceNode);
            }

            SortCallTreeNodes(rootNode);
            return rootNode;
        }

        private ChildFunctionEx CreateProfileCallTree() {
            var visitedNodes = new HashSet<ProfileCallTreeNode>();
            var rootNode = new ChildFunctionEx(ChildFunctionExKind.Root);
            rootNode.Children = new List<ChildFunctionEx>();

            var percentageFunc = PickPercentageFunction(Session.ProfileData.ProfileWeight);
            var callTree = Session.ProfileData.CallTree;

            foreach (var node in callTree.RootNodes) {
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
            bool newFunc = visitedNodes.Add(node);
            var nodeEx = CreateProfileCallTreeChild(node, kind, percentageFunc);
            parentNodeEx.Children.Add(nodeEx);

            if (!newFunc) {
                return nodeEx; // Recursion in the call graph.
            }

            //? TODO: This is still not quite right, the selected nodes
            //? shoud be found on a path that has the current stack frame as a prefix in theirs.
            if (kind == ChildFunctionExKind.CalleeNode) {
                node = GetChildCallTreeNode(node, parentNodeEx.CallTreeNode, Session.ProfileData.CallTree);
                nodeEx.CallTreeNode = node;
            }

            if (node.HasChildren) {
                if (kind == ChildFunctionExKind.CalleeNode) {
                    // For caller-callee mode, use a placeholder than when the tree gets expanded,
                    // gets replaced by the real callee nodes.
                    var dummyChildNode = CreateProfileCallTreeHeader(ChildFunctionExKind.ChildrenPlaceholder, "Placeholder", 0);
                    dummyChildNode.CallTreeNode = node;
                    nodeEx.Children.Add(dummyChildNode);
                }
                else {
                    foreach (var childNode in node.Children) {
                        CreateProfileCallTree(childNode, nodeEx, kind, visitedNodes, percentageFunc);
                    }
                }
            }
            else if (node.HasCallers && kind == ChildFunctionExKind.CallerNode) {
                // For caller-callee mode, use a placeholder than when tree gets expanded,
                // gets replaced by the real caller (backtrace) nodes.
                var dummyChildNode = CreateProfileCallTreeHeader(ChildFunctionExKind.ChildrenPlaceholder, "Placeholder", 0);
                dummyChildNode.CallTreeNode = node;
                nodeEx.Children.Add(dummyChildNode);
            }

            SortCallTreeNodes(parentNodeEx);
            visitedNodes.Remove(node);
            return nodeEx;
        }

        private static void SortCallTreeNodes(ChildFunctionEx node) {
            // Sort children in descending order,
            // since that is not yet supported by the TreeListView control.
            node.Children.Sort((a, b) => {
                int result = b.Time.CompareTo(a.Time);
                return result != 0 ? result : String.Compare(a.FunctionName, a.FunctionName, StringComparison.Ordinal);
            });
        }

        private void ExpandCallTreeTop() {
            if (CallTree.Nodes.Count > 0) {
                foreach (var childNode in CallTree.Nodes) {
                    childNode.IsExpanded = true;
                }
            }
        }

        private ChildFunctionEx CreateProfileCallTreeChild(ProfileCallTreeNode node, ChildFunctionExKind kind,
                                                           Func<TimeSpan, double> percentageFunc) {
            double weightPercentage = percentageFunc(node.Weight);
            double exclusiveWeightPercentage = percentageFunc(node.ExclusiveWeight);
            string funcName = FormatFunctionName(node);

            return new ChildFunctionEx(kind) {
                Function = node.Function,
                ModuleName = node.ModuleName,
                Time = node.Weight.Ticks,
                FunctionName = funcName,
                CallTreeNode = node,
                Percentage = weightPercentage,
                ExclusivePercentage = exclusiveWeightPercentage,
                TextColor = Brushes.Black,
                BackColor = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(weightPercentage),
                BackColor2 = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(exclusiveWeightPercentage),
            };
        }

        private ChildFunctionEx CreateProfileCallTreeHeader(string name, TimeSpan weight, TimeSpan exclusiveWeight,
                                                            Func<TimeSpan, double> percentageFunc, int priority) {
            double weightPercentage = percentageFunc(weight);
            double exclusiveWeightPercentage = percentageFunc(exclusiveWeight);
            return new ChildFunctionEx(ChildFunctionExKind.Header) {
                CallTreeNode = new ProfileCallTreeNode(null, null) {
                    Weight = weight,
                    ExclusiveWeight = exclusiveWeight
                },
                Time = TimeSpan.MaxValue.Ticks - priority,
                FunctionName = name,
                Percentage = weightPercentage,
                ExclusivePercentage = exclusiveWeightPercentage,
                TextColor = Brushes.Black,
                BackColor = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(weightPercentage),
                BackColor2 = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(exclusiveWeightPercentage),
                IsMarked = true
            };
        }

        private ChildFunctionEx CreateProfileCallTreeHeader(ChildFunctionExKind kind, string name, int priority) {
            return new ChildFunctionEx(kind) {
                Time = TimeSpan.MaxValue.Ticks - priority,
                FunctionName = name,
                TextColor = Brushes.Black,
                IsMarked = true
            };
        }

        private ChildFunctionEx CreateProfileCallTreeInstance(string name, ProfileCallTreeNode node,
                                                              Func<TimeSpan, double> percentageFunc) {
            var result = CreateProfileCallTreeChild(node, ChildFunctionExKind.Header, percentageFunc);
            result.FunctionName = name;
            result.IsMarked = true;
            return result;
        }

        private string FormatFunctionName(ProfileCallTreeNode node, int maxLength = int.MaxValue) {
            var funcName = node.FunctionName;

            if (true) {
                //? option
                var nameProvider = Session.CompilerInfo.NameProvider;

                if (nameProvider.IsDemanglingSupported) {
                    funcName = nameProvider.DemangleFunctionName(funcName, nameProvider.GlobalDemanglingOptions);
                }
            }

            if (funcName.Length > maxLength) {
                funcName = $"{funcName.Substring(0, maxLength)}...";
            }

            return funcName;
        }

        private string CreateStackBackTrace(ProfileCallTreeNode node) {
            var builder = new StringBuilder();
            AppendStackToolTipFrames(node, builder);
            return builder.ToString();
        }

        private void AppendStackToolTipFrames(ProfileCallTreeNode node, StringBuilder builder) {
            var percentage = Session.ProfileData.ScaleFunctionWeight(node.Weight);
            var funcName = FormatFunctionName(node, 80);

            if (settings_.PrependModuleToFunction) {
                funcName = $"{node.ModuleName}!{funcName}";
            }

            builder.Append($"{percentage.AsPercentageString(2, false).PadLeft(6)} | {node.Weight.AsMillisecondsString()} | {funcName}");
            builder.AppendLine(funcName);

            if (node.HasCallers) {
                AppendStackToolTipFrames(node.Callers[0], builder);
            }
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.CallTree;
        public override bool SavesStateToFile => false;

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            CallTree.Model = null;
        }

        #endregion

        private async void ChildDoubleClick(object sender, MouseButtonEventArgs e) {
            var childInfo = ((ListViewItem)sender).Content as ChildFunctionEx;

            if (childInfo != null && childInfo.Function.HasSections) {
                var openMode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTabDockRight : OpenSectionKind.ReplaceCurrent;
                await OpenFunction(childInfo, openMode);
            }
        }

        private void ChildClick(object sender, MouseButtonEventArgs e) {
            var childInfo = ((ListViewItem)sender).Content as ChildFunctionEx;

            if (childInfo != null) {
                Session.SwitchActiveFunction(childInfo.Function);
            }
        }

        private void ExpandHottestFunctionPath() {
            if (CallTree.Nodes.Count > 0) {
                ExpandHottestFunctionPath(CallTree.Nodes[0]);
            }
        }

        private void UnmarkAllFunctions() {
            foreach (var node in CallTree.Nodes) {
                var childInfo = node.Tag as ChildFunctionEx;
                childInfo.IsMarked = false;
            }
        }

        private void ExpandHottestFunctionPath(TreeNode node) {
            UnmarkAllFunctions();
            ExpandHottestFunctionPathImpl(node);
        }

        private void ExpandHottestFunctionPathImpl(TreeNode node) {
            var childInfo = node.Tag as ChildFunctionEx;
            childInfo.IsMarked = true;

            if (node.HasChildren) {
                node.IsExpanded = true;
                ExpandHottestFunctionPath(node.Nodes[0]);
            }
        }

        private void CollapseFunctionPath(TreeNode node, bool recursive = false) {
            if (recursive) {
                foreach (var child in node.Nodes) {
                    CollapseFunctionPath(child, recursive);
                }
            }

            node.IsExpanded = false;
        }

        private void ExpandHottestCallPathExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is TreeNode node) {
                UnmarkAllFunctions();
                ExpandHottestFunctionPath(node);
            }
        }

        private void CollapseCallPathExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is TreeNode node) {
                CollapseFunctionPath(node);
            }
        }

        private void SelectFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is TreeNode node) {
                var childInfo = node.Tag as ChildFunctionEx;
                Session.SwitchActiveFunction(childInfo.Function);
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

        private async Task OpenFunction(ChildFunctionEx childInfo, OpenSectionKind openMode) {
            if (childInfo != null && childInfo.Function.HasSections) {
                var args = new OpenSectionEventArgs(childInfo.Function.Sections[0], openMode);
                await Session.SwitchDocumentSectionAsync(args);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void FunctionFilter_TextChanged(object sender, TextChangedEventArgs e) {
            var text = FunctionFilter.Text.Trim();

            if (searchResultNodes_ != null) {
                // Clear previous search results.
                foreach (var node in searchResultNodes_) {
                    node.SearchResult = null;
                    node.ResetCachedName();
                }
            }

            if (text.Length > 1) {
                int results = SearchCallTree(text);
                ShowSearchSection = true;
                SearchResultText = results != 0 ? $"{searchResultNodes_.Count}" : "Not found";
            }
            else {
                ShowSearchSection = false;
            }
        }

        int SearchCallTree(string text) {
            var matchingNodes = new List<ChildFunctionEx>();
            SearchCallTree(text, profileCallTree_, matchingNodes);
            searchResultNodes_ = matchingNodes;

            foreach (var node in searchResultNodes_) {
                node.ResetCachedName();

                // Expand path to the node.
                for (var treeNode = node.TreeNode; treeNode != null; treeNode = treeNode.Parent) {
                    treeNode.IsExpanded = true;
                }
            }

            return searchResultNodes_.Count;
        }

        void SearchCallTree(string text, ChildFunctionEx node, List<ChildFunctionEx> matchingNodes) {
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

        private async void CallTreeButton_OnClick(object sender, RoutedEventArgs e) {
            var panel = Session.FindAndActivatePanel(ToolPanelKind.CallTree) as CallTreePanel;

            if (panel != null) {
                await panel.DisplayProfileCallTree();
            }
        }

        private void ClearSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
            ((TextBox)e.Parameter).Text = string.Empty;
        }

        private FlameGraphViewer fgViewer_;

        private async void Button_Click(object sender, RoutedEventArgs e) {
            var fg = await CreateFlameGraph(800,500);
            //w.Content = fg;
            //w.Show();

            Session.DisplayFloatingPanel(fg);
        }

        private async Task<FlameGraphPanel> CreateFlameGraph( double width, double height) {
            var panel = new FlameGraphPanel();
            panel.Width = width;
            panel.Height = height;
            panel.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.VerticalAlignment = VerticalAlignment.Stretch;
            await panel.Initialize(Session.ProfileData.CallTree);
            return panel;
        }
    }
}