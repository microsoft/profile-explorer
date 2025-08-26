﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProfileExplorer.Core;
using ProfileExplorer.UI.Compilers;
using ProfileExplorer.UI.Document;
using ProfileExplorer.UI.OptionsPanels;
using ProfileExplorer.UI.Profile;
using ProfileExplorer.UI.Windows;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.Processing;
using ProfileExplorer.Core.Profile.ETW;
using ProfileExplorer.Core.Profile.CallTree;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Profile.Timeline;

namespace ProfileExplorer.UI;

public partial class MainWindow : Window, IUISession {
  private CancelableTaskInstance updateProfileTask_ = new();
  private ProfileData.ProcessingResult allThreadsProfile_;
  private ProfileFilterState profileFilter_;
  private OptionsPanelHostPopup markingOptionsPanelPopup_;
  public bool IsProfileSession => ProfileData != null;
  public bool HasEnabledMarkedFunctions =>
    MarkingSettings.HasEnabledFunctionMarkings;
  public bool HasEnabledMarkedModules =>
    MarkingSettings.HasEnabledModuleMarkings;
  public ProfileData ProfileData => sessionState_?.ProfileData;

  public ProfileFilterState ProfileFilter {
    get => profileFilter_;
    set => profileFilter_ = value;
  }

  public async Task<bool> LoadProfileData(string profileFilePath, List<int> processIds,
                                          ProfileDataProviderOptions options,
                                          SymbolFileSourceSettings symbolSettings,
                                          ProfileDataReport report,
                                          ProfileLoadProgressHandler progressCallback,
                                          CancelableTask cancelableTask) {
    Trace.WriteLine($"LoadProfileData: Starting profile data loading for {profileFilePath}");
    var sw = Stopwatch.StartNew();
    using var provider = new ETWProfileDataProvider(this);
    var result = await provider.LoadTraceAsync(profileFilePath, processIds,
                                               options, symbolSettings,
                                               report, progressCallback, cancelableTask);

    Trace.WriteLine($"LoadProfileData: LoadTraceAsync completed, result is {(result != null ? "NOT NULL" : "NULL")}");
    Trace.WriteLine($"LoadProfileData: IsSessionStarted = {IsSessionStarted}");
    
    if (!IsSessionStarted) {
      Trace.WriteLine($"LoadProfileData: ERROR - Session not started, returning false");
      return false;
    }

    if (result != null) {
      result.Report = report;
      sessionState_.ProfileData = result;
      UpdateWindowTitle();
      UnloadProfilingDebugInfo();
      Trace.WriteLine($"LoadProfileData: Successfully set profile data in session");
    }
    else {
      Trace.WriteLine($"LoadProfileData: ERROR - LoadTraceAsync returned null result");
    }

    Trace.WriteLine($"Done profile load and setup: {sw}, {sw.ElapsedMilliseconds} ms");
    Trace.Flush();
    return result != null;
  }

  public async Task<bool> LoadProfileData(RawProfileData data, List<int> processIds,
                                          ProfileDataProviderOptions options,
                                          SymbolFileSourceSettings symbolSettings,
                                          ProfileDataReport report,
                                          ProfileLoadProgressHandler progressCallback,
                                          CancelableTask cancelableTask) {
    var sw = Stopwatch.StartNew();
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

    Trace.WriteLine($"Done profile load and setup: {sw}, {sw.ElapsedMilliseconds} ms");
    Trace.Flush();
    return result != null;
  }

  public async Task<bool> FilterProfileSamples(ProfileFilterState state) {
    using var cancelableTask = await updateProfileTask_.CancelCurrentAndCreateTaskAsync();

    SetApplicationProgress(true, double.NaN, "Filtering profiling data");
    StartUIUpdate();

    // Update the active profile UI.
    SetActiveProfileFilter(state);

    var totalSw = Stopwatch.StartNew();
    Trace.WriteLine("--------------------------------------------------------\n");
    Trace.WriteLine($"Profile filter {state.Filter}, samples {ProfileData.Samples.Count}");

    var filterSw = Stopwatch.StartNew();
    ProfileData.ProcessingResult result = null; // Profile before filtering.

    if (state.Filter.IncludesAll && allThreadsProfile_ != null) {
      // This speeds up going back to the unfiltered profile.
      Trace.WriteLine("Restore main profile");
      result = ProfileData.RestorePreviousProfile(allThreadsProfile_);
    }
    else {
      Trace.WriteLine("Compute new profile");
      result = await Task.Run(() => ProfileData.FilterFunctionProfile(state.Filter));
    }

    if (result.Filter.IncludesAll) {
      Trace.WriteLine("Save main profile");
      allThreadsProfile_ = result;
    }

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
    await FilterProfileSamples(new ProfileFilterState());
    await ProfileSampleRangeDeselected();
    return true;
  }

  public async Task<bool> OpenProfileFunction(ProfileCallTreeNode node, OpenSectionKind openMode,
                                              ProfileSampleFilter instanceFilter = null,
                                              IRDocumentHost targetDocument = null) {
    if (node is not {HasFunction: true}) {
      return false;
    }

    return await OpenProfileFunction(node.Function, openMode, instanceFilter, targetDocument);
  }

  public async Task<bool> OpenProfileFunction(IRTextFunction function, OpenSectionKind openMode,
                                              ProfileSampleFilter instanceFilter = null,
                                              IRDocumentHost targetDocument = null) {
    var args = new OpenSectionEventArgs(function.Sections[0], openMode, targetDocument);
    var docHost = await SwitchDocumentSectionAsync(args);

    if (instanceFilter != null) {
      await docHost.SwitchProfileInstanceAsync(instanceFilter);
    }

    return true;
  }

  public async Task<bool> SwitchActiveProfileFunction(ProfileCallTreeNode node) {
    if (node is not {HasFunction: true}) {
      return false;
    }

    await SwitchActiveFunction(node.Function);
    return true;
  }

  public async Task<bool> OpenProfileSourceFile(ProfileCallTreeNode node, ProfileSampleFilter profileFilter = null) {
    if (node is not {HasFunction: true}) {
      return false;
    }

    return await OpenProfileSourceFile(node.Function, profileFilter);
  }

  public async Task<bool> OpenProfileSourceFile(IRTextFunction function, ProfileSampleFilter profileFilter = null) {
    if (FindPanel(ToolPanelKind.Source) is not SourceFilePanel panel) {
      panel = await ShowPanel(ToolPanelKind.Source) as SourceFilePanel;
    }

    if (panel != null && function.HasSections) {
      await panel.LoadSourceFile(function.Sections[0], profileFilter);
    }

    return true;
  }

  public async Task<bool> SelectProfileFunctionInPanel(ProfileCallTreeNode node, ToolPanelKind panelKind) {
    using var cancelableTask = await updateProfileTask_.CancelCurrentAndCreateTaskAsync();

    switch (panelKind) {
      case ToolPanelKind.CallTree: {
        if (FindAndActivatePanel(ToolPanelKind.CallTree) is not CallTreePanel panel) {
          panel = await ShowPanel(ToolPanelKind.CallTree) as CallTreePanel;
        }

        panel?.SelectFunction(node);
        break;
      }
      case ToolPanelKind.FlameGraph: {
        if (FindAndActivatePanel(ToolPanelKind.FlameGraph) is not FlameGraphPanel panel) {
          panel = await ShowPanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
        }

        panel?.SelectFunction(node);
        break;
      }
      case ToolPanelKind.Timeline: {
        if (FindAndActivatePanel(ToolPanelKind.Timeline) is not TimelinePanel panel) {
          panel = await ShowPanel(ToolPanelKind.Timeline) as TimelinePanel;
        }

        if (panel != null) {
          await SelectFunctionSamples(node, panel);
        }

        break;
      }
      case ToolPanelKind.Source: {
        await OpenProfileSourceFile(node);
        break;
      }
      case ToolPanelKind.Section: {
        if (FindAndActivatePanel(ToolPanelKind.Section) is not SectionPanel panel) {
          await ShowPanel(ToolPanelKind.Section);
        }

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
    using var cancelableTask = await updateProfileTask_.CancelCurrentAndCreateTaskAsync();

    switch (panelKind) {
      case ToolPanelKind.CallTree: {
        if (FindAndActivatePanel(ToolPanelKind.CallTree) is not CallTreePanel panel) {
          panel = await ShowPanel(ToolPanelKind.CallTree) as CallTreePanel;
        }

        panel?.SelectFunction(func);
        break;
      }
      case ToolPanelKind.FlameGraph: {
        if (FindAndActivatePanel(ToolPanelKind.FlameGraph) is not FlameGraphPanel panel) {
          panel = await ShowPanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
        }

        if (panel != null) {
          await panel.SelectFunction(func);
        }

        break;
      }
      case ToolPanelKind.Timeline: {
        if (FindAndActivatePanel(ToolPanelKind.Timeline) is not TimelinePanel panel) {
          panel = await ShowPanel(ToolPanelKind.Timeline) as TimelinePanel;
        }

        if (panel != null) {
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
    using var cancelableTask = await updateProfileTask_.CancelCurrentAndCreateTaskAsync();

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
    using var cancelableTask = await updateProfileTask_.CancelCurrentAndCreateTaskAsync();

    //? TODO: If an event fires during the call tree/sample filtering,
    //? either ignore it or better run it after the filtering is done
    if (ProfileData.CallTree == null) {
      return false;
    }

    if (sourcePanelKind != ToolPanelKind.Section) {
      await SwitchActiveFunction(node.Function, false);
    }

    if (sourcePanelKind != ToolPanelKind.FlameGraph) {
      if (FindPanel(ToolPanelKind.FlameGraph) is FlameGraphPanel flameGraphPanel) {
        await flameGraphPanel.SelectFunction(node.Function, false);
      }
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

    if (FindPanel(ToolPanelKind.Timeline) is TimelinePanel panel) {
      //? TODO: Select only samples included in this call node,
      //? right now selects any instance of the func
      await SelectFunctionSamples(node, panel);
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
    using var cancelableTask = await updateProfileTask_.CancelCurrentAndCreateTaskAsync();

    var panel = FindPanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
    panel?.ClearMarkedFunctions();

    var sectionPanel = FindPanel(ToolPanelKind.Section) as SectionPanelPair;
    sectionPanel?.ClearMarkedFunctions();
    return true;
  }

  public async Task<bool> ProfileFunctionDeselected() {
    using var cancelableTask = await updateProfileTask_.CancelCurrentAndCreateTaskAsync();

    var panel = FindPanel(ToolPanelKind.Timeline) as TimelinePanel;
    panel?.ClearSelectedFunctionSamples();

    var callerCalleePanel = FindPanel(ToolPanelKind.CallerCallee) as CallTreePanel;
    callerCalleePanel?.Reset();
    return true;
  }

  public async Task<IDebugInfoProvider> GetDebugInfoProvider(IRTextFunction function) {
    return await CompilerInfo.GetOrCreateDebugInfoProvider(function).ConfigureAwait(false);
  }

  public async Task<bool> FunctionMarkingChanged(ToolPanelKind sourcePanelKind) {
    if (sourcePanelKind != ToolPanelKind.Section) {
      if (FindPanel(ToolPanelKind.Section) is SectionPanelPair panel) {
        await panel.UpdateMarkedFunctions(true);
      }
    }

    if (sourcePanelKind != ToolPanelKind.FlameGraph) {
      var panel = FindPanel(ToolPanelKind.FlameGraph) as FlameGraphPanel;
      panel?.UpdateMarkedFunctions(true);
    }

    if (sourcePanelKind != ToolPanelKind.CallTree) {
      if (FindPanel(ToolPanelKind.CallTree) is CallTreePanel panel) {
        await panel.UpdateMarkedFunctions(true);
      }
    }

    if (sourcePanelKind != ToolPanelKind.CallerCallee) {
      if (FindPanel(ToolPanelKind.CallerCallee) is CallTreePanel panel) {
        await panel.UpdateMarkedFunctions(true);
      }
    }

    // Also update any detached profiling popup.
    foreach (var popup in detachedPanels_) {
      if (popup is CallTreeNodePopup nodePopup) {
        nodePopup.UpdateMarkedFunctions();
      }
    }

    OnPropertyChanged(nameof(HasEnabledMarkedModules));
    OnPropertyChanged(nameof(HasEnabledMarkedFunctions));
    return true;
  }

  private void SetActiveProfileFilter(ProfileFilterState state) {
    ProfileFilter = state;
    ProfileFilterStateHost.DataContext = null;
    ProfileFilterStateHost.DataContext = state;
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
    await LoadProfile();
  }

  private async Task LoadProfile() {
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
    ProfileControlsVisible = true;

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
    await RecordProfile();
  }

  private async Task RecordProfile() {
    var window = new ProfileLoadWindow(this, true);
    window.Owner = this;
    bool? result = window.ShowDialog();

    if (result.HasValue && result.Value) {
      await SetupLoadedProfile();
    }
  }

  private void CanExecuteProfileCommand(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = IsSessionStarted && sessionState_.ProfileData != null;
    e.Handled = true;
  }

  private void CanExecuteLoadProfileCommand(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = !IsSessionStarted || sessionState_.ProfileData == null;
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
    return FunctionSamplesProcessor.Compute(node, profile, new ProfileSampleFilter());
  }

  private HashSet<IRTextFunction> FindFunctionsForSamples(int sampleStartIndex, int sampleEndIndex, int threadId,
                                                          ProfileData profile) {
    var filter = new ProfileSampleFilter();
    filter.TimeRange = new SampleTimeRangeInfo(TimeSpan.Zero, TimeSpan.Zero,
                                               sampleStartIndex, sampleEndIndex, threadId);

    if (threadId != -1) {
      filter.AddThread(threadId);
    }

    return FunctionsForSamplesProcessor.Compute(filter, profile);
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

  private void RemoveProfileTimeRangeButton_Click(object sender, RoutedEventArgs e) {
    ProfileFilter?.RemoveTimeRangeFilter?.Invoke();
  }

  private void RemoveProfileThreadButton_Click(object sender, RoutedEventArgs e) {
    ProfileFilter?.RemoveThreadFilter?.Invoke();
  }

  private void RemoveProfileAllFiltersButton_Click(object sender, RoutedEventArgs e) {
    ProfileFilter?.RemoveAllFilters?.Invoke();
  }

  private async void ClearFunctionsButton_Click(object sender, RoutedEventArgs e) {
    MarkingSettings.FunctionColors.Clear();
    await ReloadMarkingSettings();
  }

  private async void ClearModulesButton_Click(object sender, RoutedEventArgs e) {
    MarkingSettings.ModuleColors.Clear();
    await ReloadMarkingSettings();
  }

  private async void MarkingMenu_OnSubmenuOpened(object sender, RoutedEventArgs e) {
    // Add the saved markings menu items.
    CreateSavedMarkingMenu(SwitchMarkingsMenu, async markingSet => {
      MarkingSettings.SwitchMarkingSet(markingSet);
      await ReloadMarkingSettings();
    });

    CreateSavedMarkingMenu(AppendMarkingsMenu, async markingSet => {
      MarkingSettings.AppendMarkingSet(markingSet);
      await ReloadMarkingSettings();
    });

    // Add the built-in function markings to the menu,
    // if not already added, after the title "builtin markings" title.
    int insertionIndex = 1;

    foreach (object item in MarkingMenu.Items) {
      if (item is MenuItem menuItem &&
          menuItem.Name == "BuiltinMarkingsMenu") {
        break;
      }

      insertionIndex++;
    }

    if (MarkingMenu.Items[insertionIndex] is not MenuItem stopMenuItem ||
        !stopMenuItem.Tag.Equals("BuiltinMarkingsMenuEnd")) {
      return; // Already populated.
    }

    var builtinMarkings = MarkingSettings.BuiltinMarkingCategories;

    foreach (var markingSet in builtinMarkings.FunctionColors) {
      var item = new MenuItem {
        Header = markingSet.Title,
        ToolTip = DocumentUtils.FormatLongFunctionName(markingSet.Name),
        Tag = markingSet
      };

      var colorSelector = new ColorSelector();
      var selectorItem = new MenuItem {
        Header = colorSelector,
        Tag = markingSet
      };

      colorSelector.ColorSelected += async (o, args) => {
        var style = markingSet.CloneWithNewColor(args.SelectedColor);
        MarkingSettings.UseFunctionColors = true;
        MarkingSettings.AddFunctionColor(style);
        await ReloadMarkingSettings();
      };

      item.Items.Add(selectorItem);
      MarkingMenu.Items.Insert(insertionIndex, item);
      insertionIndex++;
    }
  }

  private void CreateSavedMarkingMenu(MenuItem menu, Action<FunctionMarkingSet> action) {
    menu.Items.Clear();

    foreach (var markingSet in MarkingSettings.SavedSets) {
      string tooltip = $"Function markings: {markingSet.FunctionColors.Count}";
      tooltip += $"\nModule markings: {markingSet.ModuleColors.Count}";
      tooltip += "\nRight-click to remove marking set";

      var item = new MenuItem {
        Header = markingSet.Title,
        ToolTip = tooltip,
        Tag = markingSet
      };

      item.Click += (sender, args) => action(markingSet);
      item.PreviewMouseRightButtonDown += (sender, args) => {
        MarkingSettings.RemoveMarkingSet(markingSet);
        menu.IsSubmenuOpen = false;
      };

      menu.Items.Add(item);
    }
  }

  private async Task ReloadMarkingSettings() {
    // Notify all panels about the marking changes.
    await FunctionMarkingChanged(ToolPanelKind.Other);
  }

  private void SaveMarkingsMenuItem_OnClick(object sender, RoutedEventArgs e) {
    TextInputWindow input = new("Save marked functions/modules", "Saved marking set name:", "Save", "Cancel");

    if (input.Show(out string result, true)) {
      MarkingSettings.SaveCurrentMarkingSet(result);
    }
  }

  private async void ImportMarkingsMenuItem_OnClick(object sender, RoutedEventArgs e) {
    if (MarkingSettings.ImportMarkings(this)) {
      await ReloadMarkingSettings();
    }
  }

  private void ExportMarkingsMenuItem_OnClick(object sender, RoutedEventArgs e) {
    MarkingSettings.ExportMarkings(this);
  }

  private void EditMarkingsMenu_OnClick(object sender, RoutedEventArgs e) {
    string filePath = App.GetFunctionMarkingsFilePath(compilerInfo_.CompilerIRName);
    Utils.OpenExternalFile(filePath);
  }

  private async void CategoriesMenu_OnSubmenuOpened(object sender, RoutedEventArgs e) {
    if (e.OriginalSource is MenuItem menuItem &&
        menuItem.Tag != null) {
      return;
    }

    await ProfilingUtils.CreateFunctionsCategoriesMenu(CategoriesMenu, async (o, args) => {
                                                         if (o is MenuItem menuItem &&
                                                             menuItem.Tag is IRTextFunction func) {
                                                           await SwitchActiveFunction(func);
                                                         }
                                                       }, null,
                                                       MarkingSettings, this);
  }

  private async void FunctionMenu_OnSubmenuOpened(object sender, RoutedEventArgs e) {
    await ProfilingUtils.PopulateMarkedFunctionsMenu(FunctionMenu, MarkingSettings, this,
                                                     e.OriginalSource, ReloadMarkingSettings);
  }

  private async void ModuleMenu_OnSubmenuOpened(object sender, RoutedEventArgs e) {
    await ProfilingUtils.PopulateMarkedModulesMenu(ModuleMenu, MarkingSettings, this,
                                                   e.OriginalSource, ReloadMarkingSettings);
  }

  private async void CopyOverviewMenu_OnClick(object sender, RoutedEventArgs e) {
    (string html, string plaintext) = await ExportProfilingReportAsHtml();
    Utils.CopyHtmlToClipboard(html, plaintext);
  }

  private async Task<(string Html, string Plaintext)>
    ExportProfilingReportAsHtml() {
    var markings = App.Settings.MarkingSettings.BuiltinMarkingCategories.FunctionColors;
    var markingCategoryList =
      await Task.Run(() => ProfilingUtils.CollectMarkedFunctions(markings, false, this));
    return ProfilingExporting.ExportProfilingReportAsHtml(markingCategoryList, this, true, 20);
  }

  private async void ExportOverviewHtmlMenu_OnClick(object sender, RoutedEventArgs e) {
    string path = Utils.ShowSaveFileDialog("HTML file|*.html", "*.html|All Files|*.*");
    bool success = true;

    if (!string.IsNullOrEmpty(path)) {
      try {
        (string html, _) = await ExportProfilingReportAsHtml();
        await File.WriteAllTextAsync(path, html);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save report to {path}: {ex.Message}");
        success = false;
      }

      if (!success) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Failed to save profiling report to {path}", "Profile Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  private async void ExportOverviewMarkdownMenu_OnClick(object sender, RoutedEventArgs e) {
    string path = Utils.ShowSaveFileDialog("Markdown file|*.md", "*.md|All Files|*.*");
    bool success = true;

    if (!string.IsNullOrEmpty(path)) {
      try {
        (_, string plaintext) = await ExportProfilingReportAsHtml();
        await File.WriteAllTextAsync(path, plaintext);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save report to {path}: {ex.Message}");
        success = false;
      }

      if (!success) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Failed to save profiling report to {path}", "Profile Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  private async void CopyMarkedFunctionMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.CopyFunctionMarkingsAsHtml(this);
  }

  private async void ExportMarkedFunctionsHtmlMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.ExportFunctionMarkingsAsHtmlFile(this);
  }

  private async void ExportMarkedFunctionsMarkdownMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.ExportFunctionMarkingsAsMarkdownFile(this);
  }

  private async void CopyMarkedModulesMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.CopyModuleMarkingsAsHtml(this);
  }

  private async void ExportMarkedModulesHtmlMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.ExportModuleMarkingsAsHtmlFile(this);
  }

  private async void ExportMarkedModulesMarkdownMenu_OnClick(object sender, RoutedEventArgs e) {
    await ProfilingExporting.ExportModuleMarkingsAsMarkdownFile(this);
  }

  private void MarkingSettingsButton_Click(object sender, RoutedEventArgs e) {
    if (markingOptionsPanelPopup_ != null) {
      markingOptionsPanelPopup_.ClosePopup();
      markingOptionsPanelPopup_ = null;
      return;
    }

    var positionAdjustment = new Point(330, MainMenu.ActualHeight + 1);
    markingOptionsPanelPopup_ = OptionsPanelHostPopup.Create<FunctionMarkingOptionsPanel, FunctionMarkingSettings>(
      MarkingSettings.Clone(), MainGrid, this,
      async (newSettings, commit) => {
        if (!newSettings.Equals(MarkingSettings)) {
          App.Settings.MarkingSettings = newSettings;
          await ReloadMarkingSettings();

          if (commit) {
            App.SaveApplicationSettings();
          }

          return newSettings.Clone();
        }

        return null;
      },
      () => markingOptionsPanelPopup_ = null,
      positionAdjustment, true);
  }
}