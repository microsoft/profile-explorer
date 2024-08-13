// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ProfileExplorer.Core;

namespace ProfileExplorer.UI;

public partial class SectionPanelPair : ToolPanelControl {
  private bool diffModeEnabled_;

  public SectionPanelPair() {
    InitializeComponent();
    DiffPanel.Visibility = Visibility.Collapsed;
    MainGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
    MainGrid.ColumnDefinitions[2].Width = new GridLength(0, GridUnitType.Pixel);
    MainPanel.OpenSection += MainPanel_OpenSection;
    MainPanel.EnterDiffMode += MainPanel_EnterDiffMode;
    MainPanel.SyncDiffedDocumentsChanged += MainPanel_SyncDiffedDocumentsChanged;
    MainPanel.FunctionSwitched += DiffPanel_FunctionSwitched;
    MainPanel.SectionListScrollChanged += MainPanel_SectionListScrollChanged;
    MainPanel.DisplayCallGraph += MainPanel_DisplayCallGraph;

    DiffPanel.OpenSection += MainPanel_OpenSection;
    DiffPanel.EnterDiffMode += MainPanel_EnterDiffMode;
    DiffPanel.FunctionSwitched += DiffPanel_FunctionSwitched;
    DiffPanel.SectionListScrollChanged += MainPanel_SectionListScrollChanged;
    DiffPanel.SyncDiffedDocumentsChanged += MainPanel_SyncDiffedDocumentsChanged;
    DiffPanel.DisplayCallGraph += MainPanel_DisplayCallGraph;
  }

  public event EventHandler<OpenSectionEventArgs> OpenSection;
  public event EventHandler<DiffModeEventArgs> EnterDiffMode;
  public event EventHandler<bool> SyncDiffedDocumentsChanged;
  public event EventHandler<DisplayCallGraphEventArgs> DisplayCallGraph;

  public ICompilerInfoProvider CompilerInfo {
    get => MainPanel.CompilerInfo;
    set {
      MainPanel.CompilerInfo = value;
      DiffPanel.CompilerInfo = value;
    }
  }

  public bool HasAnnotatedSections => MainPanel.HasAnnotatedSections || DiffPanel.HasAnnotatedSections;
  public bool SyncDiffedDocuments => MainPanel.SyncDiffedDocuments;
  public IRTextSummary MainSummary => MainPanel.Summary;
  public IRTextSummary DiffSummary => DiffPanel.Summary;

  public string MainTitle {
    get => MainPanel.DocumentTitle;
    set => MainPanel.DocumentTitle = value;
  }

  public string DiffTitle {
    get => DiffPanel.DocumentTitle;
    set => DiffPanel.DocumentTitle = value;
  }

  public override ToolPanelKind PanelKind => ToolPanelKind.Section;
  public override bool SavesStateToFile => true;

  public override ISession Session {
    get => MainPanel.Session;
    set {
      MainPanel.Session = value;
      DiffPanel.Session = value;
    }
  }

  public async Task RefreshModuleSummaries() {
    await MainPanel.Update();
  }

  public async Task RefreshProfile() {
    await MainPanel.Update(true, false);
  }

  public async Task SetMainSummary(IRTextSummary summary, bool updateFunctionList = false) {
    if (MainPanel.Summary == summary && !updateFunctionList) {
      return;
    }

    MainPanel.Summary = summary;

    if (summary != null) {
      if (updateFunctionList) {
        await MainPanel.SetupFunctionList();
      }

      await Update();
    }
  }

  public async Task SetDiffSummary(IRTextSummary summary) {
    if (DiffPanel.Summary == summary) {
      return;
    }

    DiffPanel.Summary = summary;

    if (!diffModeEnabled_) {
      MainGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
      MainGrid.ColumnDefinitions[1].Width = new GridLength(2, GridUnitType.Pixel);
      MainGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
      MainPanel.IsDiffModeEnabled = true;
      DiffPanel.IsDiffModeEnabled = true;
      DiffPanel.Visibility = Visibility.Visible;
      MainPanel.OtherSummary = summary;

      DiffPanel.FunctionPartVisible = false;
      DiffPanel.OtherSummary = MainSummary;
      diffModeEnabled_ = true;
    }
    else if (summary == null) {
      MainGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
      MainGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
      MainGrid.ColumnDefinitions[2].Width = new GridLength(0, GridUnitType.Star);
      MainPanel.IsDiffModeEnabled = false;
      DiffPanel.IsDiffModeEnabled = false;
      MainPanel.IsFunctionListVisible = true;
      DiffPanel.Visibility = Visibility.Collapsed;

      // Restore original section list, without diff placeholders/annotations.
      if (MainPanel.Summary != null) { // If there was a prev. failure, may not be set up.
        MainPanel.Sections = MainPanel.CreateSectionsExtension(true);
      }

      diffModeEnabled_ = false;
    }

    if (summary != null) {
      await Update();
    }
  }

  public async Task Update() {
    await MainPanel.Update();
    await DiffPanel.Update();
  }

  public void AddModuleSummary(IRTextSummary summary) {
    MainPanel.AddModuleSummary(summary);
  }

  public async Task RefreshDocumentsDiffs() {
    if (MainPanel.CurrentFunction == null) {
      return;
    }

    await SwitchPanelDiffFunction(MainPanel.CurrentFunction, DiffPanel);
  }

  public void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations) {
    SelectSectionPanel(section).SetSectionAnnotationState(section, hasAnnotations);
  }

  public async Task SelectSection(IRTextSection section, bool handleProfiling = true, bool focus = true) {
    await SelectFunction(section.ParentFunction, handleProfiling);
    SelectSectionPanel(section).SelectSection(section, focus);
  }

  public async Task SelectFunction(IRTextFunction function, bool handleProfiling = true) {
    await MainPanel.SelectFunction(function, handleProfiling);

    if (DiffPanel.IsDiffModeEnabled) {
      await DiffPanel.SelectFunction(function, handleProfiling);
    }
  }

  public void DiffSelectedSection() {
    MainPanel.DiffSelectedSection();
  }

  public void SwitchToSection(IRTextSection section, IRDocumentHost targetDocument = null) {
    SelectSectionPanel(section).SwitchToSection(section, targetDocument);
  }

  public IRTextSection FindDiffDocumentSection(IRTextSection section) {
    if (!diffModeEnabled_) {
      return null;
    }

    var panel = SelectSectionPanel(section);
    var otherPanel = panel == MainPanel ? DiffPanel : MainPanel;
    int sectionIndex = panel.Sections.FindIndex(item => item.Section == section);

    if (sectionIndex == -1) {
      return null;
    }

    var otherSection = otherPanel.Sections[sectionIndex];

    if (!otherSection.IsPlaceholderDiff) {
      return otherSection.Section;
    }

    while (sectionIndex > 0) {
      sectionIndex--;
      otherSection = otherPanel.Sections[sectionIndex];

      if (!otherSection.IsPlaceholderDiff) {
        return otherSection.Section;
      }
    }

    return otherSection.Section;
  }

  public override void OnSessionStart() {
    MainPanel.OnSessionStart();
    DiffPanel.OnSessionStart();
  }

  public override void OnSessionEnd() {
    SetMainSummary(null).Wait();
    SetDiffSummary(null).Wait();
    MainPanel.SaveListViewColumnsState();
    MainPanel.OnSessionEnd(); //? TODO: Await, make OnSessionEnd async
    DiffPanel.OnSessionEnd();
  }

  public void EnterBinaryDisplayMode() {
    // Update UI for viewing a binary, similar to profiling mode.
    MainPanel.EnterBinaryDisplayMode();
    DiffPanel.EnterBinaryDisplayMode();
  }

  public async Task AnalyzeDocumentDiffs() {
    Trace.TraceInformation("AnalyzeDocumentDiffs: waiting");
    await MainPanel.WaitForStatistics();
    await DiffPanel.WaitForStatistics();
    Trace.TraceInformation("AnalyzeDocumentDiffs: start");

    var comparedSections = new List<(IRTextSection, IRTextSection)>();
    var comparedFuncts = new List<IRTextFunctionEx>();

    Session.SetApplicationProgress(true, double.NaN, "Computing document diffs");

    foreach (var function in MainSummary.Functions) {
      var functionEx = MainPanel.GetFunctionExtension(function);

      if (functionEx.IsDeletionDiff || functionEx.IsInsertionDiff) {
        functionEx.Statistics.ComputeDiff(functionEx.Statistics); // Consider as no diff.
        continue;
      }

      var otherFunctionEx = DiffPanel.GetFunctionExtension(function);
      var otherFunction = otherFunctionEx.Function;

      //? TODO: With multiple, which should be compared? Probably all of them, but expensive
      if (function.SectionCount > 0 && otherFunction.SectionCount > 0) {
        comparedSections.Add((function.Sections[0], otherFunction.Sections[0]));
        comparedFuncts.Add(functionEx);
      }

      functionEx.Statistics.ComputeDiff(otherFunctionEx.Statistics);
    }

    var results = await ComputeSectionIRDiffs(comparedSections);

    for (int i = 0; i < comparedFuncts.Count; i++) {
      if (results[i].HasDiffs) {
        comparedFuncts[i].FunctionDiffKind = DiffKind.Modification;
      }
    }

    MainPanel.AddStatisticsFunctionListColumns(true, " (D)", " delta", 55);
    MainPanel.RefreshFunctionList();

    Session.SetApplicationProgress(false, double.NaN);
    Trace.TraceInformation("AnalyzeDocumentDiffs: done");
  }

  public async Task ShowModuleReport() {
    await MainPanel.ShowModuleReport();
  }

  public void MarkFunctions(List<IRTextFunction> list) {
    MainPanel.MarkFunctions(list);
  }

  public void ClearMarkedFunctions() {
    MainPanel.ClearMarkedFunctions();
  }

  private void MainPanel_SectionListScrollChanged(object sender, double offset) {
    // When using the grid splitter to resize the left/right panels,
    // the event gets called for some reason with a 0 offset and
    // the current vertical offset gets reset.

    //? TODO: Ignoring 0 offset causes other scroll issues, likely fix is to ignore
    //? the event between grid splitter mouse down and up events.

    if (SyncDiffedDocuments) {
      var otherPanel = PickOtherPanel(sender);

      if (otherPanel.Summary != null && diffModeEnabled_) {
        otherPanel.ScrollSectionList(offset);
      }
    }
  }

  private SectionPanel PickOtherPanel(object sender) {
    var panel = sender as SectionPanel;
    return panel == MainPanel ? DiffPanel : MainPanel;
  }

  private async void DiffPanel_FunctionSwitched(object sender, IRTextFunction func) {
    var otherPanel = PickOtherPanel(sender);

    if (otherPanel.Summary != null && diffModeEnabled_) {
      await SwitchPanelDiffFunction(func, otherPanel);
    }
  }

  private async Task SwitchPanelDiffFunction(IRTextFunction func, SectionPanel otherPanel) {
    var otherFunc = otherPanel.Summary.FindFunction(func.Name);

    if (otherFunc != null) {
      await otherPanel.SelectFunction(otherFunc);
    }

    await ComputePanelSectionDiff();
  }

  private void MainPanel_EnterDiffMode(object sender, DiffModeEventArgs e) {
    if (e.IsWithOtherDocument) {
      var panel = sender as SectionPanel;
      var otherSection = FindDiffDocumentSection(e.Left.Section);

      if (panel == MainPanel) {
        e.Left.OpenKind = OpenSectionKind.ReplaceLeft;
        e.Right = new OpenSectionEventArgs(otherSection, OpenSectionKind.ReplaceRight);
      }
      else {
        e.Right = e.Left;
        e.Right.OpenKind = OpenSectionKind.ReplaceRight;
        e.Left = new OpenSectionEventArgs(otherSection, OpenSectionKind.ReplaceLeft);
      }
    }

    EnterDiffMode?.Invoke(sender, e);
  }

  private void MainPanel_SyncDiffedDocumentsChanged(object sender, bool e) {
    SyncDiffedDocumentsChanged?.Invoke(this, e);
    PickOtherPanel(sender).SyncDiffedDocuments = e;
  }

  private void MainPanel_DisplayCallGraph(object sender, DisplayCallGraphEventArgs e) {
    DisplayCallGraph?.Invoke(this, e);
  }

  private void MainPanel_OpenSection(object sender, OpenSectionEventArgs e) {
    if (e.OpenKind == OpenSectionKind.ReplaceCurrent && diffModeEnabled_) {
      if (sender == MainPanel) {
        e.OpenKind = OpenSectionKind.ReplaceLeft;
      }
      else if (sender == DiffPanel) {
        e.OpenKind = OpenSectionKind.ReplaceRight;
      }
    }

    OpenSection?.Invoke(sender, e);
  }

  private async Task ComputePanelSectionDiff() {
    var baseExtList = MainPanel.CreateSectionsExtension();
    var diffExtList = DiffPanel.CreateSectionsExtension();
    var (baseList, diffList) = ComputeSectionNameListDiff(baseExtList, diffExtList);
    MainPanel.Sections = baseList;
    DiffPanel.Sections = diffList;

    var results = await ComputeSectionIRDiffs(baseList, diffList);
    DocumentDiffResult firstDiffResult = null;

    foreach (var result in results) {
      if (result.HasDiffs) {
        var baseSection = MainPanel.GetSectionExtension(result.LeftSection);
        var diffSection = DiffPanel.GetSectionExtension(result.RightSection);
        baseSection.SectionDiffKind = DiffKind.Modification;
        diffSection.SectionDiffKind = DiffKind.Modification;
        firstDiffResult ??= result;
      }
    }

    // Scroll to the first diff section.
    if (firstDiffResult != null) {
      // Force scrolling to happen after other layout updates,
      // otherwise the section lists scroll back to offset 0
      // on the next layout update, looks like a WPF bug...
      Dispatcher.Invoke(() => {
        MainPanel.SelectSection(firstDiffResult.LeftSection, false);
        DiffPanel.SelectSection(firstDiffResult.RightSection, false);
      }, DispatcherPriority.Background);
    }
  }

  private SectionPanel SelectSectionPanel(IRTextSection section) {
    var summary = section.ParentFunction.ParentSummary;

    if (MainPanel.HasSummary(summary)) {
      return MainPanel;
    }

    if (DiffPanel.HasSummary(summary)) {
      return DiffPanel;
    }

    return null;
  }

  private (List<IRTextSectionEx>, List<IRTextSectionEx>) ComputeSectionNameListDiff(
    List<IRTextSectionEx> baseList, List<IRTextSectionEx> diffList) {
    int[,] m = new int[baseList.Count + 1, diffList.Count + 1];
    int x;
    int y;

    for (x = 1; x <= baseList.Count; x++) {
      for (y = 1; y <= diffList.Count; y++) {
        var baseSection = baseList[x - 1];
        var diffSection = diffList[y - 1];

        if (baseSection.Name == diffSection.Name) {
          m[x, y] = m[x - 1, y - 1] + 1;
        }
        else {
          m[x, y] = Math.Max(m[x, y - 1], m[x - 1, y]);
        }
      }
    }

    int index = 0;
    var newBaseList = new List<IRTextSectionEx>(baseList.Count);
    var newDiffList = new List<IRTextSectionEx>(diffList.Count);
    x = baseList.Count;
    y = diffList.Count;

    while (!(x == 0 && y == 0)) {
      if (x > 0 && y > 0) {
        var baseSection = baseList[x - 1];
        var diffSection = diffList[y - 1];

        if (baseSection.Name == diffSection.Name) {
          // Found in both diff and base.
          newBaseList.Add(baseSection);
          newDiffList.Add(diffSection);
          x--;
          y--;
          continue;
        }
      }

      if (y > 0 && (x == 0 || m[x, y - 1] >= m[x - 1, y])) {
        // Found in diff, missing in base.
        var diffSection = diffList[y - 1];

        newDiffList.Add(
          new IRTextSectionEx(diffSection.Section, DiffKind.Insertion,
                              diffSection.Name, index++));

        var placeholderSection = new IRTextSection(diffSection.Section.ParentFunction, "", IRPassOutput.Empty);
        newBaseList.Add(new IRTextSectionEx(placeholderSection, DiffKind.Placeholder, "", index++));
        y--;
      }
      else if (x > 0 && (y == 0 || m[x, y - 1] < m[x - 1, y])) {
        // Not found in diff, removed from base.
        var baseSection = baseList[x - 1];

        newBaseList.Add(new IRTextSectionEx(baseSection.Section, DiffKind.Deletion, baseSection.Name, index++));
        var placeholderSection = new IRTextSection(baseSection.Section.ParentFunction, "", IRPassOutput.Empty);
        newDiffList.Add(new IRTextSectionEx(placeholderSection, DiffKind.Placeholder, "", index++));
        x--;
      }
      else {
        Debug.Assert(false);
      }
    }

    newBaseList.Reverse();
    newDiffList.Reverse();
    return (newBaseList, newDiffList);
  }

  private async Task<List<DocumentDiffResult>> ComputeSectionIRDiffs(
    List<IRTextSectionEx> baseSections, List<IRTextSectionEx> diffSections) {
    var comparedSections = new List<(IRTextSection, IRTextSection)>();

    for (int i = 0; i < baseSections.Count; i++) {
      if (baseSections[i].SectionDiffKind == DiffKind.None &&
          diffSections[i].SectionDiffKind == DiffKind.None) {
        comparedSections.Add((baseSections[i].Section, diffSections[i].Section));
      }
    }

    //? TODO: Pass the LoadedDocument to the panel, not Summary.
    return await ComputeSectionIRDiffs(comparedSections);
  }

  private async Task<List<DocumentDiffResult>> ComputeSectionIRDiffs(
    List<(IRTextSection, IRTextSection)> comparedSections) {
    var baseLoader = Session.SessionState.FindLoadedDocument(MainPanel.Summary).Loader;
    var diffLoader = Session.SessionState.FindLoadedDocument(DiffPanel.Summary).Loader;

    var diffBuilder = new DocumentDiffBuilder(App.Settings.DiffSettings);
    var cancelableTask = new CancelableTask();
    return await diffBuilder.AreSectionsDifferent(comparedSections, baseLoader, diffLoader,
                                                  Session.CompilerInfo, true, cancelableTask);
  }

  public async Task UpdateMarkedFunctions(bool externalCall) {
    await MainPanel.UpdateMarkedFunctions(externalCall);
    await DiffPanel.UpdateMarkedFunctions(externalCall);
  }
}