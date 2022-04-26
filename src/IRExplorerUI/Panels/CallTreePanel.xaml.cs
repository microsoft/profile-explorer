// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
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
using IRExplorerCore.IR;
using OxyPlot;

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
    }
    
    public partial class CallTreePanel : ToolPanelControl, INotifyPropertyChanged {
        public static readonly DependencyProperty ShowToolbarProperty =
            DependencyProperty.Register("ShowToolbar", typeof(bool), typeof(CallTreePanel));

        private IRTextFunction function_;
        private bool combineNodes_;
        private ToolTipHoverPreview stackHoverPreview_;

        public bool ShowToolbar {
            get => (bool)GetValue(ShowToolbarProperty);
            set => SetValue(ShowToolbarProperty, value);
        }

        public CallTreePanel() {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
            ShowToolbar = true;
            DataContext = this;
            stackHoverPreview_ = new ToolTipHoverPreview(CallTree, 
                mousePoint => (UIElement)CallTree.GetObjectAtPoint<ListViewItem>(mousePoint),
                (previewPoint, element) => {
                    var item = (ListViewItem)element;
                    var funcNode = ((TreeListItem)item).Node?.Tag as ChildFunctionEx;
                    var callNode = funcNode?.CallTreeNode;
                    return callNode != null ? CreateStackToolTip(callNode) : null;
                });
        }

        public CallTreePanel(ISession session) : this() {
            Session = session;
        }
        
        public bool CombineNodes {
            get => combineNodes_;
            set {
                if (combineNodes_ != value) {
                    combineNodes_ = value;
                    OnPropertyChanged(nameof(CombineNodes));

                    if (function_ != null) {
                        DisplaProfileCallerCalleeTree(function_);
                    }
                }
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e) {
            Trace.WriteLine($"Key {e.Key}");
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

        public async Task DisplaProfileCallTree() {
            var profileCallTree = await Task.Run(() => CreateProfileCallTree());
            CallTree.Model = profileCallTree;

            if (true) {
                //? TODO: Option 
                ExpandHottestFunctionPath();
            }
        }

        public async Task DisplaProfileCallerCalleeTree(IRTextFunction function) {
            function_ = function;
            var profileCallTree = await Task.Run(() => CreateProfileCallerCalleeTree(function));
            CallTree.Model = profileCallTree;
            ExpandCallTreeTop();
        }

        public void Reset() {
            CallTree.Model = null;
            function_ = null;
        }

        private List<ProfileCallTreeNode> GetCallTreeNodes(IRTextFunction function, ProfileCallTree callTree) {
            if (combineNodes_) {
                return new List<ProfileCallTreeNode>() {
                    callTree.GetCombinedCallTreeNode(function)
                };
            }
            
            return callTree.GetCallTreeNodes(function);
        }

        private async Task<ChildFunctionEx> CreateProfileCallerCalleeTree(IRTextFunction function) {
            var visitedNodes = new HashSet<ProfileCallTreeNode>();
            var rootNode = new ChildFunctionEx();
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
                var name =  isSelf ? "Self" : $"Instance {index++}";
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
                        CreateProfileCallTree(childNode, childrenNode, visitedNodes, percentageFunc);
                    }
                }

                if (instance.HasCallers) {
                    // Percentage relative to entire profile for callers.
                    percentageFunc = PickPercentageFunction(Session.ProfileData.ProfileWeight);
                    var callersNode = CreateProfileCallTreeHeader("Callers", 2);

                    if (nodeList.Count > 1) {
                        instanceNode.Children.Add(callersNode);
                    }
                    else {
                        rootNode.Children.Add(callersNode);
                    }

                    foreach (var n in instance.Callers) {
                        CreateProfileCallTree(n, callersNode, visitedNodes, percentageFunc);
                    }
                }

                SortCallTreeNodes(instanceNode);
            }

            SortCallTreeNodes(rootNode);
            return rootNode;
        }

        private async Task<ChildFunctionEx> CreateProfileCallTree() {
            var visitedNodes = new HashSet<ProfileCallTreeNode>();
            var rootNode = new ChildFunctionEx();
            rootNode.Children = new List<ChildFunctionEx>();

            var percentageFunc = PickPercentageFunction(Session.ProfileData.ProfileWeight);
            var callTree = Session.ProfileData.CallTree;

            foreach (var node in callTree.RootNodes) {
                visitedNodes.Clear();
                CreateProfileCallTree(node, rootNode, visitedNodes, percentageFunc);
            }

            return rootNode;
        }

        private ChildFunctionEx CreateProfileCallTree(ProfileCallTreeNode node, ChildFunctionEx parentNodeEx,
                                                      HashSet<ProfileCallTreeNode> visitedNodes,
                                                      Func<TimeSpan, double> percentageFunc) {
            bool newFunc = visitedNodes.Add(node);
            var nodeEx = CreateProfileCallTreeChild(node, percentageFunc);
            parentNodeEx.Children.Add(nodeEx);

            if (!newFunc) {
                return nodeEx; // Recursion in the call graph.
            }

            if (node.HasChildren) {
                foreach (var childNode in node.Children) {
                    CreateProfileCallTree(childNode, nodeEx, visitedNodes, percentageFunc);
                }
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
                return result != 0 ? result : String.Compare(a.Name, a.Name, StringComparison.Ordinal);
            });
        }

        private void ExpandCallTreeTop() {
            if (CallTree.Nodes.Count > 0) {
                foreach (var childNode in CallTree.Nodes) {
                    childNode.IsExpanded = true;
                }
            }
        }

        private ChildFunctionEx CreateProfileCallTreeChild(ProfileCallTreeNode node,
                                                           Func<TimeSpan, double> percentageFunc) {
            return CreateProfileCallTreeChild(node, node.Weight, node.ExclusiveWeight, percentageFunc);
        }

        private ChildFunctionEx CreateProfileCallTreeChild(ProfileCallTreeNode node, TimeSpan weight, 
                                                           TimeSpan exclusiveWeight, 
                                                           Func<TimeSpan, double> percentageFunc) {
            double weightPercentage = percentageFunc(weight);
            double exclusiveWeightPercentage = percentageFunc(exclusiveWeight);
            string funcName = FormatFunctionName(node);
            string toolTip = null;

            return new ChildFunctionEx() {
                Function = node.Function,
                ModuleName = node.ModuleName,
                Time = weight.Ticks,
                Name = funcName,
                CallTreeNode = node,
                Percentage = weightPercentage,
                PercentageExclusive = exclusiveWeightPercentage,
                Text = $"{weightPercentage.AsPercentageString()} ({weight.AsMillisecondsString()})",
                Text2 = $"{exclusiveWeightPercentage.AsPercentageString()} ({exclusiveWeight.AsMillisecondsString()})",
                TextColor = Brushes.Black,
                BackColor = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(weightPercentage),
                BackColor2 = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(exclusiveWeightPercentage),
            };
        }

        private ChildFunctionEx CreateProfileCallTreeHeader(string name, TimeSpan weight, TimeSpan exclusiveWeight,
                                                            Func<TimeSpan, double> percentageFunc, int priority) {
            double weightPercentage = percentageFunc(weight);
            double exclusiveWeightPercentage = percentageFunc(exclusiveWeight);
            return new ChildFunctionEx() {
                Time = TimeSpan.MaxValue.Ticks - priority,
                Name = name,
                Percentage = weightPercentage,
                PercentageExclusive = exclusiveWeightPercentage,
                Text = $"{weightPercentage.AsPercentageString()} ({weight.AsMillisecondsString()})",
                Text2 = $"{exclusiveWeightPercentage.AsPercentageString()} ({exclusiveWeight.AsMillisecondsString()})",
                TextColor = Brushes.Black,
                BackColor = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(weightPercentage),
                BackColor2 = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(exclusiveWeightPercentage),
                IsMarked = true
            };
        }

        private ChildFunctionEx CreateProfileCallTreeHeader(string name, int priority) {
            return new ChildFunctionEx() {
                Time = TimeSpan.MaxValue.Ticks - priority,
                Name = name,
                TextColor = Brushes.Black,
                IsMarked = true
            };
        }

        private ChildFunctionEx CreateProfileCallTreeInstance(string name, ProfileCallTreeNode node,
                                                              Func<TimeSpan, double> percentageFunc) {
            var result = CreateProfileCallTreeChild(node, node.Weight, node.ExclusiveWeight, percentageFunc);
            result.Name = name;
            //result.Time = long.MaxValue;
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

            if (true) { //? option
                //? TODO: Use CreatePreviewTextBlock to make module name lighter.
                // WinDBG style of combining module and func. name.
                var moduleName = Utils.TryGetFileNameWithoutExtension(node.ModuleName);
                funcName = $"{moduleName}!{funcName}";
            }

            if (funcName.Length > maxLength) {
                funcName = funcName.Substring(0, maxLength);
            }

            return funcName;
        }

        private string CreateStackToolTip(ProfileCallTreeNode node) {
            var builder = new StringBuilder();
            AppendStackToolTipFrames(node, builder);
            return builder.ToString();
        }

        private void AppendStackToolTipFrames(ProfileCallTreeNode node, StringBuilder builder) {
            if (node.HasCallers) {
                AppendStackToolTipFrames(node.Callers[0], builder);
            }

            var percentage = Session.ProfileData.ScaleFunctionWeight(node.Weight);
            builder.Append($"{percentage.AsPercentageString(2, false).PadLeft(6)}  ");
            builder.AppendLine(FormatFunctionName(node, 50));
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.CallTree;
        public override bool SavesStateToFile => false;

        public override async void OnSessionStart() {
            base.OnSessionStart();
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            CallTree.Model = null;
        }

        #endregion

        private void ChildDoubleClick(object sender, MouseButtonEventArgs e) {
            // A double-click on the +/- icon doesn't select an actual node.
            var childInfo = ((ListViewItem)sender).Content as ChildFunctionEx;

            if (childInfo != null) {
                ExpandHottestFunctionPath();
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

        private void OpenFunctionExecuted(object sender, ExecutedRoutedEventArgs e) {
        }

        private void OpenFunctionInNewTab(object sender, ExecutedRoutedEventArgs e) {
        }
        
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
