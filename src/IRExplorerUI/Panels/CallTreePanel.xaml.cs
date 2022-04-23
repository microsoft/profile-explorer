// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public CallTreePanel(ISession session) {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
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

        public async Task DisplaProfileCallTree() {
            var profileCallTree = await Task.Run(() => CreateProfileCallTree());
            CallTree.Model = profileCallTree;

            if (true) {
                //? TODO: Option 
                ExpandHottestFunctionPath();
            }
        }

        private async Task<ChildFunctionEx> CreateProfileCallTree() {
            var visitedNodes = new HashSet<ProfileCallTreeNode>();
            var rootNode = new ChildFunctionEx();
            rootNode.Children = new List<ChildFunctionEx>();

            var callTree = Session.ProfileData.CallTree;

            foreach (var node in callTree.RootNodes) {
                visitedNodes.Clear();
                var nodeEx = CreateProfileCallTree(node, rootNode, visitedNodes);
            }

            return rootNode;
        }

        private ChildFunctionEx CreateProfileCallTree(ProfileCallTreeNode node, ChildFunctionEx parentNodeEx,
                                                      HashSet<ProfileCallTreeNode> visitedNodes) {
            //var funcProfile = Session.ProfileData.GetFunctionProfile();
            bool newFunc = visitedNodes.Add(node);
            var nodeEx = CreateProfileCallTreeChild(node);
            parentNodeEx.Children.Add(nodeEx);

            if (!newFunc) {
                return nodeEx; // Recursion in the call graph.
            }

            if (node.HasChildren) {
                foreach (var childNode in node.Children) {
                    CreateProfileCallTree(childNode, nodeEx, visitedNodes);
                }
            }

            // Sort children, since that is not yet supported by the TreeListView control.
            parentNodeEx.Children.Sort((a, b) => {
                if (b.Time > a.Time) {
                    return 1;
                }
                else if (b.Time < a.Time) {
                    return -1;
                }

                return String.Compare(b.Name, a.Name, StringComparison.Ordinal);
            });

            visitedNodes.Remove(node);
            return nodeEx;
        }

        private ChildFunctionEx CreateProfileCallTreeChild(ProfileCallTreeNode node) {
            double weightPercentage = Session.ProfileData.ScaleFunctionWeight(node.Weight);
            double exclusiveWeightPercentage = Session.ProfileData.ScaleFunctionWeight(node.ExclusiveWeight);

            var funcName = node.FunctionName;

            var nameProvider = Session.CompilerInfo.NameProvider;
            if (nameProvider.IsDemanglingSupported) {
                var demanglingOptions = nameProvider.GlobalDemanglingOptions;

                if (true) { //? TODO: Option
                    funcName = nameProvider.DemangleFunctionName(funcName, demanglingOptions);
                }
            }

            var childInfo = new ChildFunctionEx() {
                Function = node.Function,
                Time = node.Weight.Ticks,
                Name = funcName,
                Percentage = weightPercentage,
                PercentageExclusive = exclusiveWeightPercentage,
                Text = $"{weightPercentage.AsPercentageString()} ({node.Weight.AsMillisecondsString()})",
                Text2 = $"{exclusiveWeightPercentage.AsPercentageString()} ({node.ExclusiveWeight.AsMillisecondsString()})",
                TextColor = Brushes.Black,
                BackColor = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(weightPercentage),
                BackColor2 = ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(exclusiveWeightPercentage),
                Children = new List<ChildFunctionEx>(),
                ModuleName = node.Function.ParentSummary.ModuleName
            };
            return childInfo;
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
