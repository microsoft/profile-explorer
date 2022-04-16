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
            var visitedFuncts = new HashSet<IRTextFunction>();
            var rootNode = new ChildFunctionEx();
            rootNode.Children = new List<ChildFunctionEx>();

            foreach (var (func, funcProfile) in Session.ProfileData.FunctionProfiles) {
                //if (func.Name.Contains(@"memset")) {
                //    var temp = funcProfile;
                //    var tempFunc = func;

                //    while (temp != null && temp.HasCallers) {
                //        Trace.WriteLine($"=> At {tempFunc.Name}, weight {temp.Weight}");

                //        var callers = temp.CallerWeights.ToList();
                //        callers.Sort((a, b) => b.Item2.CompareTo(a.Item2));
                //        var caller = callers[0];

                //        var childFunc = Session.FindFunctionWithId(caller.Item1.Item2, caller.Item1.Item1);

                //        if (childFunc != null) {
                //            Trace.WriteLine($"  - found caller {childFunc.Name}, weight {caller.Item2}");
                //            temp = Session.ProfileData.GetFunctionProfile(childFunc);
                //            tempFunc = childFunc;
                //        }
                //        else {
                //            Trace.WriteLine($"!! CALLER NOT FOUND {caller.Item2}");
                //            break;
                //        }
                //    }
                //}

                //if (!func.Name.Contains(@"JIT_NewArr1@@Y")) {
                //    continue;
                //}

                if (funcProfile.HasCallers) {
                    continue;
                }

                if (!funcProfile.HasCallees) {
                    continue;
                }

                visitedFuncts.Clear();
                CreateProfileCallTree(func, rootNode, TimeSpan.Zero, visitedFuncts);
            }

            return rootNode;
        }

        private void CreateProfileCallTree(IRTextFunction function, ChildFunctionEx parentNode,
                                           TimeSpan childTime, HashSet<IRTextFunction> visitedFuncts) {
            bool newFunc = visitedFuncts.Add(function);

            if (!newFunc) {
                return; // Recursion in the call graph.
            }

            var funcProfile = Session.ProfileData.GetFunctionProfile(function);
            var selfInfo = CreateProfileCallTreeChild(function, funcProfile, childTime);
            //? selfInfo.Statistics = GetFunctionStatistics(function);
            parentNode.Children.Add(selfInfo);

            if (funcProfile != null) {
                foreach (var pair in funcProfile.CalleesWeights) {
                    var childFunc = Session.FindFunctionWithId(pair.Key.Item2, pair.Key.Item1);

                    if (childFunc == null) {
                        Debug.Assert(false, "Should be always found");
                        continue;
                    }

                    CreateProfileCallTree(childFunc, selfInfo, pair.Value, visitedFuncts);
                }
            }

            // Sort children, since that is not yet supported by the TreeListView control.
            parentNode.Children.Sort((a, b) => {
                if (b.Time > a.Time) {
                    return 1;
                }
                else if (b.Time < a.Time) {
                    return -1;
                }

                return String.Compare(b.Name, a.Name, StringComparison.Ordinal);
            });

            visitedFuncts.Remove(function);
        }

        private ChildFunctionEx CreateProfileCallTreeChild(IRTextFunction func, FunctionProfileData funcProfile,
                                                           TimeSpan childTime) {
            double weightPercentage = 0;
            double exclusiveWeightPercentage = 0;
            bool hasProfile = funcProfile != null;

            if (hasProfile) {
                if (childTime == TimeSpan.Zero) {
                    childTime = funcProfile.Weight;
                }

                weightPercentage = Session.ProfileData.ScaleFunctionWeight(childTime);
                exclusiveWeightPercentage = Session.ProfileData.ScaleFunctionWeight(funcProfile.ExclusiveWeight);
            }

            var funcName = func.Name;

            var nameProvider = Session.CompilerInfo.NameProvider;
            if (nameProvider.IsDemanglingSupported) {
                var demanglingOptions = nameProvider.GlobalDemanglingOptions;

                if (true) { //? TODO: Option
                    funcName = nameProvider.DemangleFunctionName(funcName, demanglingOptions);
                }
            }

            var childInfo = new ChildFunctionEx() {
                Function = func,
                Time = childTime.Ticks,
                Name = funcName,
                Percentage = weightPercentage,
                PercentageExclusive = exclusiveWeightPercentage,
                Text = !hasProfile ? "" : $"{weightPercentage.AsPercentageString()} ({funcProfile.Weight.AsMillisecondsString()})",
                Text2 = !hasProfile ? "" : $"{exclusiveWeightPercentage.AsPercentageString()} ({funcProfile.ExclusiveWeight.AsMillisecondsString()})",
                TextColor = Brushes.Black,
                BackColor = !hasProfile ? Brushes.Transparent : ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(weightPercentage),
                BackColor2 = !hasProfile ? Brushes.Transparent : ProfileDocumentMarkerOptions.Default.PickBrushForPercentage(exclusiveWeightPercentage),
                Children = new List<ChildFunctionEx>(),
                ModuleName = func.ParentSummary.ModuleName
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
