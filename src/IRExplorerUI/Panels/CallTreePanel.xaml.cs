// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    
    public partial class CallTreePanel : ToolPanelControl {
        public static readonly DependencyProperty ShowToolbarProperty =
            DependencyProperty.Register("ShowToolbar", typeof(bool), typeof(CallTreePanel));
        
        public bool ShowToolbar {
            get => (bool)GetValue(ShowToolbarProperty);
            set => SetValue(ShowToolbarProperty, value);
        }

        public CallTreePanel() {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
            ShowToolbar = true;
            DataContext = this;
        }

        public CallTreePanel(ISession session) : this() {
            Session = session;
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

        private Func<TimeSpan, double> PickPercentageFunction(ProfileCallTreeNode funcProfile = null) {
            if (funcProfile != null && true) { // option
                return weight => (double)weight.Ticks / (double)funcProfile.Weight.Ticks;
            }

            return weight => Session.ProfileData.ScaleFunctionWeight(weight);
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
            var profileCallTree = await Task.Run(() => CreateProfileCallerCalleeTree(function));
            CallTree.Model = profileCallTree;
            ExpandCallTreeTop();
        }

        public void Reset() {
            CallTree.Model = null;
        }

        private async Task<ChildFunctionEx> CreateProfileCallerCalleeTree(IRTextFunction function) {
            var visitedNodes = new HashSet<ProfileCallTreeNode>();
            var rootNode = new ChildFunctionEx();
            rootNode.Children = new List<ChildFunctionEx>();

            var callTree = Session.ProfileData.CallTree;
            var nodeList = callTree.GetCallTreeNodes(function); //? Merge?

            if (nodeList == null) {
                return null;
            }

            var funcProfile = Session.ProfileData.GetFunctionProfile(function);
            int index = 1;

            foreach (var instance in nodeList) {
                var percentageFunc = PickPercentageFunction(instance);
                var instanceNode = CreateProfileCallTreeChild(instance, percentageFunc);
                instanceNode.Name = nodeList.Count > 1 ? $"Instance {index++}" : "Self";
                instanceNode.Time = Int64.MaxValue;
                instanceNode.ToolTip = "Function exclusive time";
                instanceNode.IsMarked = true;
                rootNode.Children.Add(instanceNode);

                //? relative vs instance

                ChildFunctionEx childrenNode = null;
                ChildFunctionEx callersNode = null;

                if (instance.HasChildren) {
                    if (childrenNode == null) {
                        childrenNode = new ChildFunctionEx();
                        childrenNode.Name = "Called";
                        childrenNode.ToolTip = "Called functions";
                        childrenNode.TextColor = Brushes.Black;
                        childrenNode.IsMarked = true;

                        if (nodeList.Count > 1) {
                            instanceNode.Children.Add(childrenNode);
                        }
                        else {
                            rootNode.Children.Add(childrenNode);
                        }
                    }

                    foreach (var childNode in instance.Children) {
                        CreateProfileCallTree(childNode, childrenNode, visitedNodes, percentageFunc);
                    }
                }
                
                if (instance.HasCallers) {

                    if (callersNode == null) {
                        callersNode = new ChildFunctionEx();
                        callersNode.Name = "Callers";
                        callersNode.ToolTip = "Calling functions";
                        callersNode.TextColor = Brushes.Black;
                        callersNode.IsMarked = true;

                        if (nodeList.Count > 1) {
                            instanceNode.Children.Add(callersNode);
                        }
                        else {
                            rootNode.Children.Add(callersNode);
                        }
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

            var percentageFunc = PickPercentageFunction();
            var callTree = Session.ProfileData.CallTree;

            foreach (var node in callTree.RootNodes) {
                visitedNodes.Clear();
                var nodeEx = CreateProfileCallTree(node, rootNode, visitedNodes, percentageFunc);
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
            // Sort children, since that is not yet supported by the TreeListView control.
            node.Children.Sort((a, b) => {
                if (b.Time > a.Time) {
                    return 1;
                }
                else if (b.Time < a.Time) {
                    return -1;
                }

                return String.Compare(b.Name, a.Name, StringComparison.Ordinal);
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
            string funcName = FormatFunctionName(node.FunctionName);
            string toolTip = null;

            if (true) {
                //? TODO: Option
                toolTip = CreateStackToolTip(node);
            }

            var childInfo = new ChildFunctionEx() {
                Function = node.Function,
                ModuleName = node.ModuleName,
                Time = weight.Ticks,
                Name = funcName,
                ToolTip = toolTip,
                Percentage = weightPercentage,
                PercentageExclusive = exclusiveWeightPercentage,
                Text = $"{weightPercentage.AsPercentageString()} ({weight.AsMillisecondsString()})",
                Text2 = $"{exclusiveWeightPercentage.AsPercentageString()} ({exclusiveWeight.AsMillisecondsString()})",
                TextColor = Brushes.Black,
                BackColor = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(weightPercentage),
                BackColor2 = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(exclusiveWeightPercentage),
            };

            return childInfo;
        }

        private string FormatFunctionName(string funcName) {
            if (false) {
                //? TODO: Option
                return funcName;
            }

            var nameProvider = Session.CompilerInfo.NameProvider;
            
            if (nameProvider.IsDemanglingSupported) {
                var demanglingOptions = nameProvider.GlobalDemanglingOptions;
                funcName = nameProvider.DemangleFunctionName(funcName, demanglingOptions);
            }

            return funcName;
        }

        private string CreateStackToolTip(ProfileCallTreeNode node) {
            var builder = new StringBuilder();
            AppendStackToolTipFrames(node, builder);
            Trace.WriteLine(builder);
            Trace.WriteLine("------------------------");
            return builder.ToString();
        }

        private void AppendStackToolTipFrames(ProfileCallTreeNode node, StringBuilder builder) {
            if (node.HasCallers) {
                AppendStackToolTipFrames(node.Callers[0], builder);
            }

            var percentage = Session.ProfileData.ScaleFunctionWeight(node.Weight);
            builder.Append($"{percentage.AsPercentageString().PadLeft(6)} ");
            builder.AppendLine(FormatFunctionName(node.FunctionName));
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
    }
}
