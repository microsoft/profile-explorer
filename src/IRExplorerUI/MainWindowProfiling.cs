// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using IRExplorerUI.Windows;

namespace IRExplorerUI;

public partial class MainWindow : Window, ISession {
  private CancelableTaskInstance updateProfileTask_ = new CancelableTaskInstance();
  private ProfileData.ProcessingResult allThreadsProfile_;
  private Dictionary<ProfileSampleFilter, ProfileData.ProcessingResult> prevProfiles =
    new Dictionary<ProfileSampleFilter, ProfileData.ProcessingResult>();
  public ProfileData ProfileData => sessionState_?.ProfileData;

  public async Task<bool> LoadProfileData(string profileFilePath, List<int> processIds,
                                          ProfileDataProviderOptions options,
                                          SymbolFileSourceSettings symbolSettings,
                                          ProfileDataReport report,
                                          ProfileLoadProgressHandler progressCallback,
                                          CancelableTask cancelableTask) {
    using var provider = new ETWProfileDataProvider(this);
    var result = await provider.LoadTraceAsync(profileFilePath, processIds,
                                               options, symbolSettings,
                                               report, progressCallback, cancelableTask);

    if (!IsSessionStarted) {
      return false;
    }

    if (result != null) {
      result.Report = report;
      sessionState_.ProfileData = result;
      UpdateWindowTitle();
      UnloadProfilingDebugInfo();
    }

    return result != null;
  }

  public async Task<bool> LoadProfileData(RawProfileData data, List<int> processIds,
                                          ProfileDataProviderOptions options,
                                          SymbolFileSourceSettings symbolSettings,
                                          ProfileDataReport report,
                                          ProfileLoadProgressHandler progressCallback,
                                          CancelableTask cancelableTask) {
    using var provider = new ETWProfileDataProvider(this);
    var result = await provider.LoadTraceAsync(data, processIds,
                                               options, symbolSettings,
                                               report, progressCallback, cancelableTask);

    if (!IsSessionStarted) {
      return false;
    }

    if (result != null) {
      result.Report = report;
      sessionState_.ProfileData = result;
      UpdateWindowTitle();
      UnloadProfilingDebugInfo();
    }

    return result != null;
  }

  public async Task<bool> FilterProfileSamples(ProfileSampleFilter filter) {
    using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

    SetApplicationProgress(true, double.NaN, "Filtering profiling data");
    StartUIUpdate();

    var totalSw = Stopwatch.StartNew();
    Trace.WriteLine("--------------------------------------------------------\n");
    Trace.WriteLine($"Filter {filter}, samples {ProfileData.Samples.Count}");

    var filterSw = Stopwatch.StartNew();
    ProfileData.ProcessingResult result = null;

    if (filter.IncludesAll && allThreadsProfile_ != null) {
      Trace.WriteLine("Restore main profile");
      result = ProfileData.RestorePreviousProfile(allThreadsProfile_);
    }
    // else if (prevProfiles.TryGetValue(filter, out result)) {
    //   Trace.WriteLine($"Restore other profile");
    //   result = ProfileData.RestorePreviousProfile(allThreadsProfile_);
    // }
    else {
      Trace.WriteLine("Compute new profile");
      result = await Task.Run(() => ProfileData.FilterFunctionProfile(filter));
    }

    if (result.Filter.IncludesAll) {
      Trace.WriteLine("Save main profile");
      allThreadsProfile_ = result;
    }

    //prevProfiles[result.Filter] = result;
    Trace.WriteLine($"ComputeFunctionProfile time: {filterSw.ElapsedMilliseconds} ms");

    // Update all profiling panels.
    var updateSw = Stopwatch.StartNew();
    await SectionPanel.RefreshProfile();
    await RefreshProfilingPanels();
    await ProfileSampleRangeDeselected();

    Trace.WriteLine($"RefreshProfile time: {updateSw.ElapsedMilliseconds} ms");
    Trace.WriteLine($"FilterProfileSamples time: {totalSw.ElapsedMilliseconds} ms");
    Trace.WriteLine("--------------------------------------------------------\n");

    ResetApplicationProgress();
    StopUIUpdate();
    return true;
  }

  public async Task<bool> RemoveProfileSamplesFilter() {
    await FilterProfileSamples(new ProfileSampleFilter());
    await ProfileSampleRangeDeselected();
    return true;
  }

  public async Task<bool> OpenProfileFunction(ProfileCallTreeNode node, OpenSectionKind openMode,
                                              ProfileSampleFilter instanceFilter = null) {
    if (node.Function == null) {
      return false;
    }

    return await OpenProfileFunction(node.Function, openMode, instanceFilter);
  }

  public async Task<bool> OpenProfileFunction(IRTextFunction function, OpenSectionKind openMode,
                                              ProfileSampleFilter instanceFilter = null) {
    var args = new OpenSectionEventArgs(function.Sections[0], openMode);
    var docHost = await SwitchDocumentSectionAsync(args);

    if (instanceFilter != null) {
      await docHost.SwitchProfileInstanceAsync(instanceFilter);
    }

    return true;
  }

  public async Task<bool> SwitchActiveProfileFunction(ProfileCallTreeNode node) {
    if (node.Function == null) {
      return false;
    }

    await SwitchActiveFunction(node.Function);
    return true;
  }

  public async Task<bool> OpenProfileSourceFile(ProfileCallTreeNode node, ProfileSampleFilter profileFilter = null) {
    if (node.Function == null) {
      return false;
    }

    return await OpenProfileSourceFile(node.Function, profileFilter);
  }

  public async Task<bool> OpenProfileSourceFile(IRTextFunction function, ProfileSampleFilter profileFilter = null) {
    if (FindPanel(ToolPanelKind.Source) is SourceFilePanel panel) {
      if (function.HasSections) {
        await panel.LoadSourceFile(function.Sections[0], profileFilter);
      }
    }

    return true;
  }

  public async Task<bool> SelectProfileFunctionInPanel(ProfileCallTreeNode node, ToolPanelKind panelKind) {
    using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

    switch (panelKind) {
      case ToolPanelKind.CallTree: {
        var panel = FindAndActivatePanel(ToolPanelKind.CallTree) as CallTreePanel;
        panel?.SelectFunction(node);
        break;
      }
      case ToolPanelKind.FlameGraph: {
        var panel = FindAndActivatePanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
        panel?.SelectFunction(node);
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
      case ToolPanelKind.Section: {
        await SwitchActiveProfileFunction(node);
        break;
      }
      default: {
        throw new InvalidOperationException();
      }
    }

    return true;
  }

  public async Task<bool> SelectProfileFunctionInPanel(IRTextFunction func, ToolPanelKind panelKind) {
    using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

    switch (panelKind) {
      case ToolPanelKind.CallTree: {
        if (FindAndActivatePanel(ToolPanelKind.CallTree) is CallTreePanel panel) {
          panel.SelectFunction(func);
        }

        break;
      }
      case ToolPanelKind.FlameGraph: {
        if (FindAndActivatePanel(ToolPanelKind.FlameGraph) is FlameGraphPanel panel) {
          await panel.SelectFunction(func);
        }

        break;
      }
      case ToolPanelKind.Timeline: {
        if (FindAndActivatePanel(ToolPanelKind.Timeline) is TimelinePanel panel) {
          var nodeList = ProfileData.CallTree.GetSortedCallTreeNodes(func);

          if (nodeList is {Count: > 0}) {
            await SelectFunctionSamples(nodeList[0], panel);
          }
        }

        break;
      }
      default: {
        throw new InvalidOperationException();
      }
    }

    return true;
  }

  public async Task<bool> ProfileSampleRangeSelected(SampleTimeRangeInfo range) {
    using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

    //? TODO: If an event fires during the call tree/sample filtering,
    //? either ignore it or better run it after the filtering is done
    if (ProfileData.CallTree == null) {
      return false;
    }

    var funcs = await Task.Run(() =>
                                 FindFunctionsForSamples(range.StartSampleIndex, range.EndSampleIndex,
                                                         range.ThreadId, ProfileData));
    var sectionPanel = FindPanel(ToolPanelKind.Section) as SectionPanelPair;
    sectionPanel?.MarkFunctions(funcs.ToList());

    var nodes = await Task.Run(() =>
                                 FindCallTreeNodesForSamples(funcs, ProfileData));
    var panel = FindPanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
    panel?.MarkFunctions(nodes);
    return true;
  }

  public async Task<bool> ProfileFunctionSelected(ProfileCallTreeNode node, ToolPanelKind sourcePanelKind) {
    using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

    //? TODO: If an event fires during the call tree/sample filtering,
    //? either ignore it or better run it after the filtering is done
    if (ProfileData.CallTree == null) {
      return false;
    }

    if (FindPanel(ToolPanelKind.Timeline) is TimelinePanel panel) {
      //? TODO: Select only samples included in this call node,
      //? right now selects any instance of the func
      await SelectFunctionSamples(node, panel);
    }

    if (sourcePanelKind != ToolPanelKind.Section) {
      await SwitchActiveFunction(node.Function, false);
    }

    if (sourcePanelKind != ToolPanelKind.CallTree) {
      var callTreePanel = FindPanel(ToolPanelKind.CallTree) as CallTreePanel;
      callTreePanel?.SelectFunction(node);
    }

    if (sourcePanelKind != ToolPanelKind.CallerCallee) {
      if (FindPanel(ToolPanelKind.CallerCallee) is CallTreePanel callerCalleePanel) {
        //? TODO: Make it path-sensitive (show exact instance, not combined?)
        await callerCalleePanel?.DisplayProfileCallerCalleeTree(node.Function);
      }
    }

    if (sourcePanelKind != ToolPanelKind.FlameGraph) {
      if (FindPanel(ToolPanelKind.FlameGraph) is FlameGraphPanel flameGraphPanel) {
        await flameGraphPanel.SelectFunction(node.Function, false);
      }
    }

    return true;
  }

  public async Task<bool> MarkProfileFunction(ProfileCallTreeNode node, ToolPanelKind sourcePanelKind,
                                              HighlightingStyle style) {
    if (sourcePanelKind == ToolPanelKind.Timeline) {
      if (FindPanel(ToolPanelKind.Timeline) is TimelinePanel panel) {
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

    if (funcNodes is {Count: > 0}) {
      await ProfileFunctionSelected(funcNodes[0], sourcePanelKind);
    }

    return true;
  }

  public async Task<bool> ProfileSampleRangeDeselected() {
    using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();

    var panel = FindPanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
    panel?.ClearMarkedFunctions();

    var sectionPanel = FindPanel(ToolPanelKind.Section) as SectionPanelPair;
    sectionPanel?.ClearMarkedFunctions();
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

  private void UnloadProfilingDebugInfo() {
    if (ProfileData == null) {
      return;
    }

    // Free memory used by the debug info by unloading any objects
    // such as the PDB DIA reader using COM.
    Task.Run(() => {
      foreach ((string module, var debugInfo) in ProfileData.ModuleDebugInfo) {
        debugInfo.Unload();
      }
    });
  }

  private async void LoadProfileExecuted(object sender, ExecutedRoutedEventArgs e) {
    var window = new ProfileLoadWindow(this, false);
    window.Owner = this;
    bool? result = window.ShowDialog();

    if (result.HasValue && result.Value) {
      await SetupLoadedProfile();
    }
  }

  private async Task SetupLoadedProfile() {
    UpdateWindowTitle();
    SetApplicationProgress(true, double.NaN, "Loading profiling data");
    StartUIUpdate();

    await SetupPanels();
    await RefreshProfilingPanels();

    StopUIUpdate();
    ResetApplicationProgress();
    SetOptionalStatus(TimeSpan.FromSeconds(10), "Profile data loaded");
  }

  private async Task RefreshProfilingPanels() {
    var panelTasks = new List<Task>();

    if (FindPanel(ToolPanelKind.CallTree) is CallTreePanel panel) {
      panelTasks.Add(panel.DisplayProfileCallTree());
    }

    if (FindPanel(ToolPanelKind.FlameGraph) is FlameGraphPanel fgPanel) {
      panelTasks.Add(fgPanel.DisplayFlameGraph());
    }

    if (FindPanel(ToolPanelKind.Timeline) is TimelinePanel timelinePanel) {
      panelTasks.Add(timelinePanel.DisplayFlameGraph());
    }

    await Task.WhenAll(panelTasks.ToArray());
  }

  private async void RecordProfileExecuted(object sender, ExecutedRoutedEventArgs e) {
    var window = new ProfileLoadWindow(this, true);
    window.Owner = this;
    bool? result = window.ShowDialog();

    if (result.HasValue && result.Value) {
      await SetupLoadedProfile();
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

  private async Task SelectFunctionSamples(ProfileCallTreeNode node, TimelinePanel panel) {
    var threadSamples = await Task.Run(() => FindFunctionSamples(node, ProfileData));
    panel.SelectFunctionSamples(threadSamples);
  }

  private Dictionary<int, List<SampleIndex>>
    FindFunctionSamples(ProfileCallTreeNode node, ProfileData profile) {
    // Compute the list of samples associated with the function,
    // for each thread it was executed on.
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

      var currentNode = node;
      bool match = false;

      for (int k = 0; k < stack.StackFrames.Count; k++) {
        var stackFrame = stack.StackFrames[k];

        if (stackFrame.IsUnknown) {
          continue;
        }

        if (currentNode == null || currentNode.IsGroup) {
          // Mismatch along the call path leading to the function.
          match = false;
          break;
        }

        if (stackFrame.FrameDetails.Function.Equals(currentNode.Function)) {
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

  private HashSet<IRTextFunction> FindFunctionsForSamples(int sampleStartIndex, int sampleEndIndex, int threadId,
                                                          ProfileData profile) {
    // Compute the list of functions covered by the samples
    // on the specified thread or all threads.
    var funcSet = new HashSet<IRTextFunction>();

    //? TODO: If an event fires during the call tree/sample filtering,
    //? either ignore it or better run it after the filtering is done
    if (ProfileData.CallTree == null) {
      return funcSet;
    }

    //? TODO: Abstract parallel run chunks to take action per sample (ComputeFunctionProfile)
    //? Look at SearchAsync in SectionTextSearcher.cs for an example
    //? ConcurrentExclusiveSchedulerPair from DocSectionLoader is not the right solution
    //? + AreSectionsDifferentImpl
    for (int i = sampleStartIndex; i < sampleEndIndex; i++) {
      var (sample, stack) = profile.Samples[i];

      if (threadId != -1 && stack.Context.ThreadId != threadId) {
        continue;
      }

      foreach (var stackFrame in stack.StackFrames) {
        if (!stackFrame.IsUnknown) {
          funcSet.Add(stackFrame.FrameDetails.Function);
        }
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
      var nodes = profile.CallTree.GetCallTreeNodes(func);

      if (nodes == null)
        continue;

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

    return callNodes.ToList();
  }

  public async Task<IDebugInfoProvider> GetDebugInfoProvider(IRTextFunction function) {
    return await CompilerInfo.GetOrCreateDebugInfoProvider(function);
  }
}