// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Google.Protobuf.WellKnownTypes;
using IRExplorerUI.DebugServer;
using IRExplorerUI.Diff;
using IRExplorerUI.Document;
using IRExplorerUI.OptionsPanels;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using Microsoft.Win32;
using IRExplorerUI.Query;
using IRExplorerUI.Compilers.ASM;
using IRExplorerUI.Profile;

namespace IRExplorerUI {
    public partial class MainWindow : Window, ISession {
        private CancelableTaskInstance updateProfileTask_ = new();
        public ProfileData ProfileData => sessionState_?.ProfileData;

        public async Task<bool> LoadProfileData(string profileFilePath, List<int> processIds,
                                              ProfileDataProviderOptions options,
                                              SymbolFileSourceOptions symbolOptions,
                                              ProfileDataReport report,
                                              ProfileLoadProgressHandler progressCallback,
                                              CancelableTask cancelableTask) {
            using var profileData = new ETWProfileDataProvider(this);
            var result = await profileData.LoadTraceAsync(profileFilePath, processIds,
                                                      options, symbolOptions,
                                                      report, progressCallback, cancelableTask);
            if (!IsSessionStarted) {
                return false;
            }

            if (result != null) {
                result.Report = report;
                sessionState_.ProfileData = result;
                UpdateWindowTitle();
            }

            return result != null;
        }

        public async Task<bool> LoadProfileData(RawProfileData data, List<int> processIds,
                                              ProfileDataProviderOptions options,
                                              SymbolFileSourceOptions symbolOptions,
                                              ProfileDataReport report,
                                              ProfileLoadProgressHandler progressCallback,
                                              CancelableTask cancelableTask) {
            using var profileData = new ETWProfileDataProvider(this);
            var result = await profileData.LoadTraceAsync(data, processIds,
                                                      options, symbolOptions,
                                                      report, progressCallback, cancelableTask);
            if (!IsSessionStarted) {
                return false;
            }

            if (result != null) {
                result.Report = report;
                sessionState_.ProfileData = result;
                UpdateWindowTitle();
            }

            return result != null;
        }

        private async void LoadProfileExecuted(object sender, ExecutedRoutedEventArgs e) {
            var window = new ProfileLoadWindow(this, false);
            window.Owner = this;
            var result = window.ShowDialog();

            if (result.HasValue && result.Value) {
                await SectionPanel.RefreshModuleSummaries();
                SetOptionalStatus(TimeSpan.FromSeconds(10), "Profile data loaded");
            }
        }

        private async void RecordProfileExecuted(object sender, ExecutedRoutedEventArgs e) {
            var window = new ProfileLoadWindow(this, true);
            window.Owner = this;
            var result = window.ShowDialog();

            if (result.HasValue && result.Value) {
                await SectionPanel.RefreshModuleSummaries();
                SetOptionalStatus(TimeSpan.FromSeconds(10), "Profile data loaded");
            }
        }

        private void CanExecuteProfileCommand(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = sessionState_ != null && sessionState_.ProfileData != null;
            e.Handled = true;
        }

        private void CanExecuteLoadProfileCommand(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = sessionState_ == null || sessionState_.ProfileData == null;
            e.Handled = true;
        }

        private void ViewProfileReportExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (ProfileData?.Report != null) {
                ProfileReportPanel.ShowReportWindow(ProfileData.Report, this);
            }
        }

        public async Task<bool> FilterProfileSamples(ProfileSampleFilter filter) {
            using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

            SetApplicationProgress(true, double.NaN, "Filtering");
            StartUIUpdate();

            var sw = Stopwatch.StartNew();
            Trace.WriteLine($"--------------------------------------------------------\n");
            Trace.WriteLine($"Filter {filter}, samples {ProfileData.Samples.Count}");

            {
                var sw2 = Stopwatch.StartNew();
                await Task.Run(() => ProfileData.FilterFunctionProfile(filter));
                Trace.WriteLine($"1) ComputeFunctionProfile {sw2.ElapsedMilliseconds}");
            }

            {
                var sw2 = Stopwatch.StartNew();
                await SectionPanel.RefreshProfile();
                Trace.WriteLine($"2) RefreshProfile {sw2.ElapsedMilliseconds}");
            }

            sw.Stop();
            Trace.WriteLine($"Total: {sw.ElapsedMilliseconds}");

            await ProfileSampleRangeDeselected();

            SetApplicationProgress(false, double.NaN);
            StopUIUpdate();
            return true;
        }

        public async Task<bool> RemoveProfileSamplesFilter() {
            await FilterProfileSamples(new ProfileSampleFilter());
            await ProfileSampleRangeDeselected();
            return true;
        }

        public async Task<bool> OpenProfileFunction(ProfileCallTreeNode node, OpenSectionKind openMode) {
            if (node.Function == null) {
                return false;
            }

            return await OpenProfileFunction(node.Function, openMode);
        }

        public async Task<bool> OpenProfileFunction(IRTextFunction function, OpenSectionKind openMode) {
            var args = new OpenSectionEventArgs(function.Sections[0], openMode);
            await SwitchDocumentSectionAsync(args);
            return true;
        }

        public async Task<bool> SwitchActiveProfileFunction(ProfileCallTreeNode node) {
            if (node.Function == null) {
                return false;
            }

            await SwitchActiveFunction(node.Function);
            return true;
        }

        public async Task<bool> OpenProfileSourceFile(ProfileCallTreeNode node) {
            if (node.Function == null) {
                return false;
            }

            return await OpenProfileSourceFile(node.Function);
        }

        public async Task<bool> OpenProfileSourceFile(IRTextFunction function) {
            var panel = FindPanel(ToolPanelKind.Source) as SourceFilePanel;

            if (panel != null) {
                await panel.LoadSourceFile(function.Sections[0]);
            }

            //? TODO: Option to also open new document if there is no active document.
            if (FindActiveDocumentHost() != null) {
                await OpenProfileFunction(function, OpenSectionKind.ReplaceCurrent);
            }

            return true;
        }

        public async Task<bool> SelectProfileFunction(ProfileCallTreeNode node, ToolPanelKind panelKind) {
            using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

            switch (panelKind) {
                case ToolPanelKind.CallTree: {
                    var panel = FindAndActivatePanel(ToolPanelKind.CallTree) as CallTreePanel;
                    panel.SelectFunction(node);
                    break;
                }
                case ToolPanelKind.FlameGraph: {
                    var panel = FindAndActivatePanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
                    panel.SelectFunction(node);
                    break;
                }
                case ToolPanelKind.Timeline: {
                    var panel = FindAndActivatePanel(ToolPanelKind.Timeline) as TimelinePanel;
                    await SelectFunctionSamples(node, panel);
                    break;
                }
                case ToolPanelKind.Source: {
                    await OpenProfileSourceFile(node);
                    break;
                }
                default: {
                    throw new InvalidOperationException();
                }
            }

            return true;
        }

        public async Task<bool> SelectProfileFunction(IRTextFunction func, ToolPanelKind panelKind) {
            using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

            switch (panelKind) {
                case ToolPanelKind.CallTree: {
                    var panel = FindAndActivatePanel(ToolPanelKind.CallTree) as CallTreePanel;
                    panel.SelectFunction(func);
                    break;
                }
                case ToolPanelKind.FlameGraph: {
                    var panel = FindAndActivatePanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
                    await panel.SelectFunction(func);
                    break;
                }
                case ToolPanelKind.Timeline: {
                    var panel = FindAndActivatePanel(ToolPanelKind.Timeline) as TimelinePanel;

                    //? TODO: Should include samples from all func instances
                    var nodeList = ProfileData.CallTree.GetSortedCallTreeNodes(func);

                    if (nodeList != null && nodeList.Count > 0) {
                        await SelectFunctionSamples(nodeList[0], panel);
                    }
                    break;
                }
                //? TODO: Source panel once button in Summary added
                default: {
                    throw new InvalidOperationException();
                }
            }

            return true;
        }
        
        public async Task<bool> ProfileSampleRangeSelected(SampleTimeRangeInfo range) {
            using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

            //? TODO: If an event fires during the call tree/sample filtering,
            //? either ignore it or better ruin it after the filtering is done
            if (ProfileData.CallTree == null) {
                return false;
            }

            var funcs = await Task.Run(() => 
                FindFunctionsForSamples(range.StartSampleIndex, range.EndSampleIndex,
                                        range.ThreadId, ProfileData));
            var sectinPanel = FindPanel(ToolPanelKind.Section) as SectionPanelPair;
            sectinPanel?.MarkFunctions(funcs.ToList());

            var nodes = await Task.Run(() => 
                FindCallTreeNodesForSamples(funcs, ProfileData));
            var panel = FindPanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
            panel?.MarkFunctions(nodes);
            return true;
        }

        public async Task<bool> ProfileFunctionSelected(ProfileCallTreeNode node, ToolPanelKind sourcePanelKind) {
            using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

            //? TODO: If an event fires during the call tree/sample filtering,
            //? either ignore it or better ruin it after the filtering is done
            if (ProfileData.CallTree == null) {
                return false;
            }

            var panel = FindPanel(ToolPanelKind.Timeline) as TimelinePanel;

            if (panel != null) {
                //? TODO: Select only samples included only in this call node,
                //? right now selects any instance of the func
                await SelectFunctionSamples(node, panel);
            }

            if (sourcePanelKind != ToolPanelKind.Section) {
                await SwitchActiveFunction(node.Function, false);
            }

            if (sourcePanelKind != ToolPanelKind.CallTree) {
                var callTreePanel = FindPanel(ToolPanelKind.CallTree) as CallTreePanel;
                callTreePanel?.SelectFunction(node.Function);
            }

            if (sourcePanelKind != ToolPanelKind.CallerCallee) {
                if (FindPanel(ToolPanelKind.CallerCallee) is CallTreePanel callerCalleePanel) {
                    await callerCalleePanel?.DisplayProfileCallerCalleeTree(node.Function);
                }
            }

            if (sourcePanelKind != ToolPanelKind.FlameGraph) {
                if(FindPanel(ToolPanelKind.FlameGraph) is FlameGraphPanel flameGraphPanel) {
                    await flameGraphPanel.SelectFunction(node.Function, false);
                }
            }

            return true;
        }

        private async Task SelectFunctionSamples(ProfileCallTreeNode node, TimelinePanel panel) {
            var threadSamples = await Task.Run(() => FindFunctionSamples(node, ProfileData));
            panel.SelectFunctionSamples(threadSamples);
        }

        public async Task<bool> MarkProfileFunction(ProfileCallTreeNode node, ToolPanelKind sourcePanelKind,
                                                    HighlightingStyle style) {
            if (sourcePanelKind == ToolPanelKind.Timeline) {
                var panel = FindPanel(ToolPanelKind.Timeline) as TimelinePanel;

                if (panel != null) {
                    var threadSamples = await Task.Run(() => FindFunctionSamples(node, ProfileData));
                    panel.MarkFunctionSamples(node, threadSamples, style);
                }
            }
            return true;
        }

        public async Task<bool> ProfileFunctionSelected(IRTextFunction function, ToolPanelKind sourcePanelKind) {
            if (ProfileData.CallTree == null) {
                return false;
            }
            
            var funcNodes = ProfileData.CallTree.GetSortedCallTreeNodes(function);

            if (funcNodes.Count > 0) {
                await ProfileFunctionSelected(funcNodes[0], sourcePanelKind);
            }

            return true;
        }

        public async Task<bool> ProfileSampleRangeDeselected() {
            using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

            var panel = FindPanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
            panel?.ClearMarkedFunctions();

            var sectinPanel = FindPanel(ToolPanelKind.Section) as SectionPanelPair;
            sectinPanel?.ClearMarkedFunctions();
            return true;
        }

        public async Task<bool> ProfileFunctionDeselected() {
            using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

            var panel = FindPanel(ToolPanelKind.Timeline) as TimelinePanel;
            panel?.ClearSelectedFunctionSamples();
            
            var callerCalleePanel = FindPanel(ToolPanelKind.CallerCallee) as CallTreePanel;
            callerCalleePanel?.Reset();
            return true;
        }

        private Dictionary<int, List<SampleIndex>>
            FindFunctionSamples(ProfileCallTreeNode node, ProfileData profile) {
            var sw = Stopwatch.StartNew();
            var allThreadsList = new List<SampleIndex>();
            var threadListMap = new Dictionary<int, List<SampleIndex>>();
            threadListMap[-1] = allThreadsList;

            if (node.Function == null) {
                return threadListMap;
            }

            int sampleStartIndex = 0;
            int sampleEndIndex = profile.Samples.Count;
            var funcProfile = profile.GetFunctionProfile(node.Function);

            if (funcProfile != null && funcProfile.SampleStartIndex != int.MaxValue) {
                sampleStartIndex = funcProfile.SampleStartIndex;
                sampleEndIndex = funcProfile.SampleEndIndex;
            }

            //? TODO: Abstract parallel run chunks to take action per sample
            for (int i = sampleStartIndex; i < sampleEndIndex; i++) {
                var (sample, stack) = profile.Samples[i];

                ProfileCallTreeNode currentNode = node;
                bool match = false;

                for(int k = 0; k < stack.StackFrames.Count; k++) {
                    var stackFrame = stack.StackFrames[k];
                    if (stackFrame.IsUnknown) {
                        continue;
                    }

                    if (currentNode == null || currentNode.IsGroup) {
                        // Mismatch along the call path leading to the function.
                        match = false;
                        break;
                    }
                    else if (stackFrame.FrameDetails.Function.Value.Equals(currentNode.Function)) {
                        // Continue checking if the callers show up on the stack trace
                        // to make the search context-sensitive.
                        match = true;
                        currentNode = currentNode.Caller;
                    }
                    else if (match) {
                        // Mismatch along the call path leading to the function.
                        match = false;
                        break;
                    }
                }

                if (match) {
                    var threadList = threadListMap.GetOrAddValue(stack.Context.ThreadId);
                    threadList.Add(new SampleIndex(i, sample.Time));
                    allThreadsList.Add(new SampleIndex(i, sample.Time));
                }
            }

            Trace.WriteLine($"FindSamples took: {sw.ElapsedMilliseconds} for {allThreadsList.Count} samples");
            return threadListMap;
        }


        private HashSet<IRTextFunction> FindFunctionsForSamples(int sampleStartIndex, int sampleEndIndex, int threadId, ProfileData profile) {
            var funcSet = new HashSet<IRTextFunction>();

            //? TODO: If an event fires during the call tree/sample filtering,
            //? either ignore it or better run it after the filtering is done
            if (ProfileData.CallTree == null) {
                return funcSet;
            }
            
            //? TODO: Abstract parallel run chunks to take action per sample (ComputeFunctionProfile)
            for (int i = sampleStartIndex; i < sampleEndIndex; i++) {
                var (sample, stack) = profile.Samples[i];

                if (threadId != -1 && stack.Context.ThreadId != threadId) {
                    continue;
                }

                foreach (var stackFrame in stack.StackFrames) {
                    if (stackFrame.IsUnknown)
                        continue;

                    if (stackFrame.FrameDetails.Function == null) {
                        Trace.TraceError($"Function is null for {stackFrame.FrameDetails}");
                        Utils.WaitForDebugger();
                        continue;
                    }

                    funcSet.Add(stackFrame.FrameDetails.Function);
                }
            }

            return funcSet;
        }

        private List<ProfileCallTreeNode> FindCallTreeNodesForSamples(HashSet<IRTextFunction> funcs, ProfileData profile) {
            //? TODO: If an event fires during the call tree/sample filtering,
            //? either ignore it or better run it after the filtering is done
            if (ProfileData.CallTree == null) {
                return new List<ProfileCallTreeNode>();
            }

            var callNodes = new HashSet<ProfileCallTreeNode>(funcs.Count);

            foreach (var func in funcs) {
                if (func == null) {
                    Trace.TraceError($"Function is null in list");
                    Utils.WaitForDebugger();
                    continue;
                }

                var nodes = profile.CallTree.GetCallTreeNodes(func);
                if (nodes != null) {
                    // Filter out nodes that are not in the call path leading to the function,
                    // meaning that all parents of the node instance must be in the initial set
                    // of functions covered by the samples.
                    foreach (var node in nodes) {
                        var parentNode = node.Caller;
                        bool addNode = true;

                        while (parentNode != null) {
                            if (!funcs.Contains(parentNode.Function)) {
                                addNode = false;
                                break;
                            }

                            parentNode = parentNode.Caller;
                        }

                        if (addNode) {
                            callNodes.Add(node);
                        }
                    }
                }
            }

            return callNodes.ToList();
        }

    }
}
