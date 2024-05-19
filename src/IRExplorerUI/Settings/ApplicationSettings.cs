// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using IRExplorerUI.Query;
using IRExplorerUI.Settings;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class ApplicationSettings {
  [ProtoMember(1)]
  public List<string> RecentFiles { get; set; }
  [ProtoMember(2)]
  public bool AutoReloadDocument { get; set; }
  [ProtoMember(3)]
  public string MainWindowPlacement { get; set; }
  [ProtoMember(4)]
  public GeneralSettings GeneralSettings { get; set; }
  [ProtoMember(5)]
  public List<Tuple<string, string>> RecentComparedFiles { get; set; }
  [ProtoMember(6)]
  public DocumentSettings DocumentSettings { get; set; }
  [ProtoMember(7)]
  public FlowGraphSettings FlowGraphSettings { get; set; }
  [ProtoMember(8)]
  public ExpressionGraphSettings ExpressionGraphSettings { get; set; }
  [ProtoMember(9)]
  public RemarkSettings RemarkSettings { get; set; }
  [ProtoMember(10)]
  public DiffSettings DiffSettings { get; set; }
  [ProtoMember(11)]
  public SectionSettings SectionSettings { get; set; }
  [ProtoMember(12)]
  public List<string> RecentTextSearches { get; set; }
  [ProtoMember(13)]
  public Dictionary<Guid, byte[]> FunctionTaskOptions { get; set; }
  [ProtoMember(14)]
  public string DefaultCompilerIR { get; set; }
  [ProtoMember(15)]
  public IRMode DefaultIRMode { get; set; }
  [ProtoMember(16)]
  public ProfileDataProviderOptions ProfileOptions { get; set; }
  [ProtoMember(17)]
  public SymbolFileSourceSettings SymbolSettings { get; set; }
  [ProtoMember(18)]
  public CallTreeSettings CallTreeSettings { get; set; }
  [ProtoMember(19)]
  public CallTreeSettings CallerCalleeSettings { get; set; }
  [ProtoMember(20)]
  public FlameGraphSettings FlameGraphSettings { get; set; }
  [ProtoMember(21)]
  public WorkspaceSettings WorkspaceOptions { get; set; }
  [ProtoMember(22)]
  public SourceFileSettings SourceFileSettings { get; set; }
  [ProtoMember(25)]
  public TimelineSettings TimelineSettings { get; set; }
  [ProtoMember(26)]
  public CallTreeNodeSettings CallTreeNodeSettings { get; set; }
  [ProtoMember(27)]
  public PreviewPopupSettings PreviewPopupSettings { get; set; }
  [ProtoMember(28)]
  public Dictionary<ToolPanelKind, PreviewPopupSettings> ElementPreviewPopupSettings { get; set; }
  [ProtoMember(29)]
  public FunctionMarkingSettings MarkingSettings { get; set; }
  [ProtoMember(30)]
  public HashSet<OptionValueId> KnownOptions { get; set; }

  public ApplicationSettings() {
    Reset();
  }

  public void Reset() {
    InitializeReferenceMembers();

    GeneralSettings.Reset();
    DocumentSettings.Reset();
    FlowGraphSettings.Reset();
    ExpressionGraphSettings.Reset();
    RemarkSettings.Reset();
    DiffSettings.Reset();
    SectionSettings.Reset();
    CallTreeSettings.Reset();
    CallerCalleeSettings.Reset();
    FlameGraphSettings.Reset();
    SourceFileSettings.Reset();
    TimelineSettings.Reset();
    CallTreeNodeSettings.Reset();
    PreviewPopupSettings.Reset();
    MarkingSettings.Reset();
    ElementPreviewPopupSettings.Clear();
    KnownOptions.Clear();
    AutoReloadDocument = true;
  }

  public void AddRecentFile(string path) {
    // Keep at most N recent files, and move this one on the top of the list.
    // Search as case-insensitive so that C:\file and c:\file are considered the same.
    int index = RecentFiles.FindIndex(file => file.Equals(path,
                                                          StringComparison.InvariantCultureIgnoreCase));

    if (index != -1) {
      RecentFiles.RemoveAt(index);
    }
    else if (RecentFiles.Count >= 10) {
      RecentFiles.RemoveAt(RecentFiles.Count - 1);
    }

    RecentFiles.Insert(0, path);
  }

  public void RemoveRecentFile(string path) {
    RecentFiles.Remove(path);
  }

  public void ClearRecentFiles() {
    RecentFiles.Clear();
  }

  public void AddRecentTextSearch(string text) {
    //? TODO: Use some weights (number of times used) to sort the list
    //? and make it less likely to evict some often-used term
    // Keep at most N recent files, and move this one on the top of the list.
    if (RecentTextSearches.Contains(text)) {
      RecentTextSearches.Remove(text);
    }
    else if (RecentTextSearches.Count >= 20) {
      RecentTextSearches.RemoveAt(RecentTextSearches.Count - 1);
    }

    RecentTextSearches.Insert(0, text);
  }

  public void ClearRecentTextSearches() {
    RecentTextSearches.Clear();
  }

  public void AddRecentComparedFiles(string basePath, string diffPath) {
    // Keep at most N recent files, and move this one on the top of the list.
    var pair = new Tuple<string, string>(basePath, diffPath);

    if (RecentComparedFiles.Contains(pair)) {
      RecentComparedFiles.Remove(pair);
    }
    else if (RecentComparedFiles.Count >= 10) {
      RecentComparedFiles.RemoveAt(RecentComparedFiles.Count - 1);
    }

    RecentComparedFiles.Insert(0, pair);

    // Also add both files to the recent file list.
    AddRecentFile(basePath);
    AddRecentFile(diffPath);
  }

  public void RemoveRecentComparedFiles(Tuple<string, string> pair) {
    RecentComparedFiles.Remove(pair);
  }

  public void ClearRecentComparedFiles() {
    RecentComparedFiles.Clear();
  }

  public void AddRecordedProfileSession(ProfileDataReport report) {
    AddProfilingSession(report, ProfileOptions.PreviousRecordingSessions);
  }

  public void AddLoadedProfileSession(ProfileDataReport report) {
    AddProfilingSession(report, ProfileOptions.PreviousLoadedSessions);
  }

  public void RemoveRecordedProfileSession(ProfileDataReport report) {
    ProfileOptions.PreviousRecordingSessions.Remove(report);
  }

  public void RemoveLoadedProfileSession(ProfileDataReport report) {
    ProfileOptions.PreviousLoadedSessions.Remove(report);
  }

  public void SaveFunctionTaskOptions(FunctionTaskInfo taskInfo, byte[] data) {
    FunctionTaskOptions[taskInfo.Id] = data;
  }

  public byte[] LoadFunctionTaskOptions(FunctionTaskInfo taskInfo) {
    if (FunctionTaskOptions.TryGetValue(taskInfo.Id, out byte[] data)) {
      return data;
    }

    return null;
  }

  public void SwitchDefaultCompilerIR(string irName, IRMode irMode) {
    DefaultCompilerIR = irName;
    DefaultIRMode = irMode;
  }

  public void CompilerIRSwitched(string irName, IRMode irMode) {
    //? TODO: Hack to get the default IR style picked when the IR changes
    //? Should remember a last {ir -> ir style name} and restore based on that
    DocumentSettings.SyntaxHighlightingName = null;
    App.ReloadSyntaxHighlightingFiles(irName);
  }

  [ProtoBeforeSerialization]
  public void SaveKnownOptions() {
    // Collect all option members in the settings classes
    // and store them in a set to be able to detect new options later
    // when a newer version of the app is used.
    KnownOptions = SettingsBase.CollectOptionMembers(this);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    RecentFiles ??= new List<string>();
    RecentTextSearches ??= new List<string>();
    RecentComparedFiles ??= new List<Tuple<string, string>>();
    GeneralSettings ??= new();
    DocumentSettings ??= new DocumentSettings();
    FlowGraphSettings ??= new FlowGraphSettings();
    ExpressionGraphSettings ??= new ExpressionGraphSettings();
    RemarkSettings ??= new RemarkSettings();
    DiffSettings ??= new DiffSettings();
    SectionSettings ??= new SectionSettings();
    FunctionTaskOptions ??= new Dictionary<Guid, byte[]>();
    ProfileOptions ??= new ProfileDataProviderOptions();
    SymbolSettings ??= new SymbolFileSourceSettings();
    CallTreeSettings ??= new CallTreeSettings();
    CallerCalleeSettings ??= new CallTreeSettings();
    FlameGraphSettings ??= new FlameGraphSettings();
    WorkspaceOptions ??= new WorkspaceSettings();
    SourceFileSettings ??= new SourceFileSettings();
    TimelineSettings ??= new TimelineSettings();
    CallTreeNodeSettings ??= new CallTreeNodeSettings();
    MarkingSettings ??= new FunctionMarkingSettings();
    ElementPreviewPopupSettings ??= new Dictionary<ToolPanelKind, PreviewPopupSettings>();

    if (PreviewPopupSettings == null) {
      PreviewPopupSettings = new PreviewPopupSettings(false);
      PreviewPopupSettings.Reset();
    }

    // When adding new options to existing settings classes in a newer version of the app,
    // ensure they are initialized to their default values. This is done by walking
    // the nested settings using reflection and handle each option that is not part
    // of the KnownOptions set, generated on the previous save with an older app version.
    if (KnownOptions != null) {
      try {
        SettingsBase.InitializeAllNewOptions(this, KnownOptions);
      }
      catch (Exception ex) {
        Trace.TraceError($"Failed to initialize new options: {ex.Message}");
      }
    }

    KnownOptions ??= new HashSet<OptionValueId>();
  }

  public PreviewPopupSettings GetElementPreviewPopupSettings(ToolPanelKind panelKind) {
    return ElementPreviewPopupSettings.GetOrAddValue(panelKind, () => {
      var settings = new PreviewPopupSettings(true);
      settings.Reset();
      return settings;
    });
  }

  private void AddProfilingSession(ProfileDataReport report, List<ProfileDataReport> list) {
    if (list.Contains(report)) {
      list.Remove(report);
    }
    else if (list.Count >= 10) {
      list.RemoveAt(list.Count - 1);
    }

    list.Insert(0, report);
  }

  public bool SaveToArchive(string filePath) {
    try {
      if (File.Exists(filePath)) {
        File.Delete(filePath);
      }

      string appDir = App.GetSettingsDirectoryPath();
      ZipFile.CreateFromDirectory(appDir, filePath, CompressionLevel.Optimal, false);
      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to save settings to {filePath}: {ex.Message}");
      return false;
    }
  }

  public bool LoadFromArchive(string filePath) {
    try {
      // Extract first to a temp dir.
      var tempPath = Directory.CreateTempSubdirectory("irx");
      ZipFile.ExtractToDirectory(filePath, tempPath.FullName, true);

      if (!App.ContainsSettingsFile(tempPath.FullName)) {
        Trace.TraceError($"Invalid settings archive: {filePath}");
        return false;
      }

      // Remove current settings and move the new ones in place.
      string appDir = App.GetSettingsDirectoryPath();
      Directory.Delete(appDir, true);
      Directory.Move(tempPath.FullName, appDir);
      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load settings from {filePath}: {ex.Message}");
      return false;
    }
  }
}