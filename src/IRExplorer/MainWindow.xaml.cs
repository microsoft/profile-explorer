// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AutoUpdaterDotNET;
using AvalonDock.Layout;
using Client.DebugServer;
using Client.Document;
using Client.Query;
using CoreLib;
using CoreLib.Analysis;
using CoreLib.Graph;
using CoreLib.GraphViz;
using CoreLib.IR;
using CoreLib.IR.Tags;
using CoreLib.UTC;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;

namespace Client {
    public static class AppCommand {
        public static readonly RoutedUICommand FullScreen =
            new RoutedUICommand("Untitled", "FullScreen", typeof(Window));
        public static readonly RoutedUICommand OpenDocument =
            new RoutedUICommand("Untitled", "OpenDocument", typeof(Window));
        public static readonly RoutedUICommand OpenNewDocument =
            new RoutedUICommand("Untitled", "OpenNewDocument", typeof(Window));
        public static readonly RoutedUICommand CloseDocument =
            new RoutedUICommand("Untitled", "CloseDocument", typeof(Window));
        public static readonly RoutedUICommand SaveDocument =
            new RoutedUICommand("Untitled", "SaveDocument", typeof(Window));
        public static readonly RoutedUICommand SaveAsDocument =
            new RoutedUICommand("Untitled", "SaveAsDocument", typeof(Window));
        public static readonly RoutedUICommand ToggleDiffMode =
            new RoutedUICommand("Untitled", "ToggleDiffMode", typeof(Window));
        public static readonly RoutedUICommand ReloadDocument =
            new RoutedUICommand("Untitled", "ReloadDocument", typeof(Window));
        public static readonly RoutedUICommand AutoReloadDocument =
            new RoutedUICommand("Untitled", "AutoReloadDocument", typeof(Window));
        public static readonly RoutedUICommand OpenDiffDocument =
            new RoutedUICommand("Untitled", "OpenDiffDocument", typeof(Window));
        public static readonly RoutedUICommand OpenBaseDiffDocuments =
            new RoutedUICommand("Untitled", "OpenBaseDiffDocuments", typeof(Window));
        public static readonly RoutedUICommand CloseDiffDocument =
            new RoutedUICommand("Untitled", "CloseDiffDocument", typeof(Window));
    }

    public partial class MainWindow : Window, ISessionManager {
        private const char RemovedDiffLineChar = ' ';
        private const char AddedDiffLineChar = ' ';
        private static readonly string AUTO_UPDATE_LOCATION = @"\\ir-explorer\app\update.xml";
        private static readonly string DOCUMENTATION_LOCATION = @"file://ir-explorer/docs/index.html";

        private static readonly char[] IgnoredDiffLetters = {
            '(', ')', ',', '.', ';', ':', '|', '{', '}', '!'
        };
        private LayoutDocumentPane activeDocumentPanel_;
        private AddressMetadataTag addressTag_;
        private bool appIsActivated_;
        private DispatcherTimer autoSaveTimer_;
        private HashSet<string> changedDocuments_;
        private ICompilerInfoProvider compilerInfo_;

        private Dictionary<ElementIteratorId, IRElement> debugCurrentIteratorElement_;
        private StackFrame debugCurrentStackFrame_;
        private IRTextFunction debugFunction_;
        private long debugProcessId_;
        private int debugSectionId_;
        private DebugSectionLoader debugSections_;

        private DebugServer.DebugService debugService_;
        private IRTextSummary debugSummary_;
        private LoadedDocument diffDocument_;

        private MainWindowState fullScreenRestoreState_;

        private Dictionary<GraphKind, GraphLayoutCache> graphLayout_;
        private bool ignoreDiffModeButtonEvent_;
        private LoadedDocument mainDocument_;
        private Dictionary<ToolPanelKind, List<PanelHostInfo>> panelHostSet_;
        private IRTextSection previousDebugSection_;
        private SessionStateManager sessionState_;
        private bool sideBySidePanelsCreated_;
        private DispatcherTimer updateTimer_;

        public MainWindow() {
            App.WindowShowTime = DateTime.UtcNow;

            panelHostSet_ = new Dictionary<ToolPanelKind, List<PanelHostInfo>>();
            compilerInfo_ = new UTCCompilerInfoProvider();
            changedDocuments_ = new HashSet<string>();

            InitializeComponent();
            SetupMainWindow();
            SetupGraphLayoutCache();

            ContentRendered += MainWindow_ContentRendered;
            Closing += MainWindow_Closing;
            Activated += MainWindow_Activated;
            Deactivated += MainWindow_Deactivated;
        }

        public SessionStateManager SessionState => sessionState_;

        public IRTextSummary GetDocumentSummary(IRTextSection section) {
            return sessionState_.FindDocument(section).Summary;
        }

        public void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations) {
            SectionPanel.SetSectionAnnotationState(section, hasAnnotations);
        }

        public async Task<string> GetSectionPassOutputAsync(IRPassOutput output, IRTextSection section) {
            var docInfo = sessionState_.FindDocument(section);
            return await Task.Run(() => docInfo.Loader.LoadSectionPassOutput(output));
        }

        public async Task SwitchDocumentSection(OpenSectionEventArgs args, IRDocument document) {
            await SwitchDocumentSection(args, FindDocumentHost(document));
            SectionPanel.SelectSection(args.Section, false);
        }

        public async Task SwitchGraphsAsync(GraphPanel graphPanel, IRTextSection section,
                                            IRDocument document) {
            var action = GetComputeGraphAction(graphPanel.PanelKind);
            await SwitchGraphsAsync(graphPanel, section, document, action);
        }

        public List<Reference> FindAllReferences(IRElement element, IRDocument document) {
            var panelInfo = FindTargetPanel(document, ToolPanelKind.References);
            var refPanel = panelInfo.Panel as ReferencesPanel;
            panelInfo.Host.IsSelected = true;
            return refPanel.FindAllReferences(element);
        }

        public List<Reference> FindSSAUses(IRElement element, IRDocument document) {
            var panelInfo = FindTargetPanel(document, ToolPanelKind.References);
            var refPanel = panelInfo.Panel as ReferencesPanel;
            panelInfo.Host.IsSelected = true;
            return refPanel.FindSSAUses(element);
        }

        public void DuplicatePanel(IToolPanel panel, DuplicatePanelKind duplicateKind) {
            var newPanel = CreateNewPanel(panel.PanelKind);

            if (newPanel != null) {
                DisplayNewPanel(newPanel, panel, duplicateKind);
                RenamePanels(newPanel.PanelKind);
                newPanel.ClonePanel(panel);
            }
        }

        //? Replace with CurrentDocument(Panel), where a bound panel overrides FindActiveDocument

        public IRDocument CurrentDocument => FindActiveDocument()?.TextView;

        public IRTextSection CurrentDocumentSection {
            get {
                var activeDocument = FindActiveDocument();
                return activeDocument?.Section;
            }
        }

        public List<IRDocument> OpenDocuments {
            get {
                var list = new List<IRDocument>();
                sessionState_.DocumentHosts.ForEach(doc => list.Add(doc.Document.TextView));
                return list;
            }
        }

        public ICompilerInfoProvider CompilerInfo => compilerInfo_;

        public IRDocument FindAssociatedDocument(IToolPanel panel) {
            return panel.BoundDocument ?? FindActiveDocumentView();
        }

        public IRDocumentHost FindAssociatedDocumentHost(IToolPanel panel) {
            if (panel.BoundDocument != null) {
                return FindDocumentHost(panel.BoundDocument);
            }

            return FindActiveDocument();
        }

        public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section) {
            if (sessionState_.DiffState.IsEnabled) {
                return;
            }

            sessionState_.SavePanelState(stateObject, panel, section);
        }

        public object LoadPanelState(IToolPanel panel, IRTextSection section) {
            return sessionState_.DiffState.IsEnabled ? null : sessionState_.LoadPanelState(panel, section);
        }

        public void SaveDocumentState(object stateObject, IRTextSection section) {
            if (sessionState_.DiffState.IsEnabled) {
                if (section == sessionState_.DiffState.LeftSection ||
                    section == sessionState_.DiffState.RightSection) {
                    return;
                }
            }

            sessionState_.SaveDocumentState(stateObject, section);
        }

        public object LoadDocumentState(IRTextSection section) {
            if (sessionState_.DiffState.IsEnabled) {
                if (section == sessionState_.DiffState.LeftSection ||
                    section == sessionState_.DiffState.RightSection) {
                    return null;
                }
            }

            return sessionState_.LoadDocumentState(section);
        }

        public void PopulateBindMenu(IToolPanel panel, BindMenuItemsArgs args) {
            foreach (var doc in sessionState_.DocumentHosts) {
                var menuItem = new BindMenuItem {
                    Header = GetSectionName(doc.Document.Section),
                    ToolTip = doc.Document.Section.ParentFunction.Name,
                    Tag = doc.Document.TextView,
                    IsChecked = panel.BoundDocument == doc.Document.TextView
                };

                args.MenuItems.Add(menuItem);
            }
        }

        public void BindToDocument(IToolPanel panel, BindMenuItem args) {
            var document = args.Tag as IRDocument;

            if (panel.BoundDocument == document) {
                // Unbind on second click.
                panel.BoundDocument = null;
            }
            else {
                panel.BoundDocument = document;
            }
        }

        public bool SwitchToPreviousSection(IRTextSection section, IRDocument document) {
            int index = section.Number - 1;

            if (index > 0) {
                var prevSection = section.ParentFunction.Sections[index - 1];
                var docHost = FindDocumentHost(document);
                SectionPanel.SwitchToSection(prevSection, docHost);
                return true;
            }

            return false;
        }

        public bool SwitchToNextSection(IRTextSection section, IRDocument document) {
            int index = section.Number - 1;

            if (index < section.ParentFunction.SectionCount - 1) {
                var nextSection = section.ParentFunction.Sections[index + 1];
                var docHost = FindDocumentHost(document);
                SectionPanel.SwitchToSection(nextSection, docHost);
                return true;
            }

            return false;
        }

        public async Task<SectionSearchResult> SearchSectionAsync(
            SearchInfo searchInfo, IRTextSection section, IRDocument document) {
            var docInfo = sessionState_.FindDocument(section);
            var searcher = new SectionTextSearcher(docInfo.Loader);

            if (searchInfo.SearchAll) {
                var sections = section.ParentFunction.Sections;

                var result =
                    await searcher.SearchAsync(searchInfo.SearchedText, searchInfo.SearchKind, sections);

                var panelInfo = FindTargetPanel(document, ToolPanelKind.SearchResults);
                var searchPanel = panelInfo.Panel as SearchResultsPanel;
                searchPanel.UpdateSearchResults(result, searchInfo);

                if (result.Count > 0) {
                    panelInfo.Host.IsSelected = true;
                }

                return result.Find(item => item.Section == section);
            }

            // In diff mode, use the diff text being displayed, which may be different
            // than the original section text due to diff annotations.
            if (document.DiffModeEnabled) {
                return await searcher.SearchSectionWithTextAsync(document.Text, searchInfo.SearchedText,
                                                                 searchInfo.SearchKind, section);
            }

            return await searcher.SearchSectionAsync(searchInfo.SearchedText, searchInfo.SearchKind, section);
        }

        public void ReloadDocumentSettings(DocumentSettings newSettings, IRDocument document) {
            foreach (var docHostInfo in sessionState_.DocumentHosts) {
                if (docHostInfo.Document.TextView != document) {
                    docHostInfo.Document.Settings = newSettings;
                }
            }
        }

        public void ReloadRemarkSettings(RemarkSettings newSettings, IRDocument document) {
            foreach (var docHostInfo in sessionState_.DocumentHosts) {
                if (docHostInfo.Document.TextView != document) {
                    docHostInfo.Document.RemarkSettings = newSettings;
                }
            }
        }

        private void MainWindow_Deactivated(object sender, EventArgs e) {
            appIsActivated_ = false;
        }

        private async void MainWindow_Activated(object sender, EventArgs e) {
            appIsActivated_ = true;

            foreach (string changedDoc in changedDocuments_) {
                if (ShowDocumentReloadQuery(changedDoc)) {
                    await ReloadDocument(changedDoc);
                }
            }

            changedDocuments_.Clear();
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

        private void SetupMainWindow() {
            PopulateRecentFilesMenu();
            ThemeCombobox.SelectedIndex = App.Settings.ThemeIndex;
        }

        protected override void OnSourceInitialized(EventArgs e) {
            base.OnSourceInitialized(e);
            WindowPlacement.SetPlacement(this, App.Settings.MainWindowPlacement);
        }

        private void AddRecentFile(string path) {
            App.Settings.AddRecentFile(path);
            PopulateRecentFilesMenu();
        }

        private void PopulateRecentFilesMenu() {
            var savedItems = new List<object>();

            foreach (var item in RecentFilesMenu.Items) {
                if (item is MenuItem menuItem) {
                    if (menuItem.Tag == null) {
                        savedItems.Add(menuItem);
                    }
                }
                else {
                    savedItems.Add(item);
                }
            }

            RecentFilesMenu.Items.Clear();

            foreach (string path in App.Settings.RecentFiles) {
                var item = new MenuItem();
                item.Header = path;
                item.Tag = path;
                item.Click += RecentMenuItem_Click;
                RecentFilesMenu.Items.Add(item);
            }

            foreach (var item in savedItems) {
                RecentFilesMenu.Items.Add(item);
            }
        }

        private async void RecentMenuItem_Click(object sender, RoutedEventArgs e) {
            var menuItem = sender as MenuItem;

            if (menuItem?.Tag != null) {
                await OpenDocument((string) menuItem.Tag);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e) {
            App.Settings.MainWindowPlacement = WindowPlacement.GetPlacement(this);
            App.Settings.ThemeIndex = ThemeCombobox.SelectedIndex;
            App.SaveApplicationSettings();

            if (sessionState_ == null) {
                return;
            }

            if (sessionState_.Info.IsFileSession) {
                if (MessageBox.Show("Save session changes before closing?", "IR Explorer",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No,
                                    MessageBoxOptions.DefaultDesktopOnly) ==
                    MessageBoxResult.Yes) {
                    SaveDocumentExecuted(this, null);
                }
            }
            else {
                NotifyPanelsOfSessionSave();
                NotifyDocumentsOfSessionSave();

                if (SectionPanel.HasAnnotatedSections) {
                    if (MessageBox.Show("Save file changes as a new session before closing?", "IR Explorer",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No,
                                        MessageBoxOptions.DefaultDesktopOnly) ==
                        MessageBoxResult.Yes) {
                        SaveDocumentExecuted(this, null);
                    }
                }
            }
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e) {
            //? TODO: DEV ONLY
            var now = DateTime.UtcNow;
            var time = now - App.AppStartTime;
            DevMenuStartupTime.Header = $"Startup time: {time.TotalMilliseconds} ms";

            DelayedAction.StartNew(TimeSpan.FromSeconds(10), () => {
                Dispatcher.BeginInvoke(() =>
                    CheckForUpdate()
                );
            });
        }

        private static void CheckForUpdate() {
            try {
                AutoUpdater.Start(AUTO_UPDATE_LOCATION);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed update check: {ex}");
            }
        }

        private async void EndSession() {
            if (sessionState_ == null) {
                return; // Session not opened.
            }

            if (autoSaveTimer_ != null) {
                autoSaveTimer_.Stop();
                autoSaveTimer_ = null;
            }

            // Close all documents and notify all panels.
            NotifyPanelsOfSessionEnd();

            foreach (var docHostInfo in sessionState_.DocumentHosts) {
                CloseDocument(docHostInfo);
            }

            await ExitDocumentDiffState(true);
            sessionState_.DocumentChanged -= DocumentState_DocumentChangedEvent;
            sessionState_.EndSession();
            sessionState_ = null;
            mainDocument_ = null;
            diffDocument_ = null;
        }

        private void CloseDocument(DocumentHostInfo docHostInfo) {
            ResetDocumentEvents(docHostInfo.Document);
            docHostInfo.HostParent.Children.Remove(docHostInfo.Host);
        }

        private void StartSession(string filePath, SessionKind sessionKind) {
            sessionState_ = new SessionStateManager(filePath, sessionKind);
            sessionState_.DocumentChanged += DocumentState_DocumentChangedEvent;
            sessionState_.ChangeDocumentWatcherState(App.Settings.AutoReloadDocument);
            ClearGraphLayoutCache();
            AddRecentFile(filePath);
        }

        private async void DocumentState_DocumentChangedEvent(object sender, EventArgs e) {
            var loadedDoc = (LoadedDocument) sender;

            if (appIsActivated_) {
                // The event doesn't run on the main thread, redirect.
                await Dispatcher.BeginInvoke(async () => {
                    if (ShowDocumentReloadQuery(loadedDoc.FilePath)) {
                        await ReloadDocument(loadedDoc.FilePath);
                    }
                });
            }
            else {
                // Queue for later, when the application gets focus back.
                changedDocuments_.Add(loadedDoc.FilePath);
            }
        }

        private bool ShowDocumentReloadQuery(string filePath) {
            return MessageBox.Show(
                       $"File {filePath} changed by an external application?\nDo you want to reload?",
                       "IR Explorer", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No,
                       MessageBoxOptions.DefaultDesktopOnly) ==
                   MessageBoxResult.Yes;
        }

        private async Task ReloadDocument(string filePath) {
            if (diffDocument_ != null) {
                if (filePath == mainDocument_.FilePath || filePath == diffDocument_.FilePath) {
                    await OpenBaseDiffIRDocuments(mainDocument_.FilePath, diffDocument_.FilePath);
                    return;
                }
            }

            await OpenIRDocument(filePath);
        }

        private void SetupSectionPanel() {
            SectionPanel.CompilerInfo = compilerInfo_;
            SectionPanel.MainSummary = mainDocument_.Summary;
            SectionPanel.MainTitle = mainDocument_.FileName;

            if (diffDocument_ != null) {
                ShowSectionPanelDiffs(diffDocument_);
            }

            SectionPanel.OnSessionStart();
        }

        private void StartApplicationUpdateTimer() {
            AutoUpdater.RunUpdateAsAdmin = true;
            AutoUpdater.ShowRemindLaterButton = false;
            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            updateTimer_ = new DispatcherTimer {Interval = TimeSpan.FromMinutes(10)};
            updateTimer_.Tick += delegate { CheckForUpdate(); };
            updateTimer_.Start();
        }

        private void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args) {
            if (args == null) {
                return;
            }

            if (args.IsUpdateAvailable) {
                UpdateButton.Visibility = Visibility.Visible;
                UpdateButton.Tag = args;
            }
        }

        private void StartAutoSaveTimer() {
            try {
                string filePath = Utils.GetAutoSaveFilePath();

                if (File.Exists(filePath)) {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex) {
                Trace.TraceError("Failed to delete autosave file");
            }

            //? TODO: For huge files, autosaving uses a lot of memory.
            if (!sessionState_.Info.IsDebugSession) {
                try {
                    long fileSize = new FileInfo(sessionState_.Info.FilePath).Length;

                    if (fileSize > UTCReader.MAX_PRELOADED_FILE_SIZE) {
                        Trace.TraceWarning(
                            $"Disabling auto-saving for large file: {sessionState_.Info.FilePath}");

                        sessionState_.IsAutoSaveEnabled = false;
                        return;
                    }
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to get auto-saved file size: {ex}");
                }
            }

            sessionState_.IsAutoSaveEnabled = true;
            autoSaveTimer_ = new DispatcherTimer {Interval = TimeSpan.FromSeconds(120)};
            autoSaveTimer_.Tick += async delegate { await AutoSaveSession(); };
            autoSaveTimer_.Start();
        }

        private async Task AutoSaveSession() {
            if (sessionState_ == null || !sessionState_.IsAutoSaveEnabled) {
                return;
            }

            string filePath = Utils.GetAutoSaveFilePath();
            bool saved = await SaveSessionDocument(filePath);
            Trace.TraceInformation($"Auto-saved session: {saved}");
        }

        private async Task<bool> OpenIRDocument(string filePath) {
            try {
                EndSession();
                UpdateUIBeforeLoadDocument(filePath);
                var result = await Task.Run(() => LoadDocument(filePath));

                if (result != null) {
                    SetupOpenedIRDocument(SessionKind.Default, filePath, result);
                    return true;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load document: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return false;
        }

        private void SetupOpenedIRDocument(SessionKind sessionKind, string filePath, LoadedDocument result) {
            mainDocument_ = result;
            StartSession(filePath, sessionKind);
            sessionState_.NewLoadedDocument(result);
            UpdateUIAfterLoadDocument();
            SetupSectionPanel();
            NotifyPanelsOfSessionStart();
            StartAutoSaveTimer();
        }

        private async Task<bool> OpenDiffIRDocument(string filePath) {
            try {
                var result = await Task.Run(() => LoadDocument(filePath));

                if (result != null) {
                    SetupOpenedDiffIRDocument(filePath, result);
                    return true;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load diff document: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return false;
        }

        private void SetupOpenedDiffIRDocument(string diffFilePath, LoadedDocument result) {
            diffDocument_ = result;
            sessionState_.NewLoadedDocument(result);
            UpdateUIAfterLoadDocument();
            ShowSectionPanelDiffs(result);
        }

        private void ShowSectionPanelDiffs(LoadedDocument result) {
            SectionPanel.DiffSummary = result.Summary;
            SectionPanel.DiffTitle = result.FileName;
        }

        private async Task<bool> LoadSessionDocument(SessionState state) {
            try {
                //? TODO: Proper support for multiple docs
                StartSession(state.Info.FilePath, SessionKind.FileSession);
                bool failed = false;

                foreach (var docState in state.Documents) {
                    var result = await Task.Run(() => LoadDocument(docState.DocumentText, docState.FilePath));

                    if (result != null) {
                        sessionState_.NewLoadedDocument(result);

                        if (mainDocument_ == null) {
                            mainDocument_ = result;
                        }
                        else {
                            Debug.Assert(diffDocument_ == null);
                            diffDocument_ = result;
                        }
                    }
                    else {
                        failed = true;
                    }
                }

                UpdateUIAfterLoadDocument();
                InitializeFromLoadedSession(state);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load in-memory document: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return false;
        }

        private void UpdateUIBeforeReadSession(string documentTitle) {
            Title = $"IR Explorer - Reading {documentTitle}";
            Utils.DisableControl(DockManager, 0.75);
            Mouse.OverrideCursor = Cursors.Wait;
        }

        private void UpdateUIBeforeLoadDocument(string documentTitle) {
            Title = $"IR Explorer - Loading {documentTitle}";
            Utils.DisableControl(DockManager, 0.75);
            Mouse.OverrideCursor = Cursors.Wait;
        }

        private void UpdateUIAfterLoadDocument() {
            Mouse.OverrideCursor = null;

            if (sessionState_ != null) {
                UpdateWindowTitle();
            }
            else {
                Title = "IR Explorer - Failed to load file";
            }
        }

        private void UpdateWindowTitle() {
            string title = "IR Explorer";

            if (sessionState_.Documents.Count == 1) {
                title += $" - {sessionState_.Documents[0].FilePath}";
            }
            else if (sessionState_.Documents.Count == 2) {
                title +=
                    $" - Diff: {sessionState_.Documents[0].FilePath}  |  {sessionState_.Documents[1].FilePath}";
            }

            Title = title;
            Utils.EnableControl(DockManager);
        }

        private LoadedDocument LoadDocument(string path) {
            try {
                var result = new LoadedDocument(path);
                result.Loader = new DocumentSectionLoader(path);
                result.Summary = result.Loader.LoadDocument();
                return result;
            }
            catch (Exception ex) {
                Trace.TraceError("$Failed to load document {path}");
                return null;
            }
        }

        private LoadedDocument LoadDocument(byte[] data, string path) {
            try {
                var result = new LoadedDocument(path);
                result.Loader = new DocumentSectionLoader(data);
                result.Summary = result.Loader.LoadDocument();
                return result;
            }
            catch (Exception ex) {
                Trace.TraceError("$Failed to load in-memory document");
                return null;
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e) {
            SectionPanel.OpenSection += SectionPanel_OpenSection;
            SectionPanel.EnterDiffMode += SectionPanel_EnterDiffMode;
            RegisterDefaultToolPanels();
            ResetStatusBar();

            //TelemetryConfiguration configuration = TelemetryConfiguration.CreateDefault();
            //configuration.DisableTelemetry = false;
            //configuration.InstrumentationKey = "da7d4359-13f9-40c0-a2bb-c3fb54a76275";
            //var telemetryClient = new TelemetryClient(configuration);
            //telemetryClient.TrackEvent("IRX: Start");
            //telemetryClient.Flush();
            var args = Environment.GetCommandLineArgs();

            if (args.Length >= 3) {
                string baseFilePath = args[1];
                string diffFilePath = args[2];

                if (File.Exists(baseFilePath) && File.Exists(diffFilePath)) {
                    await OpenBaseDiffIRDocuments(baseFilePath, diffFilePath);
                }
            }
            else if (args.Length == 2) {
                string filePath = args[1];

                if (File.Exists(filePath)) {
                    await OpenDocument(filePath);
                }
            }

            foreach (string arg in args) {
                if (arg.Contains("grpc-server")) {
                    StartGrpcServer();
                    break;
                }
            }

            if (sessionState_ == null) {
                Utils.DisableControl(DockManager, 0.75);
            }

            StartApplicationUpdateTimer();
        }

        private IRDocumentHost FindDocumentWithSection(IRTextSection section) {
            var result = sessionState_.DocumentHosts.Find(item => item.Document.Section == section);
            return result?.Document;
        }

        private async void SectionPanel_EnterDiffMode(object sender, DiffModeEventArgs e) {
            if (sessionState_.DiffState.IsEnabled) {
                sessionState_.DiffState.IsEnabled = false;
            }

            ignoreDiffModeButtonEvent_ = true;
            sessionState_.DiffState.StartModeChange();
            var leftDocument = FindDocumentWithSection(e.Left.Section);
            var rightDocument = FindDocumentWithSection(e.Right.Section);

            Trace.TraceInformation($"Diff mode: Start with left doc. {ObjectTracker.Track(leftDocument)}, " +
                                   $"right doc. {ObjectTracker.Track(rightDocument)}");

            leftDocument = await SwitchDocumentSection(e.Left, leftDocument, false);
            rightDocument = await SwitchDocumentSection(e.Right, rightDocument, false);
            bool result = await EnterDocumentDiffState(leftDocument, rightDocument);
            DiffModeButton.IsChecked = result;
            sessionState_.DiffState.EndModeChange();
            ignoreDiffModeButtonEvent_ = false;
            Trace.TraceInformation("Diff mode: Entered");
        }

        private void RegisterDefaultToolPanels() {
            RegisterPanel(SectionPanel, SectionPanelHost);
            RegisterPanel(FlowGraphPanel, FlowGraphPanelHost);
            RegisterPanel(DominatorTreePanel, DominatorTreePanelHost);
            RegisterPanel(PostDominatorTreePanel, PostDominatorTreePanelHost);
            RegisterPanel(DefinitionPanel, DefinitionPanelHost);
            RegisterPanel(IRInfoPanel, IRInfoPanelHost);
            RegisterPanel(SourceFilePanel, SourceFilePanelHost);
            RegisterPanel(BookmarksPanel, BookmarksPanelHost);
            RegisterPanel(ReferencesPanel, ReferencesPanelHost);
            RegisterPanel(NotesPanel, NotesPanelHost);
            RegisterPanel(PassOutputPanel, PassOutputHost);
            RegisterPanel(SearchPanel, SearchPanelHost);
            RegisterPanel(ScriptingPanel, ScriptingPanelHost);
            RegisterPanel(ExpressionGraphPanel, ExpressionGraphPanelHost);
            RenameAllPanels();
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
            document.ScrollChanged += Document_ScrollChanged;
        }

        private void Document_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            if (!sessionState_.DiffState.IsEnabled || Math.Abs(e.VerticalChange) < double.Epsilon) {
                return;
            }

            var document = sender as IRDocumentHost;

            if (sessionState_.DiffState.LeftDocument == document) {
                sessionState_.DiffState.RightDocument.TextView.ScrollToVerticalOffset(e.VerticalOffset);
            }
            else if (sessionState_.DiffState.RightDocument == document) {
                sessionState_.DiffState.LeftDocument.TextView.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }

        private void TextView_ActionPerformed(object sender, DocumentAction e) {
            var document = sender as IRDocument;
            Debug.Assert(document != null);

            if (e.ActionKind == DocumentActionKind.ShowExpressionGraph) {
                var section = document.Section;
                var loadTask = ExpressionGraphPanel.OnGenerateGraphStart(section);
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

        private void ResetDocumentEvents(IRDocumentHost document) {
            document.TextView.ElementSelected -= TextView_IRElementSelected;
            document.TextView.ElementHighlighting -= TextView_IRElementHighlighting;
            document.TextView.BlockSelected -= TextView_BlockSelected;
            document.TextView.BookmarkAdded -= TextView_BookmarkAdded;
            document.TextView.BookmarkRemoved -= TextView_BookmarkRemoved;
            document.TextView.BookmarkChanged -= TextView_BookmarkChanged;
            document.TextView.BookmarkSelected -= TextView_BookmarkSelected;
            document.TextView.BookmarkListCleared -= TextView_BookmarkListCleared;
            document.ScrollChanged -= Document_ScrollChanged;
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

        private void TextView_IRElementHighlighting(object sender, IRHighlightingEventArgs e) {
            var document = sender as IRDocument;

            if (document != null) {
                NotifyPanelsOfElementHighlight(e, document);
            }
        }

        private void TextView_IRElementSelected(object sender, IRElementEventArgs e) {
            if (sender is IRDocument document) {
                NotifyPanelsOfElementSelection(e, document);
            }
        }

        private void MirrorElementAction(IRElement element, IRDocument sourceDocument,
                                         Action<IRElement, IRDocument> action) {
            Trace.TraceInformation(
                $"Mirror action from {ObjectTracker.Track(sourceDocument)} on element {element}");

            foreach (var docInfo in sessionState_.DocumentHosts) {
                var otherDocument = docInfo.Document.TextView;

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

        private void NotifyPanelsOfElementEvent(HandledEventKind eventKind, IRDocument document,
                                                Action<IToolPanel> action) {
            foreach (var (kind, list) in panelHostSet_) {
                foreach (var panelHost in list) {
                    var panel = panelHost.Panel;

                    if (!panel.IsPanelEnabled) {
                        continue;
                    }

                    // Accept enabled panels handling the event.
                    if (eventKind == HandledEventKind.None || (panel.HandledEvents & eventKind) != 0) {
                        // Don't notify panels bound to another document
                        // or with pinned content.
                        if (ShouldNotifyPanel(panel, document)) {
                            action(panel);
                        }
                    }
                }
            }
        }

        private void NotifyPanelsOfSessionStart() {
            ForEachPanel(panel => { panel.OnSessionStart(); });
        }

        private void NotifyPanelsOfSessionEnd() {
            ForEachPanel(panel => {
                panel.OnSessionEnd();
                panel.BoundDocument = null;
            });
        }

        private void NotifyPanelsOfSessionSave() {
            ForEachPanel(panel => { panel.OnSessionSave(); });
        }

        private void NotifyDocumentsOfSessionSave() {
            sessionState_.DocumentHosts.ForEach(document => { document.Document.OnSessionSave(); });
        }

        private void NotifyPanelsOfSectionLoad(IRTextSection section, IRDocumentHost document,
                                               bool notifyAll) {
            ForEachPanel(panel => {
                if (ShouldNotifyPanel(panel, document.TextView, notifyAll)) {
                    panel.OnDocumentSectionLoaded(section, document.TextView);
                }
            });
        }

        private void NotifyPanelsOfSectionUnload(IRTextSection section, IRDocumentHost document,
                                                 bool notifyAll, bool ignoreBoundPanels = false) {
            ForEachPanel(panel => {
                if (ShouldNotifyPanel(panel, document.TextView, notifyAll, ignoreBoundPanels)) {
                    panel.OnDocumentSectionUnloaded(section, document.TextView);
                }
            });
        }

        private bool ShouldNotifyPanel(IToolPanel panel, IRDocument document, bool notifyAll = false,
                                       bool ignoreBoundPanels = false) {
            // Don't notify panels bound to another document or with pinned content.
            // If all panels should be notified, pinning in ignored.
            if (panel.BoundDocument == null) {
                if (notifyAll) {
                    return true;
                }
                else {
                    return !panel.HasPinnedContent;
                }
            }
            else {
                if (ignoreBoundPanels) {
                    return false;
                }

                return panel.BoundDocument == document;
            }
        }

        private void NotifyPanelsOfElementHighlight(IRHighlightingEventArgs e, IRDocument document) {
            NotifyPanelsOfElementEvent(HandledEventKind.ElementHighlighting, document,
                                       panel => panel.OnElementHighlighted(e));
        }

        private void NotifyPanelsOfElementSelection(IRElementEventArgs e, IRDocument document) {
            NotifyPanelsOfElementEvent(HandledEventKind.ElementSelection, document,
                                       panel => panel.OnElementSelected(e));
        }

        private void TextView_BlockSelected(object sender, IRElementEventArgs e) {
            var document = sender as IRDocument;

            if (document != null) {
                NotifyPanelsOfElementSelection(e, document);
            }

            var block = e.Element.ParentBlock;
            UpdateBlockStatusBar(block);
        }

        private void UpdateBlockStatusBar(BlockIR block) {
            string text = Utils.MakeBlockDescription(block);
            BlockStatus.Text = text;
            BlockStatus.ToolTip = text;
        }

        private void ResetStatusBar() {
            BlockStatus.Text = "";
            BlockStatus.ToolTip = null;
            BookmarkStatus.Text = "";
            BookmarkStatus.ToolTip = "";
            DiffStatusText.Text = "";
            DiffStatusText.ToolTip = "";
            OptionalStatusText.Text = "";
            OptionalStatusText.ToolTip = "";
        }

        private async void SectionPanel_OpenSection(object sender, OpenSectionEventArgs args) {
            await SwitchDocumentSection(args, args.TargetDocument);
        }

        private async Task<IRDocumentHost> SwitchDocumentSection(OpenSectionEventArgs args,
                                                                 IRDocumentHost targetDocument = null,
                                                                 bool awaitExtraTasks = true) {
            var document = targetDocument;

            if (document == null &&
                (args.OpenKind == OpenSectionKind.ReplaceCurrent ||
                 args.OpenKind == OpenSectionKind.ReplaceLeft ||
                 args.OpenKind == OpenSectionKind.ReplaceRight)) {
                document = FindSameSummaryDocument(args.Section);

                if (document == null && args.OpenKind == OpenSectionKind.ReplaceCurrent) {
                    document = FindActiveDocument();
                }
            }

            if (document == null ||
                (targetDocument == null &&
                 (args.OpenKind == OpenSectionKind.NewTab ||
                  args.OpenKind == OpenSectionKind.NewTabDockLeft ||
                  args.OpenKind == OpenSectionKind.NewTabDockRight))) {
                document = AddNewDocument(args.OpenKind).Document;
            }

            // In diff mode, reload both left/right sections and redo the diffs.
            if (sessionState_.DiffState.IsEnabled && sessionState_.DiffState.IsDiffDocument(document)) {
                await SwitchDiffedDocumentSection(args.Section, document);
                return document;
            }

            var parsedSection = await SwitchSection(args.Section, document);

            if (awaitExtraTasks) {
                await GenerateGraphs(args.Section, document.TextView);
            }
            else {
                GenerateGraphs(args.Section, document.TextView, false);
            }

            return document;
        }

        private IRTextSection FindDiffDocumentSection(IRTextSection section, LoadedDocument diffDoc) {
            SectionPanel.SelectSectionFunction(section);
            return SectionPanel.FindDiffDocumentSection(section);
        }

        private async Task SwitchDiffedDocumentSection(IRTextSection section, IRDocumentHost document,
                                                       bool redoDiffs = true) {
            string leftText = null;
            string rightText = null;
            IRTextSection newLeftSection = null;
            IRTextSection newRightSection = null;

            if (document == sessionState_.DiffState.LeftDocument) {
                var result1 = await Task.Run(() => LoadSectionText(section));
                leftText = result1.Text;
                newLeftSection = section;

                if (diffDocument_ != null) {
                    var diffSection = FindDiffDocumentSection(section, diffDocument_);

                    if (diffSection != null) {
                        var result2 = await Task.Run(() => LoadSectionText(diffSection));
                        rightText = result2.Text;
                        newRightSection = diffSection;
                    }
                    else {
                        rightText = $"Diff document does not have section {section.Name}";
                        return;
                    }
                }
                else {
                    var result2 =
                        await Task.Run(() => LoadSectionText(sessionState_.DiffState.RightDocument.Section));

                    rightText = result2.Text;
                }
            }
            else if (document == sessionState_.DiffState.RightDocument) {
                var result1 = await Task.Run(() => LoadSectionText(section));
                rightText = result1.Text;
                newRightSection = section;

                if (diffDocument_ != null) {
                    var diffSection = FindDiffDocumentSection(section, mainDocument_);

                    if (diffSection != null) {
                        var result2 = await Task.Run(() => LoadSectionText(diffSection));
                        leftText = result2.Text;
                        newLeftSection = diffSection;
                    }
                    else {
                        leftText = $"Diff document does not have section {section.Name}";
                        return;
                    }
                }
                else {
                    var result2 =
                        await Task.Run(() => LoadSectionText(sessionState_.DiffState.LeftDocument.Section));

                    leftText = result2.Text;
                }
            }
            else {
                // Document is not part of the diff set.
                return;
            }

            if (newLeftSection != null) {
                UpdateUIAfterSectionLoad(newLeftSection, sessionState_.DiffState.LeftDocument);
            }

            if (newRightSection != null) {
                UpdateUIAfterSectionLoad(newRightSection, sessionState_.DiffState.RightDocument);
            }

            await DiffDocuments(sessionState_.DiffState.LeftDocument.TextView,
                                sessionState_.DiffState.RightDocument.TextView, leftText, rightText,
                                newLeftSection, newRightSection);
        }

        private void NotifyOfSectionUnload(IRDocumentHost document, bool notifyAll,
                                           bool ignoreBoundPanels = false,
                                           bool switchingActiveDocument = false) {
            var section = document.Section;

            if (section != null) {
                document.UnloadSection(section, switchingActiveDocument);
                NotifyPanelsOfSectionUnload(section, document, notifyAll, ignoreBoundPanels);
            }
        }

        private async void ReloadDocumentSection(IRDocumentHost document) {
            if (document.Section != null) {
                await SwitchSection(document.Section, document);
            }
        }

        private async Task<ParsedSection> SwitchSection(IRTextSection section, IRDocumentHost document) {
            Trace.TraceInformation(
                $"Document {ObjectTracker.Track(document)}: Switch to section ({section.Number}) {section.Name}");

            NotifyOfSectionUnload(document, true);
            ResetDocumentEvents(document);
            ResetStatusBar();
            var delayedAction = UpdateUIBeforeSectionLoad(section, document);
            var result = await Task.Run(() => LoadSectionText(section));

            if (result.Function == null) {
                //? TODO: Handle load function failure better
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

            document.LoadSectionMinimal(result);
            NotifyPanelsOfSectionLoad(section, document, true);
            SetupDocumentEvents(document);
            document.LoadSection(result);
            UpdateUIAfterSectionLoad(section, document, delayedAction);
            return result;
        }

        private string FormatParsingErrors(ParsedSection result, string message) {
            if (!result.HadParsingErrors) {
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
                await SwitchGraphsAsync((GraphPanel) panelInfo.Panel, section, document, action);
            }
        }

        private Func<FunctionIR, IRTextSection, CancelableTaskInfo, Task<LayoutGraph>> GetComputeGraphAction(
            GraphKind graphKind) {
            return graphKind switch {
                GraphKind.FlowGraph         => ComputeFlowGraph,
                GraphKind.DominatorTree     => ComputeDominatorTree,
                GraphKind.PostDominatorTree => ComputePostDominatorTree,
                _                           => throw new InvalidOperationException("Unexpected graph kind!")
            };
        }

        private Func<FunctionIR, IRTextSection, CancelableTaskInfo, Task<LayoutGraph>> GetComputeGraphAction(
            ToolPanelKind graphKind) {
            return graphKind switch {
                ToolPanelKind.FlowGraph => ComputeFlowGraph,
                ToolPanelKind.DominatorTree => ComputeDominatorTree,
                ToolPanelKind.PostDominatorTree => ComputePostDominatorTree,
                _ => throw new InvalidOperationException("Unexpected graph kind!")
            };
        }

        public async Task SwitchGraphsAsync(GraphPanel graphPanel, IRTextSection section, IRDocument document,
                                            Func<FunctionIR, IRTextSection, CancelableTaskInfo,
                                                Task<LayoutGraph>> computeGraphAction) {
            //? TODO: When the section is changed quickly and there are long-running tasks,
            //? the CFG panel can get out of sync - the doc. tries to highlight a block
            //? for another CFG if the loading of the prev. CFG completes between loading the text
            //? and the caret reset event.
            var loadTask = graphPanel.OnGenerateGraphStart(section);

            var functionGraph =
                await Task.Run(() => computeGraphAction(document.Function, section, loadTask));

            if (functionGraph != null) {
                graphPanel.DisplayGraph(functionGraph);
                graphPanel.OnGenerateGraphDone(loadTask);
            }
            else {
                //? TODO: Handle CFG failure
                graphPanel.OnGenerateGraphDone(loadTask, true);
                Trace.TraceError($"Document {ObjectTracker.Track(document)}: Failed to load CFG");
            }
        }

        private DelayedAction UpdateUIBeforeSectionLoad(IRTextSection section, IRDocumentHost document) {
            document.Focusable = false;
            document.IsHitTestVisible = false;
            var delayedAction = new DelayedAction();
            delayedAction.Start(TimeSpan.FromMilliseconds(500), () => { document.Opacity = 0.5; });
            return delayedAction;
        }

        private void UpdateUIAfterSectionLoad(IRTextSection section, IRDocumentHost document,
                                              DelayedAction delayedAction = null) {
            delayedAction?.Cancel();
            document.Opacity = 1;
            document.Focusable = true;
            document.IsHitTestVisible = true;
            var docHostPair = FindDocumentHostPair(document);
            docHostPair.Host.Title = GetSectionName(section);
            docHostPair.Host.ToolTip = GetDocumentDescription(section);
            RenameAllPanels(); // For bound panels.
            SectionPanel.SelectSection(section, false);
        }

        private IRDocumentHost FindTargetDocument(IToolPanel panel) {
            if (panel.BoundDocument != null) {
                return FindDocumentHost(panel.BoundDocument);
            }

            return FindActiveDocument();
        }

        private void GraphViewer_GraphNodeSelected(object sender, IRElementEventArgs e) {
            var panel = ((GraphViewer) sender).HostPanel;
            var document = FindTargetDocument(panel);
            document.TextView.HighlightElement(e.Element, HighlighingType.Hovered);
        }

        private async Task<ParsedSection> LoadSectionText(IRTextSection section) {
            var docInfo = sessionState_.FindDocument(section);
            var parsedSection = docInfo.Loader.LoadSection(section);

            if (parsedSection.Function != null) {
                AnalyzeLoadedFunction(parsedSection.Function);
            }

            return parsedSection;
        }

        private void AnalyzeLoadedFunction(FunctionIR function) {
            var loopGraph = new LoopGraph(function);
            loopGraph.FindLoops();
            addressTag_ = function.GetTag<AddressMetadataTag>();
        }

        private async Task<LayoutGraph> ComputeFlowGraph(FunctionIR function, IRTextSection section,
                                                         CancelableTaskInfo loadTask) {
            var graphLayout = GetGraphLayoutCache(GraphKind.FlowGraph);
            return graphLayout.GenerateGraph(function, section, loadTask, (object) null);
        }

        private async Task<LayoutGraph> ComputeDominatorTree(FunctionIR function, IRTextSection section,
                                                             CancelableTaskInfo loadTask) {
            var graphLayout = GetGraphLayoutCache(GraphKind.DominatorTree);
            return graphLayout.GenerateGraph(function, section, loadTask, (object) null);
        }

        private async Task<LayoutGraph> ComputePostDominatorTree(FunctionIR function, IRTextSection section,
                                                                 CancelableTaskInfo loadTask) {
            var graphLayout = GetGraphLayoutCache(GraphKind.PostDominatorTree);
            return graphLayout.GenerateGraph(function, section, loadTask, (object) null);
        }

        private void SetupPanelEvents(PanelHostInfo panelHost) {
            panelHost.Host.Hiding += PanelHost_Hiding;

            switch (panelHost.PanelKind) {
                case ToolPanelKind.FlowGraph:
                case ToolPanelKind.DominatorTree:
                case ToolPanelKind.PostDominatorTree: {
                    var flowGraphPanel = panelHost.Panel as GraphPanel;
                    flowGraphPanel.GraphViewer.BlockSelected += GraphViewer_GraphNodeSelected;
                    flowGraphPanel.GraphViewer.BlockMarked += GraphViewer_BlockMarked;
                    flowGraphPanel.GraphViewer.BlockUnmarked += GraphViewer_BlockUnmarked;
                    flowGraphPanel.GraphViewer.GraphLoaded += GraphViewer_GraphLoaded;
                    break;
                }
                case ToolPanelKind.ExpressionGraph: {
                    var flowGraphPanel = panelHost.Panel as GraphPanel;
                    flowGraphPanel.GraphViewer.BlockSelected += GraphViewer_GraphNodeSelected;
                    flowGraphPanel.GraphViewer.GraphLoaded += GraphViewer_GraphLoaded;
                    break;
                }
            }
        }

        private void ResetPanelEvents(PanelHostInfo panelHost) {
            panelHost.Host.Hiding -= PanelHost_Hiding;

            switch (panelHost.PanelKind) {
                case ToolPanelKind.FlowGraph:
                case ToolPanelKind.DominatorTree:
                case ToolPanelKind.PostDominatorTree: {
                    var flowGraphPanel = panelHost.Panel as GraphPanel;
                    flowGraphPanel.GraphViewer.BlockSelected -= GraphViewer_GraphNodeSelected;
                    flowGraphPanel.GraphViewer.BlockMarked -= GraphViewer_BlockMarked;
                    flowGraphPanel.GraphViewer.BlockUnmarked -= GraphViewer_BlockUnmarked;
                    flowGraphPanel.GraphViewer.GraphLoaded -= GraphViewer_GraphLoaded;
                    break;
                }
            }
        }

        private void GraphViewer_BlockUnmarked(object sender, IRElementMarkedEventArgs e) {
            var panel = ((GraphViewer) sender).HostPanel;
            var document = FindTargetDocument(panel);
            document.TextView.UnmarkBlock(e.Element, HighlighingType.Marked);
        }

        private void GraphViewer_BlockMarked(object sender, IRElementMarkedEventArgs e) {
            var panel = ((GraphViewer) sender).HostPanel;
            var document = FindTargetDocument(panel);
            document.TextView.MarkBlock(e.Element, e.Style);
        }

        private void GraphViewer_GraphLoaded(object sender, EventArgs e) {
            var panel = ((GraphViewer) sender).HostPanel;
            var document = FindTargetDocument(panel);
            document.TextView.PanelContentLoaded(panel);
        }

        private async void ToggleButton_Checked(object sender, RoutedEventArgs e) {
            if (ignoreDiffModeButtonEvent_) {
                return;
            }

            bool result = await EnterDocumentDiffState();

            if (!result) {
                ignoreDiffModeButtonEvent_ = true;
                DiffModeButton.IsChecked = false;
                ignoreDiffModeButtonEvent_ = false;
            }
        }

        private async Task OpenDocument(string filePath) {
            bool loaded;

            if (Path.HasExtension(filePath) && Path.GetExtension(filePath) == ".irx") {
                loaded = await OpenSessionDocument(filePath);
            }
            else {
                loaded = await OpenIRDocument(filePath);
            }

            if (!loaded) {
                MessageBox.Show($"Failed to load file {filePath}", "IR Explorer", MessageBoxButton.OK,
                                MessageBoxImage.Exclamation);
            }
        }

        private async void OpenDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            string filePath = ShowOpenFileDialog();

            if (filePath != null) {
                await OpenDocument(filePath);
            }
        }

        private async void OpenDiffDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            string filePath = ShowOpenFileDialog();

            if (filePath != null) {
                bool loaded = await OpenDiffIRDocument(filePath);

                if (!loaded) {
                    MessageBox.Show($"Failed to load diff file {filePath}", "IR Explorer",
                                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
                else {
                    //if(sessionState_.Documents.Count == 2) {
                    //    ComputeFunctionDiffs(sessionState_.Documents[0].Summary.Functions[0],
                    //            sessionState_.Documents[1].Summary.Functions[0]);
                    //}
                }
            }
        }

        private async void OpenBaseDiffDocumentsExecuted(object sender, ExecutedRoutedEventArgs e) {
            var openWindow = new DiffOpenWindow();
            openWindow.Owner = this;
            var result = openWindow.ShowDialog();

            if (result.HasValue && result.Value) {
                bool loaded = await OpenBaseDiffIRDocuments(openWindow.BaseFilePath, openWindow.DiffFilePath);

                if (!loaded) {
                    MessageBox.Show("Failed to load base/diff files", "IR Explorer", MessageBoxButton.OK,
                                    MessageBoxImage.Exclamation);
                }
            }
        }

        private async Task<bool> OpenBaseDiffIRDocuments(string baseFilePath, string diffFilePath) {
            try {
                EndSession();
                UpdateUIBeforeLoadDocument($"Loading {baseFilePath}, {diffFilePath}");
                var baseTask = Task.Run(() => LoadDocument(baseFilePath));
                var diffTask = Task.Run(() => LoadDocument(diffFilePath));
                Task.WaitAll(baseTask, diffTask);

                if (baseTask.Result != null && diffTask.Result != null) {
                    SetupOpenedIRDocument(SessionKind.Default, baseFilePath, baseTask.Result);
                    SetupOpenedDiffIRDocument(diffFilePath, diffTask.Result);
                    return true;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load base/diff documents: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return false;
        }

        private async void CloseDiffDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (diffDocument_ != null) {
                await ExitDocumentDiffState();

                // Close each opened section associated with this document.
                var closedDocuments = new List<DocumentHostInfo>();

                foreach (var docHostInfo in sessionState_.DocumentHosts) {
                    var summary = docHostInfo.Document.Section.ParentFunction.ParentSummary;

                    if (summary == diffDocument_.Summary) {
                        CloseDocument(docHostInfo);
                        closedDocuments.Add(docHostInfo);
                    }
                }

                foreach (var docHostInfo in closedDocuments) {
                    sessionState_.DocumentHosts.Remove(docHostInfo);
                }

                sessionState_.RemoveLoadedDocuemnt(diffDocument_);

                // Reset the section panel.
                SectionPanel.DiffSummary = null;
                diffDocument_ = null;
                UpdateWindowTitle();
            }
        }

        private string ShowOpenFileDialog() {
            var fileDialog = new OpenFileDialog {
                DefaultExt = "*.*",
                Filter = "Log Files|*.txt;*.log;*.ir;*.irx|IR Explorer Session Files|*.irx|All Files|*.*"
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                return fileDialog.FileName;
            }

            return null;
        }

        private void OpenNewDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            string filePath = ShowOpenFileDialog();

            if (filePath != null) {
                if (!Utils.StartNewApplicationInstance(filePath)) {
                    MessageBox.Show("Failed to start new IR Explorer instance", "IR Explorer",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            EndSession();
        }

        private async void SaveDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!RequestSessionFilePath()) {
                return;
            }

            string filePath = sessionState_.Info.FilePath;
            bool loaded = await SaveSessionDocument(filePath);

            if (!loaded) {
                MessageBox.Show($"Failed to save session file {filePath}", "IR Explorer", MessageBoxButton.OK,
                                MessageBoxImage.Exclamation);
            }
        }

        private async void SaveAsDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!RequestSessionFilePath(true)) {
                return;
            }

            string filePath = sessionState_.Info.FilePath;
            bool loaded = await SaveSessionDocument(filePath);

            if (!loaded) {
                MessageBox.Show($"Failed to save session file {filePath}", "IR Explorer", MessageBoxButton.OK,
                                MessageBoxImage.Exclamation);
            }
        }

        private async void ReloadDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (sessionState_ != null && !sessionState_.Info.IsFileSession) {
                await ReloadDocument(sessionState_.Info.FilePath);
            }
        }

        private async void AutoReloadDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (sessionState_ != null) {
                App.Settings.AutoReloadDocument = (e.OriginalSource as MenuItem).IsChecked;
                sessionState_.ChangeDocumentWatcherState(App.Settings.AutoReloadDocument);
            }
        }

        private void CanExecuteDocumentCommand(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = sessionState_ != null;
            e.Handled = true;
        }

        private void CloseDiffDocumentCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = diffDocument_ != null;
            e.Handled = true;
        }

        private bool RequestSessionFilePath(bool forceNewFile = false) {
            if (!forceNewFile && sessionState_.Info.IsFileSession) {
                return true; // Save over same session file.
            }

            var fileDialog = new SaveFileDialog {
                DefaultExt = "*.irx",
                Filter = "IR Explorer Session File|*.irx"
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                sessionState_.Info.FilePath = fileDialog.FileName;
                sessionState_.Info.Kind = SessionKind.FileSession;
                UpdateWindowTitle();
                return true;
            }

            return false;
        }

        private void FullScreenExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Source != FullScreenButton) {
                FullScreenButton.IsChecked = !FullScreenButton.IsChecked;
            }

            if (FullScreenButton.IsChecked.HasValue && FullScreenButton.IsChecked.Value) {
                fullScreenRestoreState_ = new MainWindowState(this);
                MainMenu.Visibility = Visibility.Collapsed;
                MainGrid.RowDefinitions[0].Height = new GridLength(0);
                Visibility = Visibility.Collapsed;
                ResizeMode = ResizeMode.NoResize;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                Visibility = Visibility.Visible;
                Focus();
            }
            else if (fullScreenRestoreState_ != null) {
                fullScreenRestoreState_.Restore(this);
                Focus();
            }

            e.Handled = true;
        }

        private void CommandBinding_PreviewCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = true;
            e.Handled = true;
        }

        private void LayoutAnchorable_IsVisibleChanged(object sender, EventArgs e) {
            Debug.WriteLine("Visible " + sender.GetType());
        }

        private void LayoutAnchorable_IsActiveChanged(object sender, EventArgs e) {
            if (!(sender is LayoutAnchorable panelHost)) {
                return;
            }

            if (!(panelHost.Content is IToolPanel toolPanel)) {
                return;
            }

            if (panelHost.IsActive) {
                toolPanel.OnActivatePanel();
            }
            else {
                toolPanel.OnDeactivatePanel();
            }
        }

        private void LayoutAnchorable_IsSelectedChanged(object sender, EventArgs e) {
            if (!(sender is LayoutAnchorable panelHost)) {
                return;
            }

            if (!(panelHost.Content is IToolPanel toolPanel)) {
                return;
            }

            if (panelHost.IsSelected) {
                toolPanel.OnShowPanel();
            }
            else {
                toolPanel.OnHidePanel();
            }
        }

        private DocumentHostInfo AddNewDocument(OpenSectionKind kind) {
            var document = new IRDocumentHost(this);

            var host = new LayoutDocument {
                Content = document
            };

            switch (kind) {
                case OpenSectionKind.ReplaceCurrent:
                case OpenSectionKind.NewTab: {
                    if (activeDocumentPanel_ == null) {
                        activeDocumentPanel_ = new LayoutDocumentPane(host);
                        DocumentPanelGroup.Children.Add(activeDocumentPanel_);
                    }
                    else {
                        activeDocumentPanel_.Children.Add(host);
                    }

                    break;
                }
                case OpenSectionKind.NewTabDockLeft:
                case OpenSectionKind.ReplaceLeft: {
                    activeDocumentPanel_ = new LayoutDocumentPane(host);
                    DocumentPanelGroup.Children.Insert(0, activeDocumentPanel_);
                    break;
                }
                case OpenSectionKind.NewTabDockRight:
                case OpenSectionKind.ReplaceRight: {
                    activeDocumentPanel_ = new LayoutDocumentPane(host);
                    DocumentPanelGroup.Children.Add(activeDocumentPanel_);
                    break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }

            var documentHost = new DocumentHostInfo(document, host);
            documentHost.HostParent = activeDocumentPanel_;
            sessionState_.DocumentHosts.Add(documentHost);
            SetActiveDocument(documentHost);
            host.IsActiveChanged += DocumentHost_IsActiveChanged;
            host.Closed += DocumentHost_Closed;
            host.IsSelected = true;
            return documentHost;
        }

        private bool IsDiffModeDocument(IRDocumentHost document) {
            return sessionState_.DiffState.IsEnabled &&
                   (document == sessionState_.DiffState.LeftDocument ||
                    document == sessionState_.DiffState.RightDocument);
        }

        private async void DocumentHost_Closed(object sender, EventArgs e) {
            if (!(sender is LayoutDocument docHost)) {
                return;
            }

            var docHostInfo = FindDocumentHostPair(docHost);
            var document = docHostInfo.Document;

            // If the document is part of the active diff mode,
            // exit diff mode before removing it.
            if (IsDiffModeDocument(document)) {
                await ExitDocumentDiffState();
            }

            NotifyOfSectionUnload(document, true);
            UnbindPanels(docHostInfo.Document);
            RenameAllPanels();
            ResetDocumentEvents(docHostInfo.Document);
            sessionState_.DocumentHosts.Remove(docHostInfo);

            if (sessionState_.DocumentHosts.Count > 0) {
                var newActivePanel = sessionState_.DocumentHosts[0];
                activeDocumentPanel_ = newActivePanel.HostParent;
                newActivePanel.IsActiveDocument = true;
                NotifyPanelsOfSectionLoad(newActivePanel.Document.Section, newActivePanel.Document, false);
            }
            else {
                activeDocumentPanel_ = null;
            }
        }

        private void UnbindPanels(IRDocumentHost document) {
            foreach (var (kind, list) in panelHostSet_) {
                foreach (var panelInfo in list) {
                    if (panelInfo.Panel.BoundDocument == document.TextView) {
                        panelInfo.Panel.BoundDocument = null;
                    }
                }
            }
        }

        private void DocumentHost_IsActiveChanged(object sender, EventArgs e) {
            if (!(sender is LayoutDocument docHost)) {
                return;
            }

            if (!(docHost.Content is IRDocumentHost document)) {
                return;
            }

            if (docHost.IsSelected) {
                var activeDocument = FindActiveDocument();

                if (activeDocument == document) {
                    return; // Already active one.
                }

                if (activeDocument != null) {
                    NotifyOfSectionUnload(activeDocument, false, true, true);
                }

                var hostDocPair = FindDocumentHostPair(document);
                SetActiveDocument(hostDocPair);

                if (document.Section != null) {
                    var docInfo = sessionState_.FindDocument(document.Section);
                    SectionPanel.MainSummary = docInfo.Summary;
                    SectionPanel.SelectSection(document.Section, false);
                    NotifyPanelsOfSectionLoad(document.Section, document, false);
                }
            }
        }

        private DocumentHostInfo FindDocumentHostPair(IRDocumentHost document) {
            return sessionState_.DocumentHosts.Find(item => item.Document == document);
        }

        private DocumentHostInfo FindDocumentHostPair(LayoutDocument host) {
            return sessionState_.DocumentHosts.Find(item => item.Host == host);
        }

        private IRDocumentHost FindDocumentHost(IRDocument document) {
            return sessionState_.DocumentHosts.Find(item => item.Document.TextView == document).Document;
        }

        private IRDocumentHost FindSameSummaryDocument(IRTextSection section) {
            var summary = section.ParentFunction.ParentSummary;

            var results = sessionState_.DocumentHosts.FindAll(
                item => item.Document.Section != null &&
                        item.Document.Section.ParentFunction.ParentSummary ==
                        summary);

            // Try to pick the active document out of the list.
            foreach (var result in results) {
                if (result.IsActiveDocument) {
                    return result.Document;
                }
            }

            if (results.Count > 0) {
                return results[0].Document;
            }

            return null;
        }

        private IRDocumentHost FindActiveDocument() {
            var result = sessionState_.DocumentHosts.Find(item => item.IsActiveDocument);
            return result?.Document;
        }

        private IRDocument FindActiveDocumentView() {
            var result = sessionState_.DocumentHosts.Find(item => item.IsActiveDocument);
            return result?.Document?.TextView;
        }

        private void SetActiveDocument(DocumentHostInfo docHost) {
            foreach (var item in sessionState_.DocumentHosts) {
                item.IsActiveDocument = false;
            }

            docHost.IsActiveDocument = true;
        }

        private PanelHostInfo FindActivePanel(ToolPanelKind kind) {
            return panelHostSet_.TryGetValue(kind, out var list)
                ? list.Find(item => item.Panel.HasCommandFocus)
                : null;
        }

        private T FindActivePanel<T>(ToolPanelKind kind) where T : class {
            var panelHost = FindActivePanel(kind);
            return panelHost?.Panel as T;
        }

        private List<PanelHostInfo> FindTargetPanels(IRDocument document, ToolPanelKind kind) {
            var panelList = new List<PanelHostInfo>();

            if (panelHostSet_.TryGetValue(kind, out var list)) {
                // Add every panel not bound to another document.
                foreach (var panelInfo in list) {
                    if (panelInfo.Panel.BoundDocument == null || panelInfo.Panel.BoundDocument == document) {
                        panelList.Add(panelInfo);
                    }
                }
            }

            return panelList;
        }

        private PanelHostInfo FindTargetPanel(IRDocument document, ToolPanelKind kind) {
            if (panelHostSet_.TryGetValue(kind, out var list)) {
                // Use the panel bound to the document.
                var boundPanel = list.Find(item => item.Panel.BoundDocument == document);

                if (boundPanel != null) {
                    return boundPanel;
                }

                // Otherwise use, in order of preference:
                // - the last active panel that is unbound
                // - the last unbound panel
                // - the last active panel
                // - the last panel
                PanelHostInfo unboundPanelInfo = null;
                PanelHostInfo commandFocusPanelInfo = null;

                foreach (var item in list) {
                    if (item.Panel.HasCommandFocus) {
                        if (item.Panel.BoundDocument == null) {
                            return item;
                        }
                        else {
                            commandFocusPanelInfo = item;
                        }
                    }
                    else if (item.Panel.BoundDocument == null) {
                        unboundPanelInfo = item;
                    }
                }

                if (unboundPanelInfo != null) {
                    return unboundPanelInfo;
                }
                else if (commandFocusPanelInfo != null) {
                    return commandFocusPanelInfo;
                }

                return list[^1];
            }

            return null;
        }

        private T FindTargetPanel<T>(IRDocument document, ToolPanelKind kind) where T : class {
            var panelInfo = FindTargetPanel(document, kind);
            return panelInfo?.Panel as T;
        }

        private PanelHostInfo FindPanelHost(IToolPanel panel) {
            if (panelHostSet_.TryGetValue(panel.PanelKind, out var list)) {
                return list.Find(item => item.Panel == panel);
            }

            return null;
        }

        private PanelHostInfo FindPanel(LayoutAnchorable panelHost) {
            foreach (var (kind, list) in panelHostSet_) {
                var result = list.Find(item => item.Host == panelHost);

                if (result != null) {
                    return result;
                }
            }

            return null;
        }

        private string GetDefaultPanelName(ToolPanelKind kind) {
            return kind switch {
                ToolPanelKind.Bookmarks         => "Bookmarks",
                ToolPanelKind.Definition        => "Definition",
                ToolPanelKind.FlowGraph         => "Flow Graph",
                ToolPanelKind.DominatorTree     => "Dominator Tree",
                ToolPanelKind.PostDominatorTree => "Post-Dominator Tree",
                ToolPanelKind.ExpressionGraph   => "Expression Graph",
                ToolPanelKind.Developer         => "Developer",
                ToolPanelKind.Notes             => "Notes",
                ToolPanelKind.References        => "References",
                ToolPanelKind.Section           => "Sections",
                ToolPanelKind.Source            => "Source File",
                ToolPanelKind.PassOutput        => "Pass Output",
                ToolPanelKind.SearchResults     => "Search Results",
                ToolPanelKind.Scripting         => "Scripting",
                _                               => ""
            };
        }

        private void RenamePanels(ToolPanelKind kind) {
            if (panelHostSet_.TryGetValue(kind, out var list)) {
                RenamePanels(kind, list);
            }
        }

        private string GetPanelName(ToolPanelKind kind, IToolPanel panel) {
            string name = GetDefaultPanelName(kind);

            if (panel.BoundDocument != null) {
                name = $"{name} - Bound to S{panel.BoundDocument.Section.Number} ";
            }

            return name;
        }

        private void RenamePanels(ToolPanelKind kind, List<PanelHostInfo> list) {
            if (list.Count == 1) {
                list[0].Host.Title = GetPanelName(kind, list[0].Panel);
            }
            else {
                for (int i = 0; i < list.Count; i++) {
                    string name = GetPanelName(kind, list[i].Panel);
                    list[i].Host.Title = $"{name} ({i + 1})";
                }
            }
        }

        private void RenameAllPanels() {
            foreach (var (kind, list) in panelHostSet_) {
                RenamePanels(kind, list);
            }
        }

        private IToolPanel CreateNewPanel(ToolPanelKind kind) {
            return kind switch {
                ToolPanelKind.Definition    => new DefinitionPanel(),
                ToolPanelKind.References    => new ReferencesPanel(),
                ToolPanelKind.Notes         => new NotesPanel(),
                ToolPanelKind.PassOutput    => new PassOutputPanel(),
                ToolPanelKind.FlowGraph     => new GraphPanel(),
                ToolPanelKind.SearchResults => new SearchResultsPanel(),
                ToolPanelKind.Scripting     => new ScriptingPanel(),
                _                           => null
            };
        }

        private T CreateNewPanel<T>(ToolPanelKind kind) where T : class {
            return CreateNewPanel(kind) as T;
        }

        private PanelHostInfo DisplayNewPanel(IToolPanel newPanel, IToolPanel basePanel,
                                              DuplicatePanelKind duplicateKind) {
            var panelHost = AddNewPanel(newPanel);
            panelHost.Host.AddToLayout(DockManager, AnchorableShowStrategy.Right);
            var baseHost = FindPanelHost(basePanel).Host;
            bool attached = false;

            switch (duplicateKind) {
                case DuplicatePanelKind.Floating: {
                    panelHost.Host.Float();
                    break;
                }
                case DuplicatePanelKind.NewSetDockedLeft: {
                    var baseGroup = baseHost.FindParent<LayoutAnchorablePaneGroup>();

                    if (baseGroup == null) {
                        break;
                    }

                    baseGroup.Children.Insert(0, new LayoutAnchorablePane(panelHost.Host));
                    attached = true;
                    break;
                }
                case DuplicatePanelKind.NewSetDockedRight: {
                    var baseGroup = baseHost.FindParent<LayoutAnchorablePaneGroup>();

                    if (baseGroup == null) {
                        break;
                    }

                    baseGroup.Children.Add(new LayoutAnchorablePane(panelHost.Host));
                    attached = true;
                    break;
                }
                case DuplicatePanelKind.SameSet: {
                    // Insert the new panel on the right of the cloned one.
                    var basePanelHost = FindPanelHost(basePanel);
                    var baseLayoutPane = baseHost.FindParent<LayoutAnchorablePane>();

                    if (baseLayoutPane == null) {
                        break;
                    }

                    int basePaneIndex = baseLayoutPane.Children.IndexOf(basePanelHost.Host);
                    baseLayoutPane.Children.Insert(basePaneIndex + 1, panelHost.Host);
                    attached = true;
                    break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(duplicateKind), duplicateKind, null);
            }

            // Docking can fail if the target panel is hidden, make it a floating panel.
            //? TODO: Should try to make the hidden panel visible
            if (!attached) {
                panelHost.Host.Float();
            }

            panelHost.Host.IsSelected = true;
            return panelHost;
        }

        private PanelHostInfo AddNewPanel(IToolPanel panel) {
            return RegisterPanel(panel, new LayoutAnchorable {
                Content = panel
            });
        }

        private PanelHostInfo RegisterPanel(IToolPanel panel, LayoutAnchorable host) {
            var panelHost = new PanelHostInfo(panel, host);

            if (!panelHostSet_.TryGetValue(panel.PanelKind, out var list)) {
                list = new List<PanelHostInfo>();
            }

            list.Add(panelHost);
            panelHostSet_[panel.PanelKind] = list;

            // Setup events.
            SetupPanelEvents(panelHost);
            panel.OnRegisterPanel();
            panel.Session = this;

            // Make it the active panel in the group.
            SwitchCommandFocusToPanel(panelHost);
            return panelHost;
        }

        private void UnregisterPanel(PanelHostInfo panelHost) {
            if (panelHostSet_.TryGetValue(panelHost.PanelKind, out var list)) {
                list.Remove(panelHost);
                panelHost.Panel.OnUnregisterPanel();
                panelHost.Panel.Session = null;
            }
        }

        private void ForEachPanel(ToolPanelKind panelKind, Action<IToolPanel> action) {
            if (panelHostSet_.TryGetValue(panelKind, out var list)) {
                list.ForEach(item => action(item.Panel));
            }
        }

        private void ForEachPanel(Action<IToolPanel> action) {
            foreach (var (kind, list) in panelHostSet_) {
                list.ForEach(item => action(item.Panel));
            }
        }

        private void SwitchCommandFocusToPanel(PanelHostInfo panelHost) {
            var activePanel = FindActivePanel(panelHost.PanelKind);

            if (activePanel != null) {
                activePanel.Panel.HasCommandFocus = false;
            }

            panelHost.Panel.HasCommandFocus = true;
        }

        private void PickCommandFocusPanel(ToolPanelKind kind) {
            // Pick last panel without pinned content.
            if (!panelHostSet_.TryGetValue(kind, out var list)) {
                return;
            }

            if (list.Count == 0) {
                return;
            }

            var lastPanel = list.FindLast(item => !item.Panel.HasPinnedContent);

            if (lastPanel == null) {
                // If not found, just pick the last panel.
                lastPanel = list[^1];
            }

            lastPanel.Panel.HasCommandFocus = true;
        }

        private void PanelHost_Hiding(object sender, CancelEventArgs e) {
            var panelHost = sender as LayoutAnchorable;
            var panelInfo = FindPanel(panelHost);
            panelInfo.Panel.HasCommandFocus = false;

            // panelInfo.Panel.IsPanelEnabled = false;
            UnregisterPanel(panelInfo);
            ResetPanelEvents(panelInfo);
            RenamePanels(panelInfo.PanelKind);
            PickCommandFocusPanel(panelInfo.PanelKind);
        }

        private string GetSectionName(IRTextSection section) {
            string name = compilerInfo_.NameProvider.GetSectionName(section);
            return $"({section.Number}) {name}";
        }

        private string GetDocumentDescription(IRTextSection section) {
            var docInfo = sessionState_.FindDocument(section);
            return $"{section.ParentFunction.Name.Trim()} ({docInfo.FileName})";
        }

        private void MenuItem_Click_1(object sender, RoutedEventArgs e) {
            EndSession();
        }

        private async Task<bool> EnterDocumentDiffState() {
            if (sessionState_ == null) {
                // No session started yet.
            }

            sessionState_.DiffState.StartModeChange();

            if (sessionState_.DiffState.IsEnabled) {
                sessionState_.DiffState.EndModeChange();
                return true;
            }

            if (!PickLeftRightDocuments(out var leftDocument, out var rightDocument)) {
                sessionState_.DiffState.EndModeChange();
                return false;
            }

            bool result = await EnterDocumentDiffState(leftDocument, rightDocument);
            sessionState_.DiffState.EndModeChange();
            return result;
        }

        private async Task<bool> EnterDocumentDiffState(IRDocumentHost leftDocument,
                                                        IRDocumentHost rightDocument) {
            if (sessionState_ == null) {
                // No session started yet.
            }

            if (sessionState_.DiffState.IsEnabled) {
                return true;
            }

            sessionState_.DiffState.LeftDocument = leftDocument;
            sessionState_.DiffState.RightDocument = rightDocument;

            if (diffDocument_ != null) {
                // Used when diffing two different documents.
                sessionState_.DiffState.IsEnabled = true;
                await SwitchDiffedDocumentSection(leftDocument.Section, leftDocument, false);
            }
            else {
                sessionState_.DiffState.LeftSection = leftDocument.Section;
                sessionState_.DiffState.RightSection = rightDocument.Section;
                await DiffCurrentDocuments(sessionState_.DiffState);
            }

            // CreateDefaultSideBySidePanels();
            return true;
        }

        private bool PickLeftRightDocuments(out IRDocumentHost leftDocument,
                                            out IRDocumentHost rightDocument) {
            if (sessionState_.DocumentHosts.Count < 2) {
                leftDocument = rightDocument = null;
                return false;
            }

            // If one of the sections is already open, pick the associated document.
            // Otherwise, pick the last two created ones.
            leftDocument = sessionState_.DocumentHosts[^2].Document;
            rightDocument = sessionState_.DocumentHosts[^1].Document;
            return true;
        }

        private async Task ExitDocumentDiffState(bool isSessionEnding = false) {
            sessionState_.DiffState.StartModeChange();

            if (!sessionState_.DiffState.IsEnabled) {
                sessionState_.DiffState.EndModeChange();
                return;
            }

            var leftDocument = sessionState_.DiffState.LeftDocument;
            var rightDocument = sessionState_.DiffState.RightDocument;
            sessionState_.DiffState.End();

            if (!isSessionEnding) {
                // Reload sections in the same documents.
                Trace.TraceInformation("Diff mode: Reload original sections");
                var leftArgs = new OpenSectionEventArgs(leftDocument.Section, OpenSectionKind.ReplaceCurrent);

                var rightArgs =
                    new OpenSectionEventArgs(rightDocument.Section, OpenSectionKind.ReplaceCurrent);

                await SwitchDocumentSection(leftArgs, leftDocument);
                leftDocument.ExitDiffMode();
                await SwitchDocumentSection(rightArgs, rightDocument);
                rightDocument.ExitDiffMode();
            }

            ignoreDiffModeButtonEvent_ = true;
            DiffModeButton.IsChecked = false;
            ignoreDiffModeButtonEvent_ = false;
            sessionState_.DiffState.EndModeChange();
            Trace.TraceInformation("Diff mode: Exited");
        }

        private async void CreateDefaultSideBySidePanels() {
            if (sessionState_ == null) {
                //? TODO: Avoid this by disabling UI
                return; // Session not started yet.
            }

            Trace.TraceInformation("Creating default side-by-side panels");

            if (sideBySidePanelsCreated_) {
                return;
            }

            if (!PickLeftRightDocuments(out var leftDocument, out var rightDocument)) {
                return;
            }

            var rightGraphPanel = FindActivePanel<GraphPanel>(ToolPanelKind.FlowGraph);
            var leftGraphPanel = CreateNewPanel<GraphPanel>(ToolPanelKind.FlowGraph);
            var panelHost = AddNewPanel(leftGraphPanel);
            panelHost.Host.AddToLayout(DockManager, AnchorableShowStrategy.Most);
            LeftPanelGroup.Children.Insert(0, new LayoutAnchorablePane(panelHost.Host));
            leftGraphPanel.Width = LeftPanelGroup.DockWidth.Value;
            panelHost.Host.IsVisible = true;
            leftGraphPanel.BoundDocument = leftDocument.TextView;
            rightGraphPanel.BoundDocument = rightDocument.TextView;
            leftGraphPanel.InitializeFromDocument(leftDocument.TextView);
            rightGraphPanel.InitializeFromDocument(rightDocument.TextView);
            await GenerateGraphs(leftDocument.Section, leftDocument.TextView);
            await GenerateGraphs(rightDocument.Section, rightDocument.TextView);
            var rightRefPanel = FindActivePanel<ReferencesPanel>(ToolPanelKind.References);
            var leftRefPanel = CreateNewPanel<ReferencesPanel>(ToolPanelKind.References);
            DisplayNewPanel(leftRefPanel, rightRefPanel, DuplicatePanelKind.NewSetDockedLeft);
            leftRefPanel.BoundDocument = leftDocument.TextView;
            rightRefPanel.BoundDocument = rightDocument.TextView;
            leftRefPanel.InitializeFromDocument(leftDocument.TextView);
            rightRefPanel.InitializeFromDocument(rightDocument.TextView);
            FindPanelHost(rightRefPanel).Host.IsSelected = true;
            FindPanelHost(leftRefPanel).Host.IsSelected = true;
            RenameAllPanels();
            sideBySidePanelsCreated_ = true;
        }

        private async Task DiffCurrentDocuments(DiffModeInfo diffState) {
            diffState.LeftDocument.EnterDiffMode();
            diffState.RightDocument.EnterDiffMode();
            sessionState_.DiffState.IsEnabled = true;
            var leftDocument = diffState.LeftDocument.TextView;
            var rightDocument = diffState.RightDocument.TextView;
            await DiffDocuments(leftDocument, rightDocument, leftDocument.Text, rightDocument.Text);
        }

        private async Task DiffDocuments(IRDocument leftDocument, IRDocument rightDocument, string leftText,
                                         string rightText, IRTextSection newLeftSection = null,
                                         IRTextSection newRightSection = null) {
            var diff = await Task.Run(() => DocumentDiff.ComputeDiffs(leftText, rightText));
            var diffStats = new DiffStatistics();

            var leftMarkTask = MarkDiffs(leftText, diff.OldText, diff.NewText, leftDocument,
                                         false, diffStats);

            var rightMarkTask = MarkDiffs(leftText, diff.NewText, diff.OldText, rightDocument,
                                          true, diffStats);

            Task.WaitAll(leftMarkTask, rightMarkTask);
            var leftDiffResult = await leftMarkTask;
            var rightDiffResult = await rightMarkTask;
            sessionState_.DiffState.LeftSection = newLeftSection ?? sessionState_.DiffState.LeftSection;
            sessionState_.DiffState.RightSection = newRightSection ?? sessionState_.DiffState.RightSection;

            // The UI-thread dependent work.
            UpdateDiffedFunction(leftDocument, leftDiffResult, sessionState_.DiffState.LeftSection);
            UpdateDiffedFunction(rightDocument, rightDiffResult, sessionState_.DiffState.RightSection);

            // Scroll to the first diff.
            if (leftDiffResult.DiffSegments.Count > 0) {
                var firstDiff = leftDiffResult.DiffSegments[0];
                leftDocument.BringTextOffsetIntoView(firstDiff.StartOffset);
            }
            else if (rightDiffResult.DiffSegments.Count > 0) {
                var firstDiff = rightDiffResult.DiffSegments[0];
                rightDocument.BringTextOffsetIntoView(firstDiff.StartOffset);
            }

            DiffStatusText.Text = diffStats.ToString();
        }

        private void ReparseDiffedFunction(DiffMarkingResult diffResult) {
            try {
                var errorHandler = new UTCParsingErrorHandler();
                var sectionParser = new UTCSectionParser(errorHandler);
                diffResult.DiffFunction = sectionParser.ParseSection(null, diffResult.DiffText);

                if (diffResult.DiffFunction != null) {
                    AnalyzeLoadedFunction(diffResult.DiffFunction);
                }
                else {
                    Trace.TraceWarning("Failed re-parsing diffed section\n");
                }

                if (errorHandler.HadParsingErrors) {
                    Trace.TraceWarning("Errors while re-parsing diffed section:\n");

                    if (errorHandler.ParsingErrors != null) {
                        foreach (var error in errorHandler.ParsingErrors) {
                            Trace.TraceWarning($"  - {error}");
                        }
                    }
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Crashed while re-parsing diffed section: {ex}");
            }
        }

        private async Task UpdateDiffedFunction(IRDocument document, DiffMarkingResult diffResult,
                                                IRTextSection newSection) {
            UpdateDiffDocument(document, diffResult);
            var documentHost = FindDocumentHost(document);
            NotifyPanelsOfSectionUnload(document.Section, documentHost, true);

            if (diffResult.DiffFunction == null) {
                throw new InvalidOperationException();
            }

            document.LoadDiffedFunction(diffResult.DiffFunction, newSection);
            await GenerateGraphs(newSection, document, false);
            NotifyPanelsOfSectionLoad(document.Section, documentHost, true);
            MarkDiffsComplete(document, diffResult);
        }

        private bool IsSignifficantDiff(DiffPiece piece, DiffPiece otherPiece = null) {
            if (piece.Text == null) {
                return false;
            }

            bool signifficant = false;

            foreach (char letter in piece.Text) {
                if (!char.IsWhiteSpace(letter) && Array.IndexOf(IgnoredDiffLetters, letter) == -1) {
                    signifficant = true;
                    break;
                }
            }

            return signifficant;
        }

        private DiffKind EstimateModificationType(DiffPiece before, DiffPiece after) {
            //? TODO: This should query an IR-level interface
            bool isTemporary(string text, out int number) {
                int prefixLength = 0;
                var name = text.AsSpan();
                number = 0;

                if (name.StartsWith("tv".AsSpan()) || name.StartsWith("hv".AsSpan())) {
                    prefixLength = 2;
                }
                else if (name.StartsWith("t".AsSpan())) {
                    prefixLength = 1;
                }
                else {
                    return false;
                }

                var remainingName = name.Slice(prefixLength);

                foreach (char letter in remainingName) {
                    if (!char.IsDigit(letter)) {
                        return false;
                    }
                }

                return int.TryParse(remainingName, out number);
            }

            if (isTemporary(before.Text, out int beforeNumber) &&
                isTemporary(after.Text, out int afterNumber) &&
                beforeNumber == afterNumber) {
                return DiffKind.MinorModification;
            }

            return DiffKind.Modification;
        }

        private Task<DiffMarkingResult> MarkDiffs(string text, DiffPaneModel diff, DiffPaneModel otherDiff,
                                                  IRDocument textEditor, bool isRightDoc,
                                                  DiffStatistics diffStats) {
            textEditor.ExecuteDocumentAction(new DocumentAction(DocumentActionKind.ClearAllMarkers));
            textEditor.StartDiffSegmentAdding();
            textEditor.TextArea.IsEnabled = false;

            return Task.Run(() => {
                // Create a new text document and associate it with the task worker.
                var document = new TextDocument(new StringTextSource(text));
                document.SetOwnerThread(Thread.CurrentThread);
                var result = new DiffMarkingResult(document);
                var modifiedSegments = new List<DiffTextSegment>(64);
                int lineCount = diff.Lines.Count;
                int lineAdjustment = 0;

                for (int i = 0; i < lineCount; i++) {
                    var line = diff.Lines[i];

                    switch (line.Type) {
                        case ChangeType.Unchanged: {
                            break; // Ignore.
                        }
                        case ChangeType.Inserted: {
                            int actualLine = line.Position.Value + lineAdjustment;
                            int offset;

                            if (actualLine >= document.LineCount) {
                                offset = document.TextLength;
                            }
                            else {
                                offset = document.GetOffset(actualLine, 0);
                            }

                            document.Insert(offset, line.Text + Environment.NewLine);

                            result.DiffSegments.Add(
                                new DiffTextSegment(DiffKind.Insertion, offset, line.Text.Length));

                            diffStats.LinesAdded++;
                            break;
                        }
                        case ChangeType.Deleted: {
                            int actualLine = line.Position.Value + lineAdjustment;
                            var docLine = document.GetLineByNumber(Math.Min(document.LineCount, actualLine));

                            result.DiffSegments.Add(
                                new DiffTextSegment(DiffKind.Deletion, docLine.Offset,
                                                    docLine.Length));

                            diffStats.LinesDeleted++;
                            break;
                        }
                        case ChangeType.Imaginary: {
                            int actualLine = i + 1;

                            if (isRightDoc) {
                                if (actualLine <= document.LineCount) {
                                    var docLine = document.GetLineByNumber(actualLine);
                                    int offset = docLine.Offset;
                                    int length = docLine.Length;
                                    document.Replace(offset, length, new string(RemovedDiffLineChar, length));

                                    result.DiffSegments.Add(
                                        new DiffTextSegment(DiffKind.Placeholder, offset, length));
                                }
                            }
                            else {
                                int offset = actualLine <= document.LineCount
                                    ? document.GetOffset(actualLine, 0)
                                    : document.TextLength;

                                string imaginaryText =
                                    new string(AddedDiffLineChar, otherDiff.Lines[i].Text.Length);

                                document.Insert(offset, imaginaryText + Environment.NewLine);

                                result.DiffSegments.Add(
                                    new DiffTextSegment(DiffKind.Placeholder, offset,
                                                        imaginaryText.Length));
                            }

                            lineAdjustment++;
                            break;
                        }
                        case ChangeType.Modified: {
                            int actualLine = line.Position.Value + lineAdjustment;
                            int lineChanges = 0;
                            int lineLength = 0;
                            bool wholeLineReplaced = false;

                            if (actualLine < document.LineCount) {
                                var docLine = document.GetLineByNumber(actualLine);
                                document.Replace(docLine.Offset, docLine.Length, line.Text);
                                wholeLineReplaced = true;
                                lineLength = docLine.Length;
                            }

                            modifiedSegments.Clear();
                            int column = 0;

                            foreach (var piece in line.SubPieces) {
                                switch (piece.Type) {
                                    case ChangeType.Inserted: {
                                        Debug.Assert(isRightDoc);

                                        int offset = actualLine >= document.LineCount
                                            ? document.TextLength
                                            : document.GetOffset(actualLine, 0) + column;

                                        if (offset >= document.TextLength) {
                                            if (!wholeLineReplaced) {
                                                document.Insert(document.TextLength, piece.Text);
                                            }

                                            if (IsSignifficantDiff(piece)) {
                                                modifiedSegments.Add(
                                                    new DiffTextSegment(
                                                        DiffKind.Modification, offset, piece.Text.Length));
                                            }
                                        }
                                        else {
                                            var diffKind = DiffKind.Insertion;
                                            var otherPiece = FindPieceInOtherDocument(otherDiff, i, piece);

                                            if (otherPiece != null && otherPiece.Type == ChangeType.Deleted) {
                                                if (!wholeLineReplaced) {
                                                    document.Replace(
                                                        offset, otherPiece.Text.Length, piece.Text);
                                                }

                                                diffKind = EstimateModificationType(otherPiece, piece);
                                            }
                                            else {
                                                if (!wholeLineReplaced) {
                                                    document.Insert(offset, piece.Text);
                                                }
                                            }

                                            if (IsSignifficantDiff(piece)) {
                                                modifiedSegments.Add(
                                                    new DiffTextSegment(diffKind, offset, piece.Text.Length));
                                            }
                                        }

                                        break;
                                    }
                                    case ChangeType.Deleted: {
                                        Debug.Assert(!isRightDoc);
                                        int offset = document.GetOffset(actualLine, 0) + column;

                                        if (offset >= document.TextLength) {
                                            offset = document.TextLength;
                                        }

                                        var diffKind = DiffKind.Deletion;
                                        var otherPiece = FindPieceInOtherDocument(otherDiff, i, piece);

                                        if (otherPiece != null && otherPiece.Type == ChangeType.Inserted) {
                                            diffKind = EstimateModificationType(otherPiece, piece);
                                        }

                                        if (IsSignifficantDiff(piece)) {
                                            modifiedSegments.Add(
                                                new DiffTextSegment(diffKind, offset, piece.Text.Length));
                                        }

                                        break;
                                    }
                                    case ChangeType.Modified: {
                                        break;
                                    }
                                    case ChangeType.Imaginary: {
                                        if (isRightDoc) {
                                            lineChanges++;
                                            column++;
                                        }

                                        break;
                                    }
                                    case ChangeType.Unchanged: {
                                        int offset = document.GetOffset(actualLine, 0) + column;

                                        if (!wholeLineReplaced) {
                                            document.Replace(offset, piece.Text.Length, piece.Text);
                                        }

                                        break;
                                    }
                                    default: throw new ArgumentOutOfRangeException();
                                }

                                if (piece.Text != null) {
                                    column += piece.Text.Length;

                                    if (piece.Type != ChangeType.Unchanged) {
                                        lineChanges += piece.Text.Length;
                                    }
                                }
                            }

                            // If 75%+ of the line changed, mark the entire line,
                            // otherwise mark each sub-piece.
                            if (lineChanges * 4 >= lineLength * 3) {
                                if (actualLine < document.LineCount) {
                                    var docLine = document.GetLineByNumber(actualLine);

                                    result.DiffSegments.Add(
                                        new DiffTextSegment(
                                            DiffKind.Modification, docLine.Offset, docLine.Length));
                                }
                            }
                            else {
                                foreach (var segment in modifiedSegments) {
                                    result.DiffSegments.Add(segment);
                                }
                            }

                            if (isRightDoc) {
                                diffStats.LinesModified++;
                            }

                            break;
                        }
                        default: throw new ArgumentOutOfRangeException();
                    }
                }

                result.DiffText = document.Text;
                document.SetOwnerThread(null);
                ReparseDiffedFunction(result);
                return result;
            });
        }

        private void UpdateDiffDocument(IRDocument textEditor, DiffMarkingResult diffResult) {
            diffResult.DiffDocument.SetOwnerThread(Thread.CurrentThread);
            textEditor.UninstallBlockFolding();
            textEditor.Document = diffResult.DiffDocument;
            textEditor.TextArea.IsEnabled = true;
        }

        private void MarkDiffsComplete(IRDocument textEditor, DiffMarkingResult diffResult) {
            textEditor.AddDiffTextSegments(diffResult.DiffSegments);
            textEditor.SetupBlockFolding();
        }

        private DiffPiece FindPieceInOtherDocument(DiffPaneModel otherDiff, int i, DiffPiece piece) {
            if (i < otherDiff.Lines.Count) {
                var otherLine = otherDiff.Lines[i];

                if (piece.Position.Value < otherLine.SubPieces.Count) {
                    return otherLine.SubPieces.Find(item => item.Position.HasValue &&
                                                            item.Position.Value == piece.Position.Value);
                }
            }

            return null;
        }

        private async void OnTopButton_Unchecked(object sender, RoutedEventArgs e) {
            if (ignoreDiffModeButtonEvent_) {
                return;
            }

            await ExitDocumentDiffState();
        }

        private void OptionalStatusText_MouseDown(object sender, MouseButtonEventArgs e) {
            ErrorReporting.SaveOpenSections();
        }

        private void MenuItem_Click_2(object sender, RoutedEventArgs e) {
            ErrorReporting.SaveOpenSections();
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e) {
            throw new InvalidOperationException("Crash Handler test assert");
        }

        private void MenuItem_Click_3(object sender, RoutedEventArgs e) {
            CreateDefaultSideBySidePanels();
        }

        private void ShowPanelMenuClicked(object sender, RoutedEventArgs e) {
            switch (((MenuItem) sender).Tag) {
                case "Section": {
                    SectionPanelHost.IsVisible = true;
                    break;
                }
                case "Definition": {
                    DefinitionPanelHost.IsVisible = true;
                    break;
                }
                case "References": {
                    ReferencesPanelHost.IsVisible = true;
                    break;
                }
                case "Bookmarks": {
                    BookmarksPanelHost.IsVisible = true;
                    break;
                }
                case "SourceFile": {
                    SourceFilePanelHost.IsVisible = true;
                    break;
                }
                case "FlowGraph": {
                    FlowGraphPanelHost.IsVisible = true;
                    break;
                }
                case "DominatorTree": {
                    DominatorTreePanelHost.IsVisible = true;
                    break;
                }
                case "PostDominatorTree": {
                    PostDominatorTreePanelHost.IsVisible = true;
                    break;
                }
                case "ExpressionGraph": {
                    ExpressionGraphPanelHost.IsVisible = true;
                    break;
                }
            }
        }

        private async Task<bool> SaveSessionDocument(string filePath) {
            try {
                NotifyPanelsOfSessionSave();
                NotifyDocumentsOfSessionSave();
                var data = await sessionState_.SerializeSession(mainDocument_.Loader);

                if (data != null) {
                    await File.WriteAllBytesAsync(filePath, data);
                    return true;
                }
            }
            catch (IOException ioEx) {
                Trace.TraceError($"Failed to save session, IO exception: {ioEx}");
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save session, exception: {ex}");
            }

            return false;
        }

        private async Task<bool> OpenSessionDocument(string filePath) {
            try {
                EndSession();
                UpdateUIBeforeReadSession(filePath);
                var data = await File.ReadAllBytesAsync(filePath);
                var state = await SessionStateManager.DeserializeSession(data);
                bool loaded = false;

                if (state != null) {
                    loaded = await LoadSessionDocument(state);
                }

                UpdateUIAfterLoadDocument();
                return loaded;
            }
            catch (IOException ioEx) {
                Trace.TraceError($"Failed to save session, IO exception: {ioEx}");
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save session, exception: {ex}");
            }

            UpdateUIAfterLoadDocument();
            return false;
        }

        private async void InitializeFromLoadedSession(SessionState state) {
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

                // Reload state of panels.
                foreach (var panelState in docState.PanelStates) {
                    var section = summary.GetSectionWithId(panelState.Item1);
                    var panelInfo = FindActivePanel(panelState.Item2.PanelKind);
                    sessionState_.SavePanelState(panelState.Item2.StateObject, panelInfo.Panel, section);
                }
            }

            foreach (var panelState in state.GlobalPanelStates) {
                var panelInfo = FindActivePanel(panelState.PanelKind);
                sessionState_.SavePanelState(panelState.StateObject, panelInfo.Panel, null);
            }

            SetupSectionPanel();
            NotifyPanelsOfSessionStart();

            //? TODO: Reload sections left open.
            //foreach (var sectionId in state.OpenSections) {
            //    var section = DocumentSummary.GetSectionWithId(sectionId);
            //    var args = new OpenSectionEventArgs(section, OpenSectionKind.NewTabDockRight);
            //    await SwitchDocumentSection(args);
            //    SectionPanel.SelectSection(section);
            //}
            StartAutoSaveTimer();
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e) {
            var updateWindow = new UpdateWindow(UpdateButton.Tag as UpdateInfoEventArgs);
            updateWindow.Owner = this;
            var installUpdate = updateWindow.ShowDialog();

            if (installUpdate.HasValue && installUpdate.Value) {
                Close();
            }
        }

        private void MenuItem_Exit(object sender, RoutedEventArgs e) {
            Close();
        }

        private void RecentFilesMenu_Click(object sender, RoutedEventArgs e) {
            App.Settings.ClearRecentFiles();
            PopulateRecentFilesMenu();
        }

        private void MenuItem_Click_6(object sender, RoutedEventArgs e) {
            var optionsWindow = new OptionsWindow();
            optionsWindow.Owner = this;
            optionsWindow.ShowDialog();
        }

        private void MenuItem_Click_4(object sender, RoutedEventArgs e) {
            try {
                var psi = new ProcessStartInfo(DOCUMENTATION_LOCATION);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to open documentation page: {ex}");
            }
        }

        private void MenuItem_Click_5(object sender, RoutedEventArgs e) {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            MessageBox.Show($"IR Explorer\n© 2019 Microsoft Corporation\n\nVersion: {version}", "About",
                            MessageBoxButton.OK);
        }

        private void MenuItem_Click_7(object sender, RoutedEventArgs e) {
            var x = new QueryPanelPreview();
            x.Show();
        }

        private void SetOptionalStatus(string text, string tooltip = "") {
            OptionalStatusText.Text = text;
            OptionalStatusText.Foreground = Brushes.DarkGreen;
            OptionalStatusText.ToolTip = tooltip;
        }

        private void MenuItem_Click_8(object sender, RoutedEventArgs e) {
            StartGrpcServer();
        }

        private void StartGrpcServer() {
            try {
                Trace.TraceInformation("Starting Grpc server...");
                debugService_ = new DebugServer.DebugService();
                debugService_.OnStartSession += DebugService_OnSessionStarted;
                debugService_.OnUpdateIR += DebugService_OnIRUpdated;
                debugService_.OnMarkElement += DebugService_OnElementMarked;
                debugService_.OnSetCurrentElement += DebugService_OnSetCurrentElement;
                debugService_.OnExecuteCommand += DebugService_OnExecuteCommand;
                debugService_.OnHasActiveBreakpoint += DebugService_OnHasActiveBreakpoint;
                debugService_.OnClearTemporaryHighlighting += DebugService__OnClearTemporaryHighlighting;
                debugService_.OnUpdateCurrentStackFrame += DebugService__OnUpdateCurrentStackFrame;
                DebugServer.DebugService.StartServer(debugService_);
                Trace.TraceInformation("Grpc server started, waiting for client...");
                SetOptionalStatus("Waiting for client...");
            }
            catch (Exception ex) {
                Trace.TraceError("Failed to start Grpc server: {0}", $"{ex.Message}\n{ex.StackTrace}");
                SetOptionalStatus("Failed to start Grpc server.", $"{ex.Message}\n{ex.StackTrace}");
            }
        }

        private void DebugService__OnUpdateCurrentStackFrame(object sender, CurrentStackFrameRequest e) {
            Dispatcher.BeginInvoke(() => { debugCurrentStackFrame_ = e.CurrentFrame; });
        }

        private void DebugService__OnClearTemporaryHighlighting(object sender, ClearHighlightingRequest e) {
            Dispatcher.BeginInvoke(() => {
                var activeDoc = FindActiveDocument();

                if (activeDoc == null) {
                    return;
                }

                var action = new DocumentAction(DocumentActionKind.ClearTemporaryMarkers);
                activeDoc.TextView.ExecuteDocumentAction(action);
            });
        }

        private void DebugService_OnHasActiveBreakpoint(object sender,
                                                        RequestResponsePair<ActiveBreakpointRequest, bool>
                                                            e) {
            if (addressTag_ == null) {
                return;
            }

            if (addressTag_.AddressToElementMap.TryGetValue(e.Request.ElementAddress, out var element)) {
                Dispatcher.Invoke(() => {
                    var activeDoc = FindActiveDocument();

                    if (activeDoc == null) {
                        return;
                    }

                    e.Response = activeDoc.TextView.BookmarkManager.FindBookmark(element) != null;
                });
            }
        }

        private DocumentActionKind CommandToAction(ElementCommand command) {
            return command switch {
                ElementCommand.GoToDefinition => DocumentActionKind.GoToDefinition,
                ElementCommand.MarkBlock      => DocumentActionKind.MarkBlock,
                ElementCommand.MarkExpression => DocumentActionKind.MarkExpression,
                ElementCommand.MarkReferences => DocumentActionKind.MarkReferences,
                ElementCommand.MarkUses       => DocumentActionKind.MarkUses,
                ElementCommand.ShowExpression => DocumentActionKind.ShowExpressionGraph,
                ElementCommand.ShowReferences => DocumentActionKind.ShowReferences,
                ElementCommand.ShowUses       => DocumentActionKind.ShowUses,
                ElementCommand.ClearMarker    => DocumentActionKind.ClearMarker,
                _                             => throw new NotImplementedException()
            };
        }

        private void DebugService_OnExecuteCommand(object sender, ElementCommandRequest e) {
            if (addressTag_ == null) {
                return;
            }

            if (addressTag_.AddressToElementMap.TryGetValue(e.ElementAddress, out var element)) {
                Dispatcher.BeginInvoke(() => {
                    var activeDoc = FindActiveDocument();

                    if (activeDoc == null) {
                        return;
                    }

                    AppendNotesTag(element, e.Label);
                    var style = new PairHighlightingStyle();
                    style.ParentStyle = new HighlightingStyle(Colors.Transparent, Pens.GetPen(Colors.Purple));

                    style.ChildStyle = new HighlightingStyle(Colors.LightPink,
                                                             Pens.GetPen(Colors.MediumVioletRed));

                    var actionKind = CommandToAction(e.Command);

                    if (actionKind == DocumentActionKind.MarkExpression) {
                        var data = new MarkActionData {
                            IsTemporary = e.Highlighting == HighlightingType.Temporary
                        };

                        var action = new DocumentAction(actionKind, element, data);
                        activeDoc.TextView.ExecuteDocumentAction(action);
                    }
                    else {
                        var action = new DocumentAction(actionKind, element, style);
                        activeDoc.TextView.ExecuteDocumentAction(action);
                    }
                });
            }
        }

        private void DebugService_OnSetCurrentElement(object sender, SetCurrentElementRequest e) {
            if (addressTag_ == null) {
                return;
            }

            Dispatcher.BeginInvoke(() => {
                var activeDoc = FindActiveDocument();

                if (activeDoc == null) {
                    return;
                }

                var elementIteratorId = new ElementIteratorId(e.ElementId, e.ElementKind);

                if (debugCurrentIteratorElement_.TryGetValue(elementIteratorId, out var currentElement)) {
                    currentElement.RemoveTag<NotesTag>();
                    activeDoc.TextView.ClearMarkedElement(currentElement);
                    debugCurrentIteratorElement_.Remove(elementIteratorId);
                }

                if (e.ElementAddress != 0 &&
                    addressTag_.AddressToElementMap.TryGetValue(e.ElementAddress, out var element)) {
                    var notesTag = AppendNotesTag(element, e.Label);

                    if (e.ElementKind == IRElementKind.Block) {
                        activeDoc.TextView.MarkBlock(element, GetIteratorElementStyle(elementIteratorId));
                    }
                    else {
                        activeDoc.TextView.MarkElement(element, GetIteratorElementStyle(elementIteratorId));
                    }

                    activeDoc.TextView.BringElementIntoView(element);
                    debugCurrentIteratorElement_[elementIteratorId] = element;
                }
            });
        }

        private NotesTag AppendNotesTag(IRElement element, string label) {
            if (!string.IsNullOrEmpty(label)) {
                element.RemoveTag<NotesTag>();
                var notesTag = element.GetOrAddTag<NotesTag>();
                notesTag.Title = label;

                if (debugCurrentStackFrame_ != null) {
                    notesTag.Notes.Add(
                        $"{debugCurrentStackFrame_.Function}:{debugCurrentStackFrame_.LineNumber}");
                }

                return notesTag;
            }

            return null;
        }

        private HighlightingStyle GetIteratorElementStyle(ElementIteratorId elementIteratorId) {
            return elementIteratorId.ElementKind switch {
                IRElementKind.Instruction => new HighlightingStyle(
                    Colors.LightBlue, Pens.GetPen(Colors.MediumBlue)),
                IRElementKind.Operand => new HighlightingStyle(Colors.PeachPuff,
                                                               Pens.GetPen(Colors.SaddleBrown)),
                IRElementKind.User => new HighlightingStyle(Utils.ColorFromString("#EFBEE6"),
                                                            Pens.GetPen(Colors.Purple)),
                IRElementKind.UserParent =>
                new HighlightingStyle(Colors.Lavender, Pens.GetPen(Colors.Purple)),
                IRElementKind.Block => new HighlightingStyle(Colors.LightBlue, Pens.GetPen(Colors.LightBlue)),
                _                   => new HighlightingStyle(Colors.Gray)
            };
        }

        private void DebugService_OnElementMarked(object sender, MarkElementRequest e) {
            if (addressTag_ == null) {
                return;
            }

            if (addressTag_.AddressToElementMap.TryGetValue(e.ElementAddress, out var element)) {
                Dispatcher.BeginInvoke(() => {
                    var activeDoc = FindActiveDocument();

                    if (activeDoc == null) {
                        return;
                    }

                    var notesTag = AppendNotesTag(element, e.Label);

                    if (e.Highlighting == HighlightingType.Temporary) {
                        activeDoc.TextView.HighlightElement(element, HighlighingType.Selected);
                    }
                    else {
                        if (debugCurrentStackFrame_ != null) {
                            var style =
                                HighlightingStyles.StyleSet.ForIndex(debugCurrentStackFrame_.LineNumber);

                            activeDoc.TextView.MarkElement(element, style);
                        }
                        else {
                            activeDoc.TextView.MarkElement(element, Colors.Gray);
                        }
                    }

                    activeDoc.TextView.BringElementIntoView(element);
                });
            }
        }

        private string ExtractFunctionName(string text) {
            int startIndex = 0;

            while (startIndex < text.Length) {
                int index = text.IndexOfAny(new[] {'\r', '\n'}, startIndex);

                if (index != -1) {
                    string line = text.Substring(startIndex, index);

                    if (line.StartsWith("ENTRY")) {
                        return UTCReader.ExtractFunctionName(line);
                    }

                    startIndex = index + 1;
                }
                else {
                    break;
                }
            }

            return null;
        }

        private void DebugService_OnIRUpdated(object sender, UpdateIRRequest e) {
            Dispatcher.BeginInvoke(async () => {
                if (mainDocument_ == null) {
                    return;
                }

                string funcName = ExtractFunctionName(e.Text);
                funcName ??= "Debugged function";

                if (debugFunction_ == null || funcName != debugFunction_.Name) {
                    debugFunction_ = new IRTextFunction(funcName);
                    previousDebugSection_ = null;
                    debugSectionId_ = 0;
                    debugCurrentIteratorElement_ = new Dictionary<ElementIteratorId, IRElement>();
                    mainDocument_.Summary.AddFunction(debugFunction_);
                }

                string sectionName = $"Version {debugSectionId_ + 1}";

                if (debugCurrentStackFrame_ != null) {
                    sectionName +=
                        $" ({debugCurrentStackFrame_.Function}:{debugCurrentStackFrame_.LineNumber})";
                }

                var section = new IRTextSection(debugFunction_, (ulong) debugSectionId_, debugSectionId_,
                                                sectionName, new IRPassOutput(0, 0, 0, 0));

                string filteredText = ExtractLineMetadata(section, e.Text);

                try {
                    if (previousDebugSection_ != null &&
                        debugSections_.GetSectionText(previousDebugSection_) == filteredText) {
                        return; // Text unchanged
                    }
                }
                catch (Exception ex) {
                    MessageBox.Show($"Unexpected RPC failure: {ex.Message}\n {ex.StackTrace}");
                }

                debugFunction_.Sections.Add(section);
                debugSections_.AddSection(section, filteredText);
                debugSummary_.AddSection(section);
                SectionPanel.MainSummary = null;
                SectionPanel.MainSummary = debugSummary_;
                SectionPanel.SelectSection(section);
                previousDebugSection_ = section;
                debugSectionId_++;

                await SwitchDocumentSection(
                    new OpenSectionEventArgs(section, OpenSectionKind.ReplaceCurrent));
            });
        }

        private string ExtractLineMetadata(IRTextSection section, string text) {
            var builder = new StringBuilder(text.Length);
            var lines = text.Split(new[] {"\r\n", "\r", "\n"}, StringSplitOptions.None);
            int metadataLines = 0;

            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i];

                if (line.StartsWith("/// irx:")) {
                    section.AddLineMetadata(i - metadataLines - 1, line);
                    metadataLines++;
                }
                else {
                    builder.AppendLine(line);
                }
            }

            return builder.ToString();
        }

        private async void DebugService_OnSessionStarted(object sender, StartSessionRequest e) {
            if (e.ProcessId == debugProcessId_) {
                return;
            }

            await Dispatcher.BeginInvoke(() => {
                SetOptionalStatus($"Client connected, process {e.ProcessId}");
                EndSession();
                var result = new LoadedDocument("Debug session");
                debugSections_ = new DebugSectionLoader();
                debugSummary_ = debugSections_.LoadDocument();
                result.Loader = debugSections_;
                result.Summary = debugSummary_;
                SetupOpenedIRDocument(SessionKind.DebugSession, "Debug session", result);
                debugCurrentIteratorElement_ = new Dictionary<ElementIteratorId, IRElement>();
                debugProcessId_ = e.ProcessId;
                debugSectionId_ = 0;
                debugFunction_ = null;
                previousDebugSection_ = null;
            });
        }

        //? TODO: Could be useful later to speed up WPF
        //protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() {
        //    // Disable UI automation, it triggers Win32Exceptions with AvalonDock.
        //    return new CustomAutomationPeer(this);
        //}

        private class MainWindowState {
            public MainWindowState(MainWindow window) {
                CurrentResizeMode = window.ResizeMode;
                CurrentWindowStyle = window.WindowStyle;
                CurrentWindowState = window.WindowState;
                IsTopmost = window.Topmost;
                CurrentMenuVisibility = window.MainMenu.Visibility;
                CurrentMenuHeight = window.MainGrid.RowDefinitions[0].Height;
            }

            public ResizeMode CurrentResizeMode { get; set; }
            public WindowStyle CurrentWindowStyle { get; set; }
            public WindowState CurrentWindowState { get; set; }
            public bool IsTopmost { get; set; }
            public Visibility CurrentMenuVisibility { get; set; }
            public GridLength CurrentMenuHeight { get; set; }

            public void Restore(MainWindow window) {
                window.ResizeMode = CurrentResizeMode;
                window.WindowStyle = CurrentWindowStyle;
                window.WindowState = CurrentWindowState;
                window.Topmost = IsTopmost;
                window.MainMenu.Visibility = CurrentMenuVisibility;
                window.MainGrid.RowDefinitions[0].Height = CurrentMenuHeight;
            }
        }

        private class DiffStatistics {
            public int LinesAdded;
            public int LinesDeleted;
            public int LinesModified;

            public override string ToString() {
                if (LinesAdded == 0 && LinesDeleted == 0 && LinesModified == 0) {
                    return "0 diffs";
                }

                return $"A {LinesAdded}, D {LinesDeleted}, M {LinesModified}";
            }
        }

        private class DiffMarkingResult {
            public TextDocument DiffDocument;
            internal FunctionIR DiffFunction;
            public List<DiffTextSegment> DiffSegments;
            public string DiffText;
            public bool FunctionReparsingRequired;

            public DiffMarkingResult(TextDocument diffDocument) {
                DiffDocument = diffDocument;
                DiffSegments = new List<DiffTextSegment>(4096);
            }
        }

        private struct ElementIteratorId {
            public int ElementId;
            public IRElementKind ElementKind;

            public ElementIteratorId(int elementId, IRElementKind elementKind) {
                ElementId = elementId;
                ElementKind = elementKind;
            }

            public override bool Equals(object obj) {
                return obj is ElementIteratorId id &&
                       ElementId == id.ElementId &&
                       ElementKind == id.ElementKind;
            }

            public override int GetHashCode() {
                return HashCode.Combine(ElementId, ElementKind);
            }
        }
    }
}
