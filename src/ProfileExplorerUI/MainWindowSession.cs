// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.Graph;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.IR.Tags;
using ProfileExplorer.UI.Compilers;
using ProfileExplorer.UI.Document;
using ProfileExplorer.UI.Profile;
using ProfileExplorer.UI.Query;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Binary;
using ProfileExplorerUI.Session;
using System.Linq;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.UI.Compilers.ASM;

namespace ProfileExplorer.UI;

public partial class MainWindow : Window, IUISession {
  private SemaphoreSlim SessionLoadCompleted = new(1);
  public bool SilentMode { get; set; }
  public IUICompilerInfoProvider CompilerInfo => compilerInfo_;
  public IRDocument CurrentDocument => FindActiveDocumentHost()?.TextView;
  public bool IsInDiffMode => sessionState_.SectionDiffState.IsEnabled;
  public bool IsInTwoDocumentsDiffMode => sessionState_.IsInTwoDocumentsDiffMode;
  public bool IsInTwoDocumentsMode => DiffDocumentSummary != null;
  public DiffModeInfo DiffModeInfo => sessionState_.SectionDiffState;
  public IRTextSummary MainDocumentSummary => sessionState_.MainDocument?.Summary;
  public IRTextSummary DiffDocumentSummary => sessionState_.DiffDocument?.Summary;
  public bool IsSessionStarted => sessionState_ != null;

  public IRTextSection CurrentDocumentSection {
    get {
      var activeDocument = FindActiveDocumentHost();
      return activeDocument?.Section;
    }
  }

  public List<IRDocument> OpenDocuments {
    get {
      var list = new List<IRDocument>();

      if (IsSessionStarted) {
        sessionState_.DocumentHosts.ForEach(doc => list.Add(doc.DocumentHost.TextView));
      }

      return list;
    }
  }

  ICompilerInfoProvider ISession.CompilerInfo => CompilerInfo;

  public IReadOnlyList<ILoadedDocument> Documents => sessionState_.Documents;

  public IRTextFunction FindFunctionWithId(int funcNumber, Guid summaryId) {
    return sessionState_.FindFunctionWithId(funcNumber, summaryId);
  }

  public async Task<ILoadedDocument> OpenSessionDocument(string filePath) {
    try {
      await EndSession();
      UpdateUIBeforeReadSession(filePath);
      byte[] data = await File.ReadAllBytesAsync(filePath);
      var state = await SessionStateManager.DeserializeSession(data);

      if (state != null) {
        var loadedDoc = await LoadSessionDocument(state);
        return loadedDoc;
      }
    }
    catch (Exception ex) {
      await EndSession();
      Trace.TraceError($"Failed to load session, exception: {ex}");
    }
    finally {
      UpdateUIAfterLoadDocument();
    }

    return null;
  }

  public async Task<bool> SaveSessionDocument(string filePath) {
    try {
      NotifyPanelsOfSessionSave();
      NotifyDocumentsOfSessionSave();
      sessionState_.Info.IsSaved = true;
      byte[] data = await sessionState_.SerializeSession().ConfigureAwait(false);

      if (data != null) {
        await File.WriteAllBytesAsync(filePath, data).ConfigureAwait(false);
        SetOptionalStatus(TimeSpan.FromSeconds(10), "Session saved");
        return true;
      }
    }
    catch (Exception ex) {
      SetOptionalStatus(TimeSpan.FromSeconds(10), "Failed to save session", ex.Message, Colors.DarkRed.AsBrush());
      Trace.TraceError($"Failed to save session, exception: {ex}");
    }

    return false;
  }

  public async Task<IRDocumentHost>
    OpenDocumentSectionAsync(OpenSectionEventArgs args) {
    return await OpenDocumentSectionAsync(args, args.TargetDocument);
  }

  public async Task<ParsedIRTextSection> LoadAndParseSection(IRTextSection section) {
    return await Task.Run(async () => {
      var docInfo = sessionState_.FindLoadedDocument(section);

      // This shouldn't happen if document was loaded properly...
      if (docInfo == null || docInfo.Loader == null) {
        Trace.WriteLine($"Failed LoadAndParseSection for function {section.ParentFunction.Name}");
        Utils.WaitForDebugger();
        return null;
      }

      var parsedSection = docInfo.Loader.LoadSection(section);

      if (parsedSection != null && parsedSection.Function != null) {
        var funcDebugInfo = ProfileData?.GetFunctionProfile(section.ParentFunction)?.FunctionDebugInfo;
        var loadedDoc = FindLoadedDocument(section.ParentFunction);
        await compilerInfo_.AnalyzeLoadedFunction(parsedSection.Function, section, loadedDoc, funcDebugInfo);
        addressTag_ = parsedSection.Function.GetTag<AssemblyMetadataTag>();
        return parsedSection;
      }

      string placeholderText = "Could not find function code";
      var dummyFunc = new FunctionIR(section.ParentFunction.Name);
      return new ParsedIRTextSection(section, placeholderText.AsMemory(), dummyFunc) {
        LoadFailed = true
      };
    });
  }

  public async Task<Graph> ComputeGraphAsync(GraphKind kind, IRTextSection section,
                                             IRDocument document, CancelableTask loadTask = null,
                                             object options = null) {
    var graphLayout = GetGraphLayoutCache(kind);
    loadTask ??= new CancelableTask(); // Required, but client may not care about canceling.
    return await Task.Run(() => graphLayout.GenerateGraph(document.Function, section,
                                                          loadTask, options));
  }

  public async Task ReloadDocumentSettings(DocumentSettings newSettings, IRDocument document) {
  // CompilerInfo no longer exposes ReloadSettings.

    foreach (var docHostInfo in sessionState_.DocumentHosts) {
      if (docHostInfo.DocumentHost.TextView != document) {
        docHostInfo.DocumentHost.Settings = newSettings;
        await docHostInfo.DocumentHost.ReloadSettings();
      }
    }
  }

  public async Task ReloadRemarkSettings(RemarkSettings newSettings, IRDocument document) {
    foreach (var docHostInfo in sessionState_.DocumentHosts) {
      if (docHostInfo.DocumentHost.TextView != document) {
        await docHostInfo.DocumentHost.UpdateRemarkSettings(newSettings);
      }
    }
  }

  public async Task ReloadSettings() {
    // Reload UI settings.
    OnPropertyChanged(nameof(WindowScaling));

  // Compiler provider no longer exposes ReloadSettings.

    if (!IsSessionStarted) {
      return;
    }

    // Reload settings for all open documents views.
    foreach (var docHostInfo in sessionState_.DocumentHosts) {
      await docHostInfo.DocumentHost.ReloadSettings();
    }

    // Reload settings for all panels.
    await ForEachPanelAsync(async (panel) => await panel.OnReloadSettings());
    await HandleNewDiffSettings(App.Settings.DiffSettings, false, true);
  }

  public Task<string> GetSectionTextAsync(IRTextSection section, IRDocument targetDiffDocument = null) {
    if (IsInDiffMode && targetDiffDocument != null) {
      IRDocument diffDocument = null;

      if (targetDiffDocument == sessionState_.SectionDiffState.LeftDocument.TextView) {
        diffDocument = sessionState_.SectionDiffState.LeftDocument.TextView;
      }
      else if (targetDiffDocument == sessionState_.SectionDiffState.RightDocument.TextView) {
        diffDocument = sessionState_.SectionDiffState.RightDocument.TextView;
      }

      if (diffDocument != null) {
        return Task.FromResult(diffDocument.Text);
      }
    }

    var docInfo = sessionState_.FindLoadedDocument(section);
    return Task.Run(() => docInfo.Loader.GetSectionText(section));
  }

  public Task<string> GetSectionOutputTextAsync(IRPassOutput output, IRTextSection section) {
    var docInfo = sessionState_.FindLoadedDocument(section);
    return Task.Run(() => docInfo.Loader.GetSectionOutputText(output));
  }

  public Task<List<string>> GetSectionOutputTextLinesAsync(IRPassOutput output, IRTextSection section) {
    var docInfo = sessionState_.FindLoadedDocument(section);
    return Task.Run(() => docInfo.Loader.GetSectionPassOutputTextLines(output));
  }

  public Task<string> GetDocumentTextAsync(IRTextSummary summary) {
    var docInfo = sessionState_.FindLoadedDocument(summary);
    return Task.Run(() => docInfo.Loader.GetDocumentOutputText());
  }

  public IToolPanel FindPanel(ToolPanelKind kind) {
    var panelInfo = FindTargetPanel(null, kind);

    if (panelInfo != null) {
      return panelInfo.Panel;
    }

    return null;
  }

  public void ActivatePanel(IToolPanel panel) {
    var panelHost = FindPanelHost(panel);

    if (panelHost != null) {
      panelHost.Host.Show();
      panelHost.Host.IsActive = true;
    }
  }

  public async Task<SectionSearchResult> SearchSectionAsync(
    SearchInfo searchInfo, IRTextSection section, IRDocument document) {
    var docInfo = sessionState_.FindLoadedDocument(section);
    var searcher = new SectionTextSearcher(docInfo.Loader);

    if (searchInfo.SearchAll) {
      var sections = section.ParentFunction.Sections;
      var results = await searcher.SearchAsync(searchInfo.SearchedText, searchInfo.SearchKind, sections);

      var panelInfo = FindTargetPanel(document, ToolPanelKind.SearchResults);
      var searchPanel = panelInfo.Panel as SearchResultsPanel;
      searchPanel.UpdateSearchResults(results, searchInfo);

      if (results.Count > 0) {
        panelInfo.Host.IsSelected = true;
      }

      // Return the results for the section that started the search, if any.
      var sectionResult = results.Find(item => item.Section == section);
      return sectionResult ?? new SectionSearchResult(section);
    }

    // In diff mode, use the diff text being displayed, which may be different
    // than the original section text due to diff annotations.
    if (document.DiffModeEnabled) {
      return await searcher.SearchSectionWithTextAsync(document.Text, searchInfo.SearchedText,
                                                       searchInfo.SearchKind, section);
    }

    return await searcher.SearchSectionAsync(searchInfo.SearchedText, searchInfo.SearchKind, section);
  }

  public bool SwitchToPreviousSection(IRTextSection section, IRDocument document) {
    var prevSection = GetPreviousSection(section);

    if (prevSection != null) {
      var docHost = FindDocumentHost(document);
      SectionPanel.SwitchToSection(prevSection, docHost);
      return true;
    }

    return false;
  }

  public bool SwitchToNextSection(IRTextSection section, IRDocument document) {
    var nextSection = GetNextSection(section);

    if (nextSection != null) {
      var docHost = FindDocumentHost(document);
      SectionPanel.SwitchToSection(nextSection, docHost);
      return true;
    }

    return false;
  }

  public IRTextSection GetNextSection(IRTextSection section) {
    int index = section.Number - 1;

    if (index < section.ParentFunction.SectionCount - 1) {
      return section.ParentFunction.Sections[index + 1];
    }

    return null;
  }

  public IRTextSection GetPreviousSection(IRTextSection section) {
    int index = section.Number - 1;

    if (index > 0) {
      return section.ParentFunction.Sections[index - 1];
    }

    return null;
  }

  public void SaveDocumentState(object stateObject, IRTextSection section) {
    if (IsInDiffMode) {
      //? TODO: Find a way to at least temporarily save state for the two diffed docs
      //? Issue is that in diff mode a section can have a different FunctionIR depending
      //? on the other section is compared with
      if (section == sessionState_.SectionDiffState.LeftSection ||
          section == sessionState_.SectionDiffState.RightSection) {
        return;
      }
    }

    sessionState_.SaveDocumentState(stateObject, section);
  }

  public object LoadDocumentState(IRTextSection section) {
    if (IsInDiffMode) {
      if (section == sessionState_.SectionDiffState.LeftSection ||
          section == sessionState_.SectionDiffState.RightSection) {
        return null;
      }
    }

    return sessionState_.LoadDocumentState(section);
  }

  public bool SaveFunctionTaskOptions(FunctionTaskInfo taskInfo, IFunctionTaskOptions options) {
    byte[] data = FunctionTaskOptionsSerializer.Serialize(options);
    App.Settings.SaveFunctionTaskOptions(taskInfo, data);
    return true;
  }

  public IFunctionTaskOptions LoadFunctionTaskOptions(FunctionTaskInfo taskInfo) {
    byte[] data = App.Settings.LoadFunctionTaskOptions(taskInfo);

    if (data != null) {
      return FunctionTaskOptionsSerializer.Deserialize(data, taskInfo.OptionsType);
    }

    return null;
  }

  public void SetApplicationStatus(string text, string tooltip) {
    SetOptionalStatus(text, tooltip, Brushes.MediumBlue);
  }

  public void SetApplicationProgress(bool visible, double percentage, string title = null) {
    Dispatcher.BeginInvoke(() => {
      if (visible && !documentLoadProgressVisible_) {
        Mouse.OverrideCursor = Cursors.AppStarting;
        ShowProgressBar(title);
      }
      else if (!visible) {
        Mouse.OverrideCursor = null;
        HideProgressBar();
        return;
      }

      if (double.IsNaN(percentage)) {
        DocumentLoadProgressBar.IsIndeterminate = true;
      }
      else {
        DocumentLoadProgressBar.IsIndeterminate = false;
        percentage = Math.Max(percentage, DocumentLoadProgressBar.Value);
        DocumentLoadProgressBar.Value = percentage;
      }
    }, DispatcherPriority.Render);
  }

  public void UpdateDocumentTitles() {
    foreach (var docHostPair in sessionState_.DocumentHosts) {
      docHostPair.Host.Title = GetDocumentTitle(docHostPair.DocumentHost, docHostPair.Section);
      docHostPair.Host.ToolTip = GetDocumentDescription(docHostPair.DocumentHost, docHostPair.Section);
    }
  }

  public async Task SwitchGraphsAsync(GraphPanel graphPanel, IRTextSection section, IRDocument document,
                                      Func<FunctionIR, IRTextSection, CancelableTask, Graph> computeGraphAction) {
    if (document.Function == null) {
      return; // Function failed to load, ignore.
    }

    using var loadTask = await graphPanel.OnGenerateGraphStart(section);
    var functionGraph = await Task.Run(() => computeGraphAction(document.Function, section, loadTask));

    if (functionGraph != null) {
      graphPanel.DisplayGraph(functionGraph);
      graphPanel.OnGenerateGraphDone(loadTask);
    }
    else {
      //? TODO: Handle CFG failure in the UI
      graphPanel.OnGenerateGraphDone(loadTask, true);
      Trace.TraceError($"Document {ObjectTracker.Track(document)}: Failed to load CFG");
    }
  }

  public Task<string> GetRawSectionTextAsync(IRTextSection section, IRDocument targetDiffDocument = null) {
    var docInfo = sessionState_.FindLoadedDocument(section);
    return Task.Run(() => docInfo.Loader.GetSectionText(section));
  }

  public Task<string> GetSectionOutputTextAsync(IRTextSection section, bool useOutputBefore) {
    var output = useOutputBefore ? section.OutputBefore : section.OutputAfter;
    var docInfo = sessionState_.FindLoadedDocument(section);
    return Task.Run(() => docInfo.Loader.GetSectionOutputText(output));
  }

  public IToolPanel FindAndActivatePanel(ToolPanelKind kind) {
    var panelInfo = FindTargetPanel(null, kind);

    if (panelInfo != null) {
      panelInfo.Host.IsActive = true;
      panelInfo.Host.IsSelected = true;
      return panelInfo.Panel;
    }

    return null;
  }

  public void ResetApplicationProgress() {
    SetApplicationProgress(false, double.NaN);
  }

  private async Task<ILoadedDocument> OpenDocument(string filePath) {
    ILoadedDocument loadedDoc = null;
    bool failed = false;
    bool isProfilingFile = false;

    if (Path.HasExtension(filePath)) {
      if (Utils.FileHasExtension(filePath, ".pex")) {
        loadedDoc = await OpenSessionDocument(filePath);
        failed = loadedDoc == null;
      }
      else if (Utils.IsBinaryFile(filePath)) {
        loadedDoc = await OpenBinaryDocument(filePath);
        failed = loadedDoc == null;
      }
      else if (Utils.FileHasExtension(filePath, ".etl")) {
        var profileSession = RecordingSession.FromFile(filePath);
        var window = new ProfileLoadWindow(this, false, profileSession);
        window.Owner = this;
        isProfilingFile = true;

        bool? result = window.ShowDialog();
        failed = !result.HasValue || !result.Value;

        if (!failed) {
          await SetupLoadedProfile();
          return sessionState_.MainDocument;
        }
      }
    }

    if (loadedDoc == null && !failed) {
      loadedDoc = await OpenIRDocument(filePath, filePath, LoadDocument);
    }

    if (loadedDoc == null && !isProfilingFile) {
      using var centerForm = new DialogCenteringHelper(this);
      MessageBox.Show($"Failed to load file {filePath}", "Profile Explorer", MessageBoxButton.OK,
                      MessageBoxImage.Exclamation);
    }

    return loadedDoc;
  }

  private async Task SwitchBinaryCompilerTarget(string filePath) {
    // Identify compiler target and switch IR mode.
    var binaryInfo = PEBinaryInfoProvider.GetBinaryFileInfo(filePath);

    if (binaryInfo == null) {
      return;
    }

    switch (binaryInfo.Architecture) {
      case Machine.I386:
      case Machine.Amd64: {
        switch (binaryInfo.FileKind) {
          case BinaryFileKind.Native: {
            await SwitchCompilerTarget("ASM", IRMode.x86_64);
            break;
          }
        }

        break;
      }
      case Machine.Arm:
      case Machine.Arm64: {
        switch (binaryInfo.FileKind) {
          case BinaryFileKind.Native: {
            await SwitchCompilerTarget("ASM", IRMode.ARM64);
            break;
          }
        }

        break;
      }
    }
  }

  private async Task<ILoadedDocument> OpenBinaryDocument(string filePath) {
    await SwitchBinaryCompilerTarget(filePath);
    var result = await OpenIRDocument(filePath, filePath, LoadBinaryDocument);

    if (result != null) {
      SectionPanel.EnterBinaryDisplayMode();
    }

    return result;
  }

  private async void OpenDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
    await OpenDocument();
  }

  private void OpenDebugExecuted(object sender, ExecutedRoutedEventArgs e) {
    Utils.ShowOpenFileDialog(CompilerInfo.OpenDebugFileFilter, "*.pdb", "Open debug info file",
                             path => sessionState_.MainDocument.DebugInfoFile =
                               DebugFileSearchResult.Success(path));
  }

  private void OpenDiffDebugExecuted(object sender, ExecutedRoutedEventArgs e) {
    Utils.ShowOpenFileDialog(CompilerInfo.OpenDebugFileFilter, "*.pdb", "Open debug info file",
                             path => sessionState_.DiffDocument.DebugInfoFile =
                               DebugFileSearchResult.Success(path));
  }

  private void CanExecuteDiffDocumentCommand(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = sessionState_ != null && sessionState_.IsInTwoDocumentsDiffMode;
    e.Handled = true;
  }

  private async Task OpenDocument() {
    await Utils.ShowOpenFileDialogAsync(CompilerInfo.OpenFileFilter, "*.*", "Open file",
                                        async path => {
                                          var loadedDoc = await OpenDocument(path);

                                          if (loadedDoc != null) {
                                            AddRecentFile(path);
                                          }
                                        });
  }

  private async Task<ILoadedDocument>
    InitializeFromLoadedSession(SessionState state, Dictionary<Guid, ILoadedDocument> idToDocumentMap) {
    sessionState_.Info.Notes = state.Info.Notes;
    int index = 0;

    foreach (var docState in state.Documents) {
      var summary = sessionState_.Documents[index].Summary;
      index++;

      // Reload state of document annotations.
      foreach (var sectionState in docState.SectionStates) {
        var section = summary.GetSectionWithId(sectionState.Item1);
        sessionState_.SaveDocumentState(sectionState.Item2, section);
      }
    }

    // Reload state of panels from the session state.
    foreach (var docPanelStates in state.DocumentPanelStates) {
      var loadedDoc = idToDocumentMap[docPanelStates.Key];
      var summary = loadedDoc.Summary;
      
      foreach (var panelState in docPanelStates.Value) {
        var section = summary.GetSectionWithId(panelState.Item1);
        var panelInfo = FindActivePanel(panelState.Item2.PanelKind);
        sessionState_.SavePanelState(panelState.Item2.StateObject, panelInfo.Panel, section);
      }
    }

    foreach (var panelState in state.GlobalPanelStates) {
      var panelInfo = FindActivePanel(panelState.PanelKind);
      sessionState_.SavePanelState(panelState.StateObject, panelInfo.Panel, null);
    }

    await SetupPanels();

    // Reload sections left open.
    var idToDocumentHostMap = new Dictionary<Guid, IRDocumentHost>();

    foreach (var openSection in state.OpenSections) {
      var loadedDoc = idToDocumentMap[openSection.DocumentId];
      var section = loadedDoc.Summary.GetSectionWithId(openSection.SectionId);
      var openKind = OpenSectionKind.NewTab;

      if (sessionState_.IsInTwoDocumentsDiffMode) {
        if (loadedDoc == sessionState_.MainDocument) {
          openKind = OpenSectionKind.NewTabDockLeft;
        }
        else if (loadedDoc == sessionState_.DiffDocument) {
          openKind = OpenSectionKind.NewTabDockRight;
        }
      }

      var args = new OpenSectionEventArgs(section, openKind);
      var docHost = await OpenDocumentSectionAsync(args);
      idToDocumentHostMap[openSection.DocumentId] = docHost;
    }

    // Enter diff mode if it was active.
    if (state.SectionDiffState.IsEnabled &&
        state.OpenSections.Count > 1 &&
        idToDocumentHostMap.TryGetValue(state.SectionDiffState.LeftSection.DocumentId, out var leftDocument) &&
        idToDocumentHostMap.TryGetValue(state.SectionDiffState.RightSection.DocumentId, out var rightDocument)) {
      await EnterDocumentDiffState(leftDocument, rightDocument);

      // Compare the two files.
      if (sessionState_.IsInTwoDocumentsDiffMode) {
        await ShowSectionPanelDiffs(sessionState_.DiffDocument);
      }
    }

    StartAutoSaveTimer();
    return sessionState_.MainDocument;
  }

  private bool RequestSessionFilePath(bool forceNewFile = false) {
    if (!forceNewFile && sessionState_.Info.IsSavedFileSession) {
      return true; // Save over same session file.
    }

    string filePath = Utils.ShowSaveFileDialog("Profile Explorer Session File|*.pex", "*.pex");

    if (filePath == null) {
      return false;
    }

    sessionState_.Info.FilePath = filePath;
    sessionState_.Info.Kind = SessionKind.FileSession;
    UpdateWindowTitle();
    return true;
  }

  private void StartSession(string filePath, SessionKind sessionKind) {
    sessionState_ = new SessionStateManager(filePath, sessionKind, compilerInfo_);
    sessionState_.DocumentChanged += DocumentState_DocumentChangedEvent;
    sessionState_.ChangeDocumentWatcherState(App.Settings.AutoReloadDocument);
    documentLoadTask_ = new CancelableTaskInstance(false, SessionState.RegisterCancelableTask,
                                                   SessionState.UnregisterCancelableTask);
    ClearGraphLayoutCache();
  // Compiler provider no longer exposes ReloadSettings.

    DiffModeButton.IsEnabled = true;
    HideStartPage();
  }

  private async Task EndSession(bool showStartPage = true) {
    await BeginSessionStateChange();

    if (!IsSessionStarted) {
      EndSessionStateChange();
      return;
    }

    if (autoSaveTimer_ != null) {
      autoSaveTimer_.Stop();
      autoSaveTimer_ = null;
    }

    // Wait for any pending tasks to complete.
    await sessionState_.CancelPendingTasks();

    // Close all documents and notify all panels.
    NotifyPanelsOfSessionEnd();

    foreach (var docHostInfo in sessionState_.DocumentHosts) {
      CloseDocument(docHostInfo);
    }

    await ExitDocumentDiffState(true);
    sessionState_.DocumentChanged -= DocumentState_DocumentChangedEvent;
    sessionState_.EndSession();

    FunctionAnalysisCache.ResetCache();
    DiffModeButton.IsEnabled = false;

    if (showStartPage) {
      ShowStartPage();
    }

    EndSessionStateChange();
  }

  private void CloseDocument(DocumentHostInfo docHostInfo) {
    ResetDocumentEvents(docHostInfo.DocumentHost);
    docHostInfo.HostParent.Children.Remove(docHostInfo.Host);
  }

  private async Task<ILoadedDocument> OpenIRDocument(string filePath, string modulePath,
                                                    Func<string, string, Guid, ProgressInfoHandler,
                                                      Task<ILoadedDocument>> loadFunc) {
    try {
      await EndSession();
      await BeginSessionStateChange();
      UpdateUIBeforeLoadDocument(filePath);
      var result = await Task.Run(async () => await loadFunc(filePath, modulePath, Guid.NewGuid(),
                                                             UpdateIRDocumentLoadProgress));

      if (result != null) {
        await SetupOpenedIRDocument(SessionKind.Default, result);
        return result;
      }
    }
    catch (Exception ex) {
      await EndSession();
      Trace.TraceError($"Failed to load document: {ex}");
    }
    finally {
      UpdateUIAfterLoadDocument();
      EndSessionStateChange();
    }

    // Failed to start a session.
    return null;
  }

  private async Task BeginSessionStateChange() {
    // Wait for any running state changes.
    await SessionLoadCompleted.WaitAsync();
    await updateProfileTask_.CancelTaskAndWaitAsync();

    loadingDocuments_ = true;
    documentLoadStartTime_ = DateTime.UtcNow;
    lastDocumentLoadTime_ = DateTime.UtcNow;
  }

  private void EndSessionStateChange() {
    SessionLoadCompleted.Release();
  }

  private void UpdateIRDocumentLoadProgress(IRSectionReader reader, SectionReaderProgressInfo info) {
    if (info.IsIndeterminate) {
      Dispatcher.BeginInvoke(new Action(() => {
        SetApplicationProgress(true, double.NaN);
      }), DispatcherPriority.Render);
    }
    else if (info.TotalBytes == 0) {
      Dispatcher.BeginInvoke(new Action(() => {
        SetApplicationProgress(false, 0);
      }), DispatcherPriority.Render);
      return;
    }

    // Updating too often slows down the file parsing by about
    // 20-30% due to Dispatcher.BeginInvoke overhead.
    var currentTime = DateTime.UtcNow;
    var diffTime = currentTime - lastDocumentLoadTime_;

    if (diffTime.TotalMilliseconds < 100) {
      return;
    }

    // Schedule the UI update.
    Dispatcher.BeginInvoke(new Action(() => {
      if (!loadingDocuments_) {
        // It can happen that this code on the dispatchers runs after
        // the document has already been loaded, so just ignore the events.
        return;
      }

      bool firstTimeShowPanel = false;

      if (!documentLoadProgressVisible_) {
        // Progress panel not displayed yet, show it only after a bit of time
        // passes since most files are small and load instantly.
        var timeDiff = DateTime.UtcNow - documentLoadStartTime_;

        if (timeDiff.TotalMilliseconds < 500) {
          return;
        }

        firstTimeShowPanel = true;
      }

      double percentage = Math.Ceiling(100 * (info.BytesProcessed / (double)info.TotalBytes));

      // If the progress panel is about to be displayed, but most of the file
      // as been processed already, there's no point in showing it anymore.
      if (firstTimeShowPanel) {
        if (percentage > 50) {
          return;
        }
      }

      SetApplicationProgress(true, percentage);
    }), DispatcherPriority.Render);
  }

  private async Task SetupOpenedIRDocument(SessionKind sessionKind, ILoadedDocument result) {
    StartSession(result.FilePath, sessionKind);
    sessionState_.RegisterLoadedDocument(result);
    sessionState_.MainDocument = result;

    UpdateUIAfterLoadDocument();
    StartAutoSaveTimer();
    await SetupPanels();
  }

  private async Task<bool> OpenDiffIRDocument(string filePath) {
    try {
      var result = await Task.Run(() => LoadDocument(filePath, filePath, Guid.NewGuid(),
                                                     UpdateIRDocumentLoadProgress));

      if (result != null) {
        await SetupOpenedDiffIRDocument(filePath, result);
        return true;
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load diff document {filePath}: {ex}");
    }

    UpdateUIAfterLoadDocument();
    return false;
  }

  private async Task SetupOpenedDiffIRDocument(string diffFilePath, ILoadedDocument result) {
    sessionState_.RegisterLoadedDocument(result);
    sessionState_.EnterTwoDocumentDiffMode(result);
    await SetupPanels();
    //await ShowSectionPanelDiffs(result);
  }

  private async Task<ILoadedDocument> LoadDocument(string filePath, string modulePath, Guid id,
                                                  ProgressInfoHandler progressHandler) {
    return await LoadDocument(filePath, modulePath, id, progressHandler,
                              new DocumentSectionLoader(filePath, compilerInfo_.IR));
  }

  private async Task<ILoadedDocument> LoadBinaryDocument(string filePath, string modulePath, Guid id,
                                                        ProgressInfoHandler progressHandler) {
    return await LoadBinaryDocument(filePath, modulePath, id, null, progressHandler).ConfigureAwait(false);
  }

  private async Task<ILoadedDocument> LoadBinaryDocument(string filePath, string modulePath, Guid id,
                                                        IDebugInfoProvider debugInfo,
                                                        ProgressInfoHandler progressHandler) {
    var loader = new DisassemblerSectionLoader(filePath, compilerInfo_.IR, debugInfo, compilerInfo_.DebugFileFinder,
                                               compilerInfo_.DebugInfoProviderFactory, compilerInfo_.NameProvider);
    var result = await LoadDocument(filePath, modulePath, id, progressHandler, loader);

    if (result != null) {
      result.BinaryFile = BinaryFileSearchResult.Success(filePath);

      if (debugInfo == null) {
        result.DebugInfo = loader.DebugInfo;
        result.DebugInfoFile = loader.DebugInfoFile;
      }
    }

    return result;
  }

  private async Task<ILoadedDocument> LoadDocument(string filePath, string modulePath, Guid id,
                                                  ProgressInfoHandler progressHandler,
                                                  IRTextSectionLoader loader) {
    try {
      var result = await Task.Run(async () => {
        var result = new LoadedDocument(filePath, modulePath, id);
        result.Loader = loader;
        result.Summary = await result.Loader.LoadDocument(progressHandler);
        return result;
      });

      return result;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load document {filePath}: {ex}");
      return null;
    }
  }

  private async Task<ILoadedDocument> LoadDocument(byte[] data, string filePath, string modulePath, Guid id,
                                      ProgressInfoHandler progressHandler) {
    try {
      var result = new LoadedDocument(filePath, modulePath, id);
      result.Loader = new DocumentSectionLoader(data, compilerInfo_.IR);
      result.Summary = await result.Loader.LoadDocument(progressHandler);
      return result;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load in-memory document: {ex}");
      return null;
    }
  }

  private async Task<ILoadedDocument> LoadSessionDocument(SessionState state) {
    try {
      if (!string.IsNullOrEmpty(state.Info.IRName)) {
        await SwitchCompilerTarget(state.Info.IRName, state.Info.IRMode);
      }

      StartSession(state.Info.FilePath, SessionKind.FileSession);
      var idToDocumentMap = new Dictionary<Guid, ILoadedDocument>();

      foreach (var docState in state.Documents) {
        ILoadedDocument result = null;

        if (docState.DocumentText != null && docState.DocumentText.Length > 0) {
          result = await Task.Run(() => LoadDocument(docState.DocumentText, docState.FilePath,
                                                     docState.ModuleName, docState.Id,
                                                     UpdateIRDocumentLoadProgress));
        }
        else if (docState.BinaryFile != null) {
          result = await Task.Run(() => LoadBinaryDocument(docState.BinaryFile.FilePath,
                                                           docState.ModuleName, docState.Id,
                                                           UpdateIRDocumentLoadProgress));
        }
        else if (!string.IsNullOrEmpty(docState.ModuleName)) {
          // Fake document used by profiling to represent missing binaries.
          result = LoadedDocument.CreateDummyDocument(docState.ModuleName, docState.Id);
        }

        if (result == null) {
          UpdateUIAfterLoadDocument();
          return null;
        }

        // Profiling can add extra dummy functions to a document, restore them.
        if (docState.FunctionNames != null) {
          result.AddDummyFunctions(docState.FunctionNames);
        }

        sessionState_.RegisterLoadedDocument(result);
        idToDocumentMap[docState.Id] = result;

        if (state.IsInTwoDocumentsDiffMode) {
          if (docState.Id == state.MainDocumentId) {
            sessionState_.MainDocument = result;
          }
          else if (docState.Id == state.DiffDocumentId) {
            sessionState_.EnterTwoDocumentDiffMode(result);
          }
        }
        else {
          // Outside of diff mode there can still be multiple documents
          // loaded with profile sessions, for ex.
          if (docState.Id == state.MainDocumentId) {
            sessionState_.MainDocument = result;
          }
        }
      }

      UpdateUIAfterLoadDocument();
      return await InitializeFromLoadedSession(state, idToDocumentMap);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load in-memory document: {ex}");
    }

    UpdateUIAfterLoadDocument();
    return null;
  }

  private async void SectionPanel_OpenSection(object sender, OpenSectionEventArgs args) {
    await OpenDocumentSectionAsync(args, args.TargetDocument);
  }

  private async Task<IRDocumentHost>
    OpenDocumentSectionAsync(OpenSectionEventArgs args, IRDocumentHost targetDocument = null,
                             bool runExtraTasks = true) {
    var document = targetDocument;

    if (args.OpenKind == OpenSectionKind.NewTab ||
        args.OpenKind == OpenSectionKind.NewTabDockLeft ||
        args.OpenKind == OpenSectionKind.NewTabDockRight) {
      document = (await AddNewDocument(args.OpenKind)).DocumentHost;
    }
    else if (args.OpenKind == OpenSectionKind.ReplaceCurrent ||
             args.OpenKind == OpenSectionKind.ReplaceLeft ||
             args.OpenKind == OpenSectionKind.ReplaceRight) {
      // Try to pick a host from the same module first.
      document = FindSameSummaryDocumentHost(args.Section);

      // Otherwise pick the active host, if any, or create a new one.
      if (document == null && args.OpenKind == OpenSectionKind.ReplaceCurrent) {
        document = FindActiveDocumentHost();
      }

      if (document == null) {
        document = (await AddNewDocument(args.OpenKind)).DocumentHost;
      }
    }
    else {
      throw new InvalidOperationException("Unhandled OpenSectionKind");
    }

    // In diff mode, reload both left/right sections and redo the diffs.
    if (IsInDiffMode &&
        sessionState_.SectionDiffState.IsDiffDocument(document)) {
      await SwitchDiffedDocumentSection(args.Section, document);
      return document;
    }

    await SwitchSection(args.Section, document, runExtraTasks);
    return document;
  }

  private async Task<ParsedIRTextSection>
    SwitchSection(IRTextSection section, IRDocumentHost document, bool runExtraTasks) {
    Trace.TraceInformation(
      $"Document {ObjectTracker.Track(document)}: Switch to section ({section.Number}) {section.Name}");

    // Wait for any other pending opening tasks to complete,
    // otherwise the opened document and panels can get out of sync.
    using var task = await documentLoadTask_.CancelCurrentAndCreateTaskAsync();

    await NotifyOfSectionUnload(document, true);
    ResetDocumentEvents(document);
    ResetStatusBar();
    var delayedAction = UpdateUIBeforeSectionLoad(section, document);

    // Load section and try to parse it.
    var result = await LoadAndParseSection(section);

    if (task.IsCanceled) {
      return null;
    }

    // Handle loading failures.
    UpdateUIForSectionLoading(result, document);

    if (result == null) {
      // Hide any UI that may show due to long-running tasks.
      UpdateUIAfterSectionLoad(section, document, delayedAction);
      return null;
    }

    // Update UI to reflect new section before starting long-running tasks.
    await document.LoadSectionMinimal(result);

    if (task.IsCanceled) {
      return null;
    }

    await NotifyPanelsOfSectionLoad(section, document, true);

    if (task.IsCanceled) {
      return null;
    }

    SetupDocumentEvents(document);
    await UpdateUIAfterSectionSwitch(section, document);

    if (task.IsCanceled) {
      return null;
    }

    // Load both the document and generate graphs in parallel,
    // since both can be fairly time-consuming for huge functions.
    var documentTask = document.LoadSection(result);
    Task graphTask = null;

    if (runExtraTasks) {
      graphTask = GenerateGraphs(section, document.TextView);
    }

    await documentTask;
    UpdateUIAfterSectionLoad(section, document, delayedAction);

    if (graphTask != null) {
      await graphTask;
    }

    return result;
  }

  private void UpdateUIForSectionLoading(ParsedIRTextSection result, IRDocumentHost document) {
    if (result == null || result.Function == null) {
      Trace.TraceError($"Document {ObjectTracker.Track(document)}: Failed to parse function");
      OptionalStatusText.Text = "Failed to parser section IR";
      OptionalStatusText.ToolTip = FormatParsingErrors(result, "Section IR parsing errors");
    }
    else if (result.HadParsingErrors) {
      Trace.TraceWarning($"Document {ObjectTracker.Track(document)}: Parsed function with errors");
      OptionalStatusText.Text = "IR parsing issues";
      OptionalStatusText.ToolTip = FormatParsingErrors(result, "Section IR parsing errors");
    }
    else {
      OptionalStatusText.Text = "";
    }
  }

  private string FormatParsingErrors(ParsedIRTextSection result, string message) {
    if (result == null || !result.HadParsingErrors) {
      return "";
    }

    var builder = new StringBuilder();
    builder.AppendLine($"{message}: {result.ParsingErrors.Count}");

    foreach (var error in result.ParsingErrors) {
      builder.AppendLine(error.ToString());
    }

    return builder.ToString();
  }

  private async Task GenerateGraphs(IRTextSection section, IRDocument document,
                                    bool awaitTasks = true) {
    var tasks = new[] {
      GenerateGraphs(GraphKind.FlowGraph, section, document),
      GenerateGraphs(GraphKind.DominatorTree, section, document),
      GenerateGraphs(GraphKind.PostDominatorTree, section, document)
    };

    if (awaitTasks) {
      await Task.WhenAll(tasks);
    }
  }

  private async Task GenerateGraphs(GraphKind graphKind, IRTextSection section, IRDocument document) {
    var panelKind = graphKind switch {
      GraphKind.FlowGraph         => ToolPanelKind.FlowGraph,
      GraphKind.DominatorTree     => ToolPanelKind.DominatorTree,
      GraphKind.PostDominatorTree => ToolPanelKind.PostDominatorTree,
      _                           => throw new InvalidOperationException("Unexpected graph kind!")
    };

    var action = GetComputeGraphAction(graphKind);
    var flowGraphPanels = FindTargetPanels(document, panelKind);

    foreach (var panelInfo in flowGraphPanels) {
      await SwitchGraphsAsync((GraphPanel)panelInfo.Panel, section, document, action);
    }
  }

  private Func<FunctionIR, IRTextSection, CancelableTask, Graph> GetComputeGraphAction(
    GraphKind graphKind) {
    return graphKind switch {
      GraphKind.FlowGraph         => ComputeFlowGraph,
      GraphKind.DominatorTree     => ComputeDominatorTree,
      GraphKind.PostDominatorTree => ComputePostDominatorTree,
      _                           => throw new InvalidOperationException("Unexpected graph kind!")
    };
  }

  private Func<FunctionIR, IRTextSection, CancelableTask, Graph> GetComputeGraphAction(
    ToolPanelKind graphKind) {
    return graphKind switch {
      ToolPanelKind.FlowGraph         => ComputeFlowGraph,
      ToolPanelKind.DominatorTree     => ComputeDominatorTree,
      ToolPanelKind.PostDominatorTree => ComputePostDominatorTree,
      _                               => throw new InvalidOperationException("Unexpected graph kind!")
    };
  }

  private void GraphViewer_GraphNodeSelected(object sender, IRElementEventArgs e) {
    var panel = ((GraphViewer)sender).HostPanel;
    var document = FindTargetDocument(panel);
    document.TextView.HighlightElement(e.Element, HighlighingType.Selected);
  }

  private Graph ComputeFlowGraph(FunctionIR function, IRTextSection section,
                                 CancelableTask loadTask) {
    var graphLayout = GetGraphLayoutCache(GraphKind.FlowGraph);
    return graphLayout.GenerateGraph(function, section, loadTask, (object)null);
  }

  private Graph ComputeDominatorTree(FunctionIR function, IRTextSection section,
                                     CancelableTask loadTask) {
    var graphLayout = GetGraphLayoutCache(GraphKind.DominatorTree);
    return graphLayout.GenerateGraph(function, section, loadTask, (object)null);
  }

  private Graph ComputePostDominatorTree(FunctionIR function, IRTextSection section,
                                         CancelableTask loadTask) {
    var graphLayout = GetGraphLayoutCache(GraphKind.PostDominatorTree);
    return graphLayout.GenerateGraph(function, section, loadTask, (object)null);
  }

  private void GraphViewer_BlockUnmarked(object sender, IRElementMarkedEventArgs e) {
    var panel = ((GraphViewer)sender).HostPanel;
    var document = FindTargetDocument(panel);
    document.TextView.UnmarkBlock(e.Element, HighlighingType.Marked);
  }

  private void GraphViewer_BlockMarked(object sender, IRElementMarkedEventArgs e) {
    var panel = ((GraphViewer)sender).HostPanel;
    var document = FindTargetDocument(panel);
    document.TextView.MarkBlock(e.Element, e.Style);
  }

  private void GraphViewer_GraphLoaded(object sender, EventArgs e) {
    var panel = ((GraphViewer)sender).HostPanel;
    var document = FindTargetDocument(panel);
    document.TextView.PanelContentLoaded(panel);
  }

  private void SetupGraphLayoutCache() {
    graphLayout_ = new Dictionary<GraphKind, GraphLayoutCache>();
    graphLayout_.Add(GraphKind.FlowGraph, new GraphLayoutCache(GraphKind.FlowGraph));
    graphLayout_.Add(GraphKind.DominatorTree, new GraphLayoutCache(GraphKind.DominatorTree));
    graphLayout_.Add(GraphKind.PostDominatorTree, new GraphLayoutCache(GraphKind.PostDominatorTree));
    graphLayout_.Add(GraphKind.ExpressionGraph, new GraphLayoutCache(GraphKind.ExpressionGraph));
  }

  private void ClearGraphLayoutCache() {
    foreach (var cache in graphLayout_.Values) {
      cache.ClearCache();
    }
  }

  private GraphLayoutCache GetGraphLayoutCache(GraphKind kind) {
    return graphLayout_[kind];
  }

  private DelayedAction UpdateUIBeforeSectionLoad(IRTextSection section, IRDocumentHost document) {
    var delayedAction = new DelayedAction();
    //delayedAction.Start(TimeSpan.FromMilliseconds(500), () => { document.Opacity = 0.5; });
    return delayedAction;
  }

  private async Task UpdateUIAfterSectionSwitch(IRTextSection section, IRDocumentHost document,
                                                DelayedAction delayedAction = null) {
    var docHostPair = FindDocumentHostPair(document);

    //? TODO: Can happen if the loading is slow (debug mode?)
    //? and doc view is closed by user.
    if (docHostPair == null) {
      return;
    }

    docHostPair.Host.Title = GetDocumentTitle(document, section);
    docHostPair.Host.ToolTip = GetDocumentDescription(document, section);

    RenameAllPanels(); // For bound panels.
    await SectionPanel.SelectSection(section, false);

    if (delayedAction != null) {
      UpdateUIAfterSectionLoad(section, document, delayedAction);
    }
  }

  private void UpdateUIAfterSectionLoad(IRTextSection section, IRDocumentHost document,
                                        DelayedAction delayedAction) {
    delayedAction.Cancel();
    //document.Opacity = 1;
  }

  private IRDocumentHost FindTargetDocument(IToolPanel panel) {
    if (panel.BoundDocument != null) {
      return FindDocumentHost(panel.BoundDocument);
    }

    return FindActiveDocumentHost();
  }

  private void SetupDocumentEvents(IRDocumentHost document) {
    document.TextView.ActionPerformed += TextView_ActionPerformed;
    document.TextView.ElementSelected += TextView_IRElementSelected;
    document.TextView.ElementHighlighting += TextView_IRElementHighlighting;
    document.TextView.BlockSelected += TextView_BlockSelected;
    document.TextView.BookmarkAdded += TextView_BookmarkAdded;
    document.TextView.BookmarkRemoved += TextView_BookmarkRemoved;
    document.TextView.BookmarkChanged += TextView_BookmarkChanged;
    document.TextView.BookmarkSelected += TextView_BookmarkSelected;
    document.TextView.BookmarkListCleared += TextView_BookmarkListCleared;
    document.TextView.CaretChanged += TextView_CaretChanged;
    document.VerticalScrollChanged += DocumentVerticalScrollChanged;
    document.PassOutputVerticalScrollChanged += DocumentPassOutputVerticalScrollChanged;
    document.PassOutputVisibilityChanged += Document_PassOutputVisibilityChanged;
    document.PassOutputShowBeforeChanged += Document_PassOutputShowBeforeChanged;
  }

  private void ResetDocumentEvents(IRDocumentHost document) {
    document.TextView.ElementSelected -= TextView_IRElementSelected;
    document.TextView.ElementHighlighting -= TextView_IRElementHighlighting;
    document.TextView.BlockSelected -= TextView_BlockSelected;
    document.TextView.BookmarkAdded -= TextView_BookmarkAdded;
    document.TextView.BookmarkRemoved -= TextView_BookmarkRemoved;
    document.TextView.BookmarkChanged -= TextView_BookmarkChanged;
    document.TextView.BookmarkSelected -= TextView_BookmarkSelected;
    document.TextView.BookmarkListCleared -= TextView_BookmarkListCleared;
    document.TextView.CaretChanged -= TextView_CaretChanged;
    document.VerticalScrollChanged -= DocumentVerticalScrollChanged;
    document.PassOutputVerticalScrollChanged -= DocumentPassOutputVerticalScrollChanged;
    document.PassOutputVisibilityChanged -= Document_PassOutputVisibilityChanged;
    document.PassOutputShowBeforeChanged -= Document_PassOutputShowBeforeChanged;
  }

  private void DocumentVerticalScrollChanged(object sender, (double offset, double offsetChangeAmount) value) {
    if (!IsInDiffMode || Math.Abs(value.offsetChangeAmount) < double.Epsilon) {
      return;
    }

    var document = sender as IRDocumentHost;
    var otherDocument = sessionState_.SectionDiffState.GetOtherDocument(document);

    if (otherDocument != null) {
      otherDocument.TextView.ScrollToVerticalOffset(value.offset);
    }
  }

  private async void Document_PassOutputShowBeforeChanged(object sender, bool e) {
    sessionState_.SectionDiffState.PassOutputShowBefore = e;

    if (!IsInDiffMode) {
      return;
    }

    var document = sender as IRDocumentHost;
    var otherDocument = sessionState_.SectionDiffState.GetOtherDocument(document);

    if (otherDocument != null) {
      otherDocument.PassOutput.ShowBeforeOutput = e;
      await DiffDocumentPassOutput();
    }
  }

  private async void Document_PassOutputVisibilityChanged(object sender, bool e) {
    sessionState_.SectionDiffState.PassOutputVisible = e;

    if (!IsInDiffMode) {
      return;
    }

    var document = sender as IRDocumentHost;
    var otherDocument = sessionState_.SectionDiffState.GetOtherDocument(document);

    if (otherDocument != null) {
      otherDocument.PassOutputVisible = e;

      if (e) {
        await DiffDocumentPassOutput();
      }
    }
  }

  private void
    DocumentPassOutputVerticalScrollChanged(object sender, (double offset, double offsetChangeAmount) value) {
    if (!IsInDiffMode || Math.Abs(value.offsetChangeAmount) < double.Epsilon) {
      return;
    }

    var document = sender as IRDocumentHost;
    var otherDocument = sessionState_.SectionDiffState.GetOtherDocument(document);

    if (otherDocument != null) {
      otherDocument.PassOutput.TextView.ScrollToVerticalOffset(value.offset);
    }
  }

  private void TextView_CaretChanged(object sender, int caretOffset) {
    if (!IsInDiffMode) {
      return;
    }

    // Move the caret in the other document to the same position.
    var document = sender as IRDocument;
    var docHost = FindDocumentHost(document);

    if (!sessionState_.SectionDiffState.IsDiffDocument(docHost)) {
      return;
    }

    var otherDocHost = sessionState_.SectionDiffState.GetOtherDocument(docHost);
    otherDocHost.TextView.SetCaretAtOffset(caretOffset);
  }

  private async void TextView_ActionPerformed(object sender, DocumentAction e) {
    var document = sender as IRDocument;
    Debug.Assert(document != null);

    if (e.ActionKind == DocumentActionKind.ShowExpressionGraph) {
      var section = document.Section;
      using var loadTask = await ExpressionGraphPanel.OnGenerateGraphStart(section);

      var graphLayout = GetGraphLayoutCache(GraphKind.ExpressionGraph);
      var options = App.Settings.ExpressionGraphSettings.GetGraphPrinterOptions();
      options.IR = CompilerInfo.IR;
      var graph = graphLayout.GenerateGraph(e.Element, section, loadTask, options);

      if (graph != null) {
        ExpressionGraphPanel.DisplayGraph(graph);
        ExpressionGraphPanel.OnGenerateGraphDone(loadTask);
        var panelHost = FindPanelHost(ExpressionGraphPanel).Host;

        if (!panelHost.IsActive) {
          panelHost.IsActive = true;
        }
      }
      else {
        ExpressionGraphPanel.OnGenerateGraphDone(loadTask, true);
        Trace.TraceError($"Document {ObjectTracker.Track(document)}: Failed to load CFG");
      }

      return;
    }

    MirrorElementAction(e.Element, document,
                        (otherElement, otherDocument) => {
                          otherDocument.ExecuteDocumentAction(e.WithNewElement(otherElement));
                        });
  }

  private async void TextView_BlockSelected(object sender, IRElementEventArgs e) {
    var document = sender as IRDocument;

    if (document != null) {
      await NotifyPanelsOfElementSelection(e, document);
    }

    var block = e.Element.ParentBlock;
    UpdateBlockStatusBar(block);
  }

  private void TextView_BookmarkSelected(object sender, SelectedBookmarkInfo e) {
    var document = sender as IRDocument;
    var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
    bookmarksPanel.Bookmarks.Refresh();
    BookmarkStatus.Text = $"{e.SelectedIndex + 1} / {e.TotalBookmarks}";
  }

  private void TextView_BookmarkListCleared(object sender, EventArgs e) {
    var document = sender as IRDocument;
    var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
    bookmarksPanel.Bookmarks.Clear();
  }

  private void TextView_BookmarkRemoved(object sender, Bookmark e) {
    var document = sender as IRDocument;
    var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
    bookmarksPanel.Bookmarks.Remove(e);
  }

  private void TextView_BookmarkAdded(object sender, Bookmark e) {
    var document = sender as IRDocument;
    var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
    bookmarksPanel.Bookmarks.Add(e);
  }

  private void TextView_BookmarkChanged(object sender, Bookmark e) {
    var document = sender as IRDocument;
    var bookmarksPanel = FindTargetPanel<BookmarksPanel>(document, ToolPanelKind.Bookmarks);
    bookmarksPanel.Bookmarks.Refresh();
  }

  private async void TextView_IRElementHighlighting(object sender, IRHighlightingEventArgs e) {
    var document = sender as IRDocument;

    if (document != null) {
      await NotifyPanelsOfElementHighlight(e, document);
    }
  }

  private async void TextView_IRElementSelected(object sender, IRElementEventArgs e) {
    if (sender is IRDocument document) {
      await NotifyPanelsOfElementSelection(e, document);
    }
  }

  private void MirrorElementAction(IRElement element, IRDocument sourceDocument,
                                   Action<IRElement, IRDocument> action) {
    Trace.TraceInformation(
      $"Mirror action from {ObjectTracker.Track(sourceDocument)} on element {element}");

    foreach (var docInfo in sessionState_.DocumentHosts) {
      var otherDocument = docInfo.DocumentHost.TextView;

      // Skip if same section or from another function.
      if (otherDocument == sourceDocument ||
          otherDocument.Section.ParentFunction != sourceDocument.Section.ParentFunction) {
        continue;
      }

      if (element != null) {
        var finder = new ReferenceFinder(otherDocument.Function);
        var otherOp = finder.FindEquivalentValue(element);

        if (otherOp != null) {
          action(otherOp, otherDocument);
          continue;
        }
      }

      action(null, otherDocument);
    }
  }

  private void UpdateUIBeforeReadSession(string documentTitle) {
    if (!string.IsNullOrEmpty(documentTitle)) {
      Title = $"Profile Explorer - Reading {documentTitle}";
    }

    UpdatePanelEnabledState(false);
    Utils.DisableControl(StartPage, 0.85);
    Mouse.OverrideCursor = Cursors.AppStarting;
    SetApplicationProgress(true, double.NaN, "Loading session");
  }

  private void UpdateUIBeforeLoadDocument(string documentTitle) {
    if (!string.IsNullOrEmpty(documentTitle)) {
      Title = $"Profile Explorer - Loading {documentTitle}";
    }

    StartUIUpdate();
  }

  private void StartUIUpdate() {
    UpdatePanelEnabledState(false);
    Utils.DisableControl(StartPage, 0.85);
    Mouse.OverrideCursor = Cursors.AppStarting;
  }

  private void StopUIUpdate() {
    Mouse.OverrideCursor = null;
    UpdatePanelEnabledState(true);
  }

  private void UpdateUIAfterLoadDocument() {
    StopUIUpdate();
    loadingDocuments_ = false;

    if (sessionState_ != null) {
      UpdateWindowTitle();
    }
    else {
      Title = "Profile Explorer - Failed to load file";
      UpdatePanelEnabledState(false);
    }

    // Hide temporary UI.
    HideProgressBar();
  }

  private IRDocumentHost FindDocumentWithSection(IRTextSection section) {
    var result = sessionState_.DocumentHosts.Find(item => item.DocumentHost.Section == section);
    return result?.DocumentHost;
  }

  private async void DocumentState_DocumentChangedEvent(object sender, EventArgs e) {
    var loadedDoc = (ILoadedDocument)sender;
    var eventTime = DateTime.UtcNow;

    // Queue for later, when the application gets focus back.
    // A lock is needed, since the event can fire concurrently.
    lock (lockObject_) {
      changedDocuments_[loadedDoc.FilePath] = eventTime;
    }

    if (appIsActivated_) {
      // The event doesn't run on the main thread, redirect.
      await Dispatcher.BeginInvoke(new Action(async () => {
        await HandleChangedDocuments();
      }));
    }
  }

  private bool ShowDocumentReloadQuery(string filePath) {
    if (SilentMode) {
      return false;
    }

    lastDocumentReloadQueryTime_ = DateTime.UtcNow;

    using var centerForm = new DialogCenteringHelper(this);
    return MessageBox.Show(
             $"File {filePath} changed by an external application?\nDo you want to reload?",
             "Profile Explorer", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No,
             MessageBoxOptions.DefaultDesktopOnly) ==
           MessageBoxResult.Yes;
  }

  private async Task ReloadDocument(string filePath) {
    if (sessionState_.IsInTwoDocumentsDiffMode) {
      await CheckedOpenBaseDiffIRDocuments(sessionState_.MainDocument.FilePath,
                                           sessionState_.DiffDocument.FilePath);
    }
    else {
      await CheckedOpenDocument(filePath);
    }
  }

  private void StartAutoSaveTimer() {
    //? TODO: Disabled since saving of profile sessions is also not supported yet.
    // try {
    //   string filePath = Utils.GetAutoSaveFilePath();
    //
    //   if (File.Exists(filePath)) {
    //     File.Delete(filePath);
    //   }
    // }
    // catch (Exception) {
    //   Trace.TraceError("Failed to delete autosave file");
    // }
    //
    // //? TODO: For huge files, autosaving uses a lot of memory.
    // if (!sessionState_.Info.IsDebugSession) {
    //   try {
    //     long fileSize = new FileInfo(sessionState_.Info.FilePath).Length;
    //
    //     if (fileSize > SectionReaderBase.MAX_PRELOADED_FILE_SIZE) {
    //       Trace.TraceWarning(
    //         $"Disabling auto-saving for large file: {sessionState_.Info.FilePath}");
    //
    //       sessionState_.IsAutoSaveEnabled = false;
    //       return;
    //     }
    //   }
    //   catch (Exception ex) {
    //     Trace.TraceError($"Failed to get auto-saved file size: {ex}");
    //   }
    // }
    //
    // sessionState_.IsAutoSaveEnabled = true;
    // autoSaveTimer_ = new DispatcherTimer {Interval = TimeSpan.FromSeconds(300)};
    // autoSaveTimer_.Tick += async delegate { await AutoSaveSession().ConfigureAwait(false); };
    // autoSaveTimer_.Start();
  }

  private async Task AutoSaveSession() {
    //? TODO: Disabled since saving of profile sessions is also not supported yet.
    // if (sessionState_ == null || !sessionState_.IsAutoSaveEnabled) {
    //   return;
    // }
    //
    // string filePath = Utils.GetAutoSaveFilePath();
    // bool saved = await SaveSessionDocument(filePath).ConfigureAwait(false);
    // Trace.TraceInformation($"Auto-saved session: {saved}");
  }

  private void OpenNewDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!App.StartNewApplicationInstance()) {
      using var centerForm = new DialogCenteringHelper(this);
      MessageBox.Show("Failed to start new Profile Explorer instance", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private async Task CheckedOpenDocument(string filePath) {
    var loadedDoc = await OpenDocument(filePath);

    if (loadedDoc == null) {
      MessageBox.Show($"Filed to open binary file {filePath}", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }
  }

  private async Task CheckedOpenBaseDiffIRDocuments(string baseFilePath, string diffFilePath) {
    var (baseLoadedDoc, diffLoadedDoc) =
      await OpenBaseDiffDocuments(baseFilePath, diffFilePath);

    if (baseLoadedDoc == null || diffLoadedDoc == null) {
      MessageBox.Show($"Filed to open base/diff binary files {baseFilePath}\nand {diffFilePath}", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }
  }

  private async void CloseDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
    await EndSession(showStartPage: true);
  }

  private async void SaveDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!RequestSessionFilePath()) {
      return;
    }

    string filePath = sessionState_.Info.FilePath;
    bool loaded = await SaveSessionDocument(filePath);

    if (!loaded) {
      using var centerForm = new DialogCenteringHelper(this);
      MessageBox.Show($"Failed to save session file {filePath}", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }
  }

  private async void SaveAsDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!RequestSessionFilePath(true)) {
      return;
    }

    string filePath = sessionState_.Info.FilePath;
    bool loaded = await SaveSessionDocument(filePath);

    if (!loaded) {
      using var centerForm = new DialogCenteringHelper(this);
      MessageBox.Show($"Failed to save session file {filePath}", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }
  }

  private async void ReloadDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (sessionState_ != null && !sessionState_.Info.IsFileSession && !sessionState_.Info.IsDebugSession) {
      await ReloadDocument(sessionState_.Info.FilePath);
    }
  }

  private void AutoReloadDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (sessionState_ != null) {
      App.Settings.AutoReloadDocument = (e.OriginalSource as MenuItem).IsChecked;
      sessionState_.ChangeDocumentWatcherState(App.Settings.AutoReloadDocument);
    }
  }

  private void CanExecuteDocumentCommand(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = IsSessionStarted;
    e.Handled = true;
  }

  public ILoadedDocument FindLoadedDocument(IRTextFunction func) {
    return sessionState_.FindLoadedDocument(func);
  }

  public ICompilerInfoProvider CreateCompilerInfoProvider(IRMode mode) {
    return new ASMUICompilerInfoProvider(mode);
  }

  public ILoadedDocument CreateLoadedDocument(string filePath, string modulePath, Guid id) {
    return new LoadedDocument(filePath, modulePath, id);
  }

  public ILoadedDocument CreateDummyDocument(string name) {
    return LoadedDocument.CreateDummyDocument(name);
  }
}