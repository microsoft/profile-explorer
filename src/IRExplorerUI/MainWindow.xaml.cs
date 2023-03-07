// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using AvalonDock.Controls;
using AvalonDock.Layout;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit.Document;
using IRExplorerUI.DebugServer;
using IRExplorerUI.OptionsPanels;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.UTC;
using IRExplorerUI.Diff;
using Microsoft.Win32;
using IRExplorerUI.Panels;
using IRExplorerUI.Scripting;
using IRExplorerUI.Compilers.ASM;
using IRExplorerUI.Compilers.UTC;
using IRExplorerUI.Compilers.LLVM;
using IRExplorerUI.Controls;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Windows.Automation.Peers;
using IRExplorerUI.Scripting;
using IRExplorerUI.Utilities;
using AvalonDock.Layout.Serialization;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using IRExplorerCore.Utilities;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Wpf;
using OxyPlot.SkiaSharp.Wpf;
using LinearAxis = OxyPlot.Axes.LinearAxis;
using System.Drawing.Drawing2D;
using static Google.Protobuf.WellKnownTypes.Field.Types;
using System.Windows.Documents;

namespace IRExplorerUI {
    public static class AppCommand {
        public static readonly RoutedUICommand FullScreen =
            new RoutedUICommand("Untitled", "FullScreen", typeof(Window));
        public static readonly RoutedUICommand OpenDocument =
            new RoutedUICommand("Untitled", "OpenDocument", typeof(Window));
        public static readonly RoutedUICommand OpenNewDocument =
            new RoutedUICommand("Untitled", "OpenNewDocument", typeof(Window));
        public static readonly RoutedUICommand OpenDebug =
            new RoutedUICommand("Untitled", "OpenDebug", typeof(Window));
        public static readonly RoutedUICommand OpenDiffDebug =
            new RoutedUICommand("Untitled", "OpenDiffDebug", typeof(Window));
        public static readonly RoutedUICommand OpenExecutable =
            new RoutedUICommand("Untitled", "OpenExecutable", typeof(Window));
        public static readonly RoutedUICommand OpenExecutableDiff =
            new RoutedUICommand("Untitled", "OpenExecutableDiff", typeof(Window));
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
        public static readonly RoutedUICommand SwapDiffDocuments =
            new RoutedUICommand("Untitled", "SwapDiffDocuments", typeof(Window));
        public static readonly RoutedUICommand ShowDocumentSearch =
            new RoutedUICommand("Untitled", "ShowDocumentSearch", typeof(Window));
        public static readonly RoutedUICommand LoadProfile =
            new RoutedUICommand("Untitled", "LoadProfile", typeof(Window));
        public static readonly RoutedUICommand RecordProfile =
            new RoutedUICommand("Untitled", "RecordProfile", typeof(Window));
        public static readonly RoutedUICommand ViewProfileReport =
            new RoutedUICommand("Untitled", "ViewProfileReport", typeof(Window));
        public static readonly RoutedUICommand ShowProfileCallGraph =
            new RoutedUICommand("Untitled", "ShowProfileCallGraph", typeof(Window));
    }

    public class DummyWindowAutomationPeer : FrameworkElementAutomationPeer {
        public DummyWindowAutomationPeer(FrameworkElement owner) : base(owner) { }

        protected override string GetNameCore() {
            return "CustomWindowAutomationPeer";
        }

        protected override AutomationControlType GetAutomationControlTypeCore() {
            return AutomationControlType.Window;
        }

        protected override List<AutomationPeer> GetChildrenCore() {
            return new List<AutomationPeer>();
        }
    }

    public partial class MainWindow : Window, ISession {
        private LayoutDocumentPane activeDocumentPanel_;
        private AssemblyMetadataTag addressTag_;
        private bool appIsActivated_;
        private DispatcherTimer autoSaveTimer_;
        private Dictionary<string, DateTime> changedDocuments_;
        private ICompilerInfoProvider compilerInfo_;
        private MainWindowState fullScreenRestoreState_;

        private Dictionary<GraphKind, GraphLayoutCache> graphLayout_;
        private bool ignoreDiffModeButtonEvent_;
        private Dictionary<ToolPanelKind, List<PanelHostInfo>> panelHostSet_;
        private IRTextSection previousDebugSection_;
        private SessionStateManager sessionState_;
        private bool sideBySidePanelsCreated_;
        private DispatcherTimer updateTimer_;
        private List<DraggablePopup> detachedPanels_;
        private Point previousWindowPosition_;
        private DateTime lastDocumentLoadTime_;
        private DateTime lastDocumentReloadQueryTime_;
        private DelayedAction statusTextAction_;
        private object lockObject_;

        public MainWindow() {
            App.WindowShowTime = DateTime.UtcNow;
            InitializeComponent();

            App.Session = this;
            panelHostSet_ = new Dictionary<ToolPanelKind, List<PanelHostInfo>>();
            changedDocuments_ = new Dictionary<string, DateTime>();
            detachedPanels_ = new List<DraggablePopup>();
            lockObject_ = new object();

            SetupMainWindow();
            SetupGraphLayoutCache();

            ContentRendered += MainWindow_ContentRendered;
            StateChanged += MainWindow_StateChanged;
            LocationChanged += MainWindow_LocationChanged;
            Closing += MainWindow_Closing;
            Activated += MainWindow_Activated;
            Deactivated += MainWindow_Deactivated;
            SizeChanged += MainWindow_SizeChanged;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) {
            UpdateStartPagePanelPosition();
        }

        protected override AutomationPeer OnCreateAutomationPeer() {
            return new DummyWindowAutomationPeer(this);
        }

        private void MainWindow_StateChanged(object sender, EventArgs e) {
            if (WindowState == WindowState.Minimized) {
                detachedPanels_.ForEach(panel => panel.Minimize());
            }
            else if (WindowState == WindowState.Normal) {
                detachedPanels_.ForEach(panel => panel.Restore());
            }
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e) {
            var currentWindowPosition = new Point(Left, Top);

            if (detachedPanels_.Count > 0) {
                var diff = currentWindowPosition - previousWindowPosition_;

                detachedPanels_.ForEach(panel => {
                    panel.HorizontalOffset += diff.X;
                    panel.VerticalOffset += diff.Y;
                });
            }

            previousWindowPosition_ = currentWindowPosition;
        }

        private void CloseDetachedPanels() {
            // Close all remark preview panels.
            detachedPanels_.ForEach(panel => panel.Close());
            detachedPanels_.Clear();
        }

        public SessionStateManager SessionState => sessionState_;

        public IRTextSummary GetDocumentSummary(IRTextSection section) {
            return sessionState_.FindLoadedDocument(section).Summary;
        }

        public void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations) {
            SectionPanel.SetSectionAnnotationState(section, hasAnnotations);
        }

        public async Task<IRDocumentHost> SwitchDocumentSectionAsync(OpenSectionEventArgs args) {
            var documentHost = args.TargetDocument;

            if (documentHost != null) {
                // Use the existing editor to show the section.
                documentHost = await OpenDocumentSectionAsync(args, documentHost);
            }
            else {
                // Open a new editor to show the section.
                documentHost = await OpenDocumentSectionAsync(args);
            }

            await SectionPanel.SelectSection(args.Section, false);
            return documentHost;
        }

        public async Task SwitchGraphsAsync(GraphPanel graphPanel, IRTextSection section,
                                            IRDocument document) {
            var action = GetComputeGraphAction(graphPanel.PanelKind);
            await SwitchGraphsAsync(graphPanel, section, document, action);
        }

        public void ShowAllReferences(IRElement element, IRDocument document) {
            ShowAllReferencesImpl(element, document, false);
        }

        public void ShowSSAUses(IRElement element, IRDocument document) {
            ShowAllReferencesImpl(element, document, true);
        }

        private void ShowAllReferencesImpl(IRElement element, IRDocument document, bool showSSAUses) {
            var panelInfo = FindTargetPanel(document, ToolPanelKind.References);
            var refPanel = panelInfo.Panel as ReferencesPanel;
            panelInfo.Host.IsSelected = true;
            refPanel.FindAllReferences(element, showSSAUses);
        }

        private void MainWindow_Deactivated(object sender, EventArgs e) {
            appIsActivated_ = false;

            detachedPanels_.ForEach(panel => {
                panel.SendToBack();
            });
        }

        private async void MainWindow_Activated(object sender, EventArgs e) {
            appIsActivated_ = true;

            detachedPanels_.ForEach(panel => {
                panel.BringToFront();
            });

            await HandleChangedDocuments();
        }

        private async Task HandleChangedDocuments() {
            var reloadedDocuments = new List<string>();

            lock (lockObject_) {
                foreach (var pair in changedDocuments_) {
                    if (pair.Value < lastDocumentLoadTime_ ||
                        pair.Value < lastDocumentReloadQueryTime_) {
                        continue; // Event happened before the last document reload, ignore.
                    }

                    reloadedDocuments.Add(pair.Key);
                }

                changedDocuments_.Clear();
            }

            foreach (var documentPath in reloadedDocuments) {
                if (ShowDocumentReloadQuery(documentPath)) {
                    await ReloadDocument(documentPath);
                }
                else {
                    // Don't keep showing the dialog if no reload is wanted.
                    changedDocuments_.Clear();
                }
            }
        }

        private void SetupMainWindow() {
            PopulateRecentFilesMenu();
            ThemeCombobox.SelectedIndex = App.Settings.ThemeIndex;
            DiffModeButton.IsEnabled = false;
        }

        private void SetupMainWindowCompilerTarget() {
            IRTypeLabel.Content = compilerInfo_.CompilerDisplayName;
        }

        protected override void OnSourceInitialized(EventArgs e) {
            base.OnSourceInitialized(e);
            WindowPlacement.SetPlacement(this, App.Settings.MainWindowPlacement);
        }

        private void AddRecentFile(string path) {
            App.Settings.AddRecentFile(path);
            App.SaveApplicationSettings();
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
                await OpenDocument((string)menuItem.Tag);
            }
        }

        private async void MainWindow_Closing(object sender, CancelEventArgs e) {
            // Save settings, including the window state.
            App.Settings.MainWindowPlacement = WindowPlacement.GetPlacement(this);
            App.Settings.ThemeIndex = ThemeCombobox.SelectedIndex;
            App.SaveApplicationSettings();
            SaveDockLayout();
            Trace.Flush();

            if (sessionState_ == null) {
                return;
            }

            if (sessionState_.Info.IsFileSession) {
                using var centerForm = new DialogCenteringHelper(this);
                var result = MessageBox.Show("Save session changes before closing?", "IR Explorer",
                                             MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
                if (result == MessageBoxResult.Yes) {
                    SaveDocumentExecuted(this, null);
                }
                else if (result == MessageBoxResult.Cancel) {
                    e.Cancel = true;
                    return;
                }
            }
            else {
                NotifyPanelsOfSessionSave();
                NotifyDocumentsOfSessionSave();

                if (SectionPanel.HasAnnotatedSections) {
                    using var centerForm = new DialogCenteringHelper(this);
                    var result = MessageBox.Show("Save file changes as a new session before closing?", "IR Explorer",
                                                  MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
                    if (result == MessageBoxResult.Yes) {
                        SaveDocumentExecuted(this, null);
                    }
                    else if (result == MessageBoxResult.Cancel) {
                        e.Cancel = true;
                        return;
                    }
                }
            }

            await EndSession();
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e) {
            SetupStartPagePanel();

            if (sessionState_ == null) {
                ShowStartPage();
            }

            var time = DateTime.UtcNow - App.AppStartTime;
            DevMenuStartupTime.Header = $"Startup time: {time.TotalMilliseconds} ms";

            DelayedAction.StartNew(TimeSpan.FromSeconds(10), () => {
                Dispatcher.BeginInvoke(new Action(() => {
                    CheckForUpdate();
                }));
            });

        }

        private static void CheckForUpdate() {
            try {
                AutoUpdater.Start(App.AutoUpdateInfo);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed update check: {ex}");
            }
        }

        private void StartApplicationUpdateTimer() {
            AutoUpdater.RunUpdateAsAdmin = true;
            AutoUpdater.ShowRemindLaterButton = false;

            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            updateTimer_ = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
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

        private async Task ShowSectionPanelDiffs(LoadedDocument result) {
            await SectionPanel.AnalyzeDocumentDiffs();
            await SectionPanel.RefreshDocumentsDiffs();
        }

        private void ShowProgressBar(string title) {
            documentLoadProgressVisible_ = true;
            DocumentLoadProgressBar.Value = 0;
            DocumentLoadProgressBar.Visibility = Visibility.Visible;
            DocumentLoadProgressPanel.Visibility = Visibility.Visible;

            if (string.IsNullOrEmpty(title)) {
                title = "Loading";
            }

            DocumentLoadLabel.Text = title;
        }

        private void HideProgressBar() {
            DocumentLoadProgressPanel.Visibility = Visibility.Collapsed;
            DocumentLoadProgressBar.Visibility = Visibility.Hidden;
            DocumentLoadProgressBar.IsIndeterminate = false;
            DocumentLoadProgressBar.Value = 0;
            documentLoadProgressVisible_ = false;
        }

        private void UpdateWindowTitle() {
            string title = "IR Explorer";

            if (sessionState_.Info.IsFileSession) {
                title += $" - {sessionState_.Info.FilePath}";
                
                if (sessionState_.MainDocument != null &&
                    sessionState_.MainDocument.BinaryFileExists) {
                    if (ProfileData != null && !string.IsNullOrEmpty(ProfileData.Report.TraceInfo.TraceFilePath)) {
                        title += $" ({Utils.TryGetFileName(ProfileData.Report.TraceInfo.TraceFilePath)}, {sessionState_.MainDocument.BinaryFile})";
                    }
                    else {
                        title += $" ({sessionState_.MainDocument.BinaryFile})";
                    }
                }
            }
            else {
                if (sessionState_.Documents.Count == 1) {
                    var name = sessionState_.Documents[0].BinaryFile?.FilePath ?? sessionState_.Documents[0].FilePath;
                    title += $" - {name}";
                }
                else if (sessionState_.Documents.Count == 2) {
                    var baseName = sessionState_.Documents[0].BinaryFile?.FilePath ?? sessionState_.Documents[0].FilePath;
                    var diffName = sessionState_.Documents[1].BinaryFile?.FilePath ?? sessionState_.Documents[1].FilePath;
                    title += $" - Base: {baseName}  | Diff: {diffName}";
                }
            }

            Title = title;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e) {
            await SetupCompilerTarget();

            SectionPanel.OpenSection += SectionPanel_OpenSection;
            SectionPanel.EnterDiffMode += SectionPanel_EnterDiffMode;
            SectionPanel.SyncDiffedDocumentsChanged += SectionPanel_SyncDiffedDocumentsChanged;
            SectionPanel.DisplayCallGraph += SectionPanel_DisplayCallGraph;
            SearchResultsPanel.OpenSection += SectionPanel_OpenSection;

            if (!RestoreDockLayout()) {
                RegisterDefaultToolPanels();
            }

            ResetStatusBar();

            //? TODO: This needs a proper arg parsing lib
            var args = Environment.GetCommandLineArgs();

            if (args.Length >= 3) {
                string baseFilePath = args[1];
                string diffFilePath = args[2];
                bool opened = false;

                if (File.Exists(baseFilePath) && File.Exists(diffFilePath)) {
                    baseFilePath = Path.GetFullPath(baseFilePath);
                    diffFilePath = Path.GetFullPath(diffFilePath);
                    var (baseLoadedDoc, diffLoadedDoc) = await OpenBaseDiffIRDocumentsImpl(baseFilePath, diffFilePath);
                    opened = baseLoadedDoc != null && diffLoadedDoc != null;
                }

                if (!opened) {
                    MessageBox.Show($"Failed to open base/diff files {baseFilePath} and {diffFilePath}");
                    return;
                }

                if (args.Length >= 5 && IsInTwoDocumentsDiffMode) {
                    if (args[3].EndsWith("script")) {
                        SilentMode = true;
                        string scriptPath = args[4];
                        var script = Script.LoadFromFile(scriptPath);

                        if (script == null) {
                            MessageBox.Show($"Failed {scriptPath}");
                            MessageBox.Show(string.Join(Environment.NewLine, args));
                        }

                        string scriptOutPath = null;

                        if (args.Length >= 7) {
                            if (args[5].EndsWith("out")) {
                                scriptOutPath = args[6];
                            }
                        }

                        var session = new ScriptSession(null, this) {
                            SilentMode = true,
                            SessionName = scriptOutPath
                        };

                        script.Execute(session);
                        this.Close();
                    }
                    else if (args[3].EndsWith("func")) {
                        // Open a certain function and section.
                        var funcName = args[4];
                        var func = sessionState_.MainDocument.Summary.FindFunction(funcName);

                        if (func != null) {
                            await SectionPanel.SelectFunction(func);

                            if (args.Length >= 7) {
                                if (args[5].EndsWith("section")) {
                                    var sectionName = args[6];
                                    var section = func.FindSection(sectionName);

                                    if (section != null) {
                                        await SectionPanel.SelectSection(section);
                                        SectionPanel.DiffSelectedSection();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if (args.Length == 2) {
                string filePath = args[1];

                if (File.Exists(filePath)) {
                    filePath = Path.GetFullPath(filePath);
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
            else {
                // Hide the start page if a file was loaded on start.
                HideStartPage();
            }

            StartApplicationUpdateTimer();
        }

        private async void SectionPanel_DisplayCallGraph(object sender, DisplayCallGraphEventArgs e) {
            await DisplayCallGraph(e.Summary, e.Section, e.BuildPartialGraph);
        }

        private void SetupStartPagePanel() {
            StartPage.OpenRecentDocument += StartPage_OpenRecentDocument;
            StartPage.OpenRecentDiffDocuments += StartPage_OpenRecentDiffDocuments;
            StartPage.OpenFile += StartPage_OpenFile;
            StartPage.CompareFiles += StartPage_CompareFiles;
            StartPage.ClearRecentDocuments += StartPage_ClearRecentDocuments;
            StartPage.ClearRecentDiffDocuments += StartPage_ClearRecentDiffDocuments;
            UpdateStartPagePanelPosition();
        }

        private void StartPage_ClearRecentDiffDocuments(object sender, EventArgs e) {
            using var centerForm = new DialogCenteringHelper(this);

            if (MessageBox.Show("Clear the list of recent compared documents?", "IR Explorer",
                                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) {
                App.Settings.ClearRecentComparedFiles();
                App.SaveApplicationSettings();
                StartPage.ReloadFileList();
            }
        }

        private void StartPage_ClearRecentDocuments(object sender, EventArgs e) {
            using var centerForm = new DialogCenteringHelper(this);

            if (MessageBox.Show("Clear the list of recent documents?", "IR Explorer",
                                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) {
                App.Settings.ClearRecentFiles();
                App.SaveApplicationSettings();
                StartPage.ReloadFileList();
            }
        }

        private async void StartPage_CompareFiles(object sender, EventArgs e) {
            await OpenBaseDiffDocuments();
        }

        private async void StartPage_OpenFile(object sender, EventArgs e) {
            await OpenDocument();
        }

        private async void StartPage_OpenRecentDiffDocuments(object sender, Tuple<string, string> e) {
            await OpenBaseDiffDocuments(e.Item1, e.Item2);
        }

        private async void StartPage_OpenRecentDocument(object sender, string e) {
            await OpenDocument(e);
        }

        private void UpdateStartPagePanelPosition() {
            if (sessionState_ != null) {
                return;
            }

            var documentHost = Utils.FindChildLogical<LayoutDocumentPaneGroupControl>(this);

            if (documentHost != null) {
                var height = Math.Max(StartPage.MinHeight, documentHost.ActualHeight * 0.9);
                var left = (documentHost.ActualWidth / 2) - (StartPage.ActualWidth / 2);
                var top = (documentHost.ActualHeight / 2) - (height / 2);
                StartPage.Height = height;
                StartPage.Margin = new Thickness(left, top, 0, 0);
            }
        }

        private void ShowStartPage() {
            StartPage.ReloadFileList();
            StartPage.Visibility = Visibility.Visible;
        }

        private void HideStartPage() {
            StartPage.Visibility = Visibility.Collapsed;
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
            OptionalStatusText.Text = "";
            OptionalStatusText.ToolTip = "";
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

        private void ShowDocumentSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
            ShowDocumentSearchPanel();
        }

        private void CanExecuteAlways(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = true;
            e.Handled = true;
        }

        private string GetDocumentTitle(IRDocumentHost document, IRTextSection section) {
            var title = compilerInfo_.NameProvider.GetSectionName(section, true);

            if (sessionState_.SectionDiffState.IsEnabled) {
                if(sessionState_.SectionDiffState.LeftDocument == document) {
                    return $"Base: {title}";
                }
                else if(sessionState_.SectionDiffState.RightDocument == document) {
                    return $"Diff: {title}";
                }
            }

            return title;
        }

        private string GetDocumentDescription(IRDocumentHost document, IRTextSection section) {
            var docInfo = sessionState_.FindLoadedDocument(section);
            return $"{section.ParentFunction.Name.Trim()} ({section.ParentFunction.ParentSummary.ModuleName})";
        }

        private async void MenuItem_Click_1(object sender, RoutedEventArgs e) {
            await EndSession();
        }

        private void CreateDefaultSideBySidePanels() {
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

            //var rightGraphPanel = FindActivePanel<GraphPanel>(ToolPanelKind.FlowGraph);
            //var leftGraphPanel = CreateNewPanel<GraphPanel>(ToolPanelKind.FlowGraph);
            //var panelHost = AddNewPanel(leftGraphPanel);
            //panelHost.Host.AddToLayout(DockManager, AnchorableShowStrategy.Most);
            //LeftPanelGroup.Children.Insert(0, new LayoutAnchorablePane(panelHost.Host));
            //leftGraphPanel.Width = LeftPanelGroup.DockWidth.Value;
            //panelHost.Host.IsVisible = true;
            //leftGraphPanel.BoundDocument = leftDocument.TextView;
            //rightGraphPanel.BoundDocument = rightDocument.TextView;
            //leftGraphPanel.InitializeFromDocument(leftDocument.TextView);
            //rightGraphPanel.InitializeFromDocument(rightDocument.TextView);
            //await GenerateGraphs(leftDocument.Section, leftDocument.TextView);
            //await GenerateGraphs(rightDocument.Section, rightDocument.TextView);
            //var rightRefPanel = FindActivePanel<ReferencesPanel>(ToolPanelKind.References);
            //var leftRefPanel = CreateNewPanel<ReferencesPanel>(ToolPanelKind.References);
            //DisplayNewPanel(leftRefPanel, rightRefPanel, DuplicatePanelKind.NewSetDockedLeft);
            //leftRefPanel.BoundDocument = leftDocument.TextView;
            //rightRefPanel.BoundDocument = rightDocument.TextView;
            //leftRefPanel.InitializeFromDocument(leftDocument.TextView);
            //rightRefPanel.InitializeFromDocument(rightDocument.TextView);
            //FindPanelHost(rightRefPanel).Host.IsSelected = true;
            //FindPanelHost(leftRefPanel).Host.IsSelected = true;
            //RenameAllPanels();
            //sideBySidePanelsCreated_ = true;
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

        private async Task DisplayCallGraph(IRTextSummary summary, IRTextSection section,
                                            bool buildPartialGraph) {
            var loadedDoc = sessionState_.FindLoadedDocument(summary);
            var layoutGraph = await Task.Run(() =>
                CallGraphUtils.BuildCallGraphLayout(summary, section, loadedDoc,
                                                    CompilerInfo, buildPartialGraph));
            DisplayCallGraph(layoutGraph, section);
        }

        private void DisplayCallGraph(Graph layoutGraph, IRTextSection section) {
            var panel = new CallGraphPanel(this);
            panel.TitleSuffix = $" - S{section.Number} ({CompilerInfo.NameProvider.GetSectionName(section)})";

            DisplayFloatingPanel(panel);
            panel.DisplayGraph(layoutGraph);
        }

        private async void GraphViewer_NodeSelected(object sender, TaggedObject e) {
            var graphNode = e as CallGraphNode;

            if (graphNode != null && graphNode.Function != null) {
                await SectionPanel.SelectFunction(graphNode.Function);
            }
        }

        public Task SwitchActiveFunction(IRTextFunction function) {
            return SectionPanel.SelectFunction(function);
        }

        private void MenuItem_Click_3(object sender, RoutedEventArgs e) {

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
            App.OpenDocumentation();
        }

        private void MenuItem_Click_5(object sender, RoutedEventArgs e) {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            MessageBox.Show($"IR Explorer\n© 2022 Microsoft Corporation\n\nVersion: {version}", "About",
                            MessageBoxButton.OK);
        }

        private void SetOptionalStatus(TimeSpan duration, string text, string tooltip = "", Brush textBrush = null) {
            SetOptionalStatus(text, tooltip, textBrush);
            statusTextAction_ = DelayedAction.StartNew(duration, () => SetOptionalStatus(""));
        }

        private void SetOptionalStatus(string text, string tooltip = "", Brush textBrush = null) {
            statusTextAction_?.Cancel();

            if (!Dispatcher.CheckAccess()) {
                Dispatcher.Invoke(() => SetOptionalStatusImpl(text, tooltip, textBrush));
            }
            else {
                SetOptionalStatusImpl(text, tooltip, textBrush);
            }
        }

        private void SetOptionalStatusImpl(string text, string tooltip = "", Brush textBrush = null) {
            OptionalStatusText.Text = text;
            OptionalStatusText.Foreground = textBrush ?? Brushes.Black;
            OptionalStatusText.ToolTip = tooltip;
            OptionalStatusText.Visibility = !string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
        }
        
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

        private void MenuItem_Click_9(object sender, RoutedEventArgs e) {
            InstallExtension();
        }

        private static void InstallExtension() {
            App.InstallExtension();
        }

        private void AlwaysOnTopMenuClicked(object sender, RoutedEventArgs e) {
            SetAlwaysOnTop(AlwaysOnTopCheckbox.IsChecked);
        }

        private void AlwaysOnTopButton_Click(object sender, RoutedEventArgs e) {
            SetAlwaysOnTop(AlwaysOnTopButton.IsChecked.HasValue && AlwaysOnTopButton.IsChecked.Value);
        }

        private void SetAlwaysOnTop(bool state) {
            Topmost = state;
            AlwaysOnTopCheckbox.IsChecked = state;
            AlwaysOnTopButton.IsChecked = state;
        }

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e) {
            double width = this.ActualWidth;
            double height = this.ActualHeight;
            var bmpCopied = new RenderTargetBitmap((int)Math.Round(width), (int)Math.Round(height), 96, 96, PixelFormats.Default);
            var dv = new DrawingVisual();

            using (DrawingContext dc = dv.RenderOpen()) {
                VisualBrush vb = new VisualBrush(this);
                dc.DrawRectangle(vb, null, new Rect(new Point(), new Size(width, height)));
            }

            bmpCopied.Render(dv);
            Clipboard.SetImage(bmpCopied);
        }

        private async Task SwitchCompilerTarget(ICompilerInfoProvider compilerInfo) {
            await EndSession();
            compilerInfo_ = compilerInfo;
            compilerInfo_.ReloadSettings();
            App.Settings.CompilerIRSwitched(compilerInfo_.CompilerIRName, compilerInfo.IR.Mode);
            SetupMainWindowCompilerTarget();
        }

        private async void LLVMMenuItem_Click(object sender, RoutedEventArgs e) {
            await SwitchCompilerTarget("LLVM");
        }

        private async void UTCMenuItem_Click(object sender, RoutedEventArgs e) {
            await SwitchCompilerTarget("UTC");
        }

        private async void ASMMenuItem_Click(object sender, RoutedEventArgs e) {
            await SwitchCompilerTarget("ASM", IRMode.x86_64);
        }

        private async void ARM64ASMMenuItem_Click(object sender, RoutedEventArgs e) {
            await SwitchCompilerTarget("ASM", IRMode.ARM64);
        }

        private async void DotNetMenuItem_Click(object sender, RoutedEventArgs e) {
            await SwitchCompilerTarget("DotNet", IRMode.x86_64);
        }
        private async void DotNetARM64MenuItem_Click(object sender, RoutedEventArgs e) {
            await SwitchCompilerTarget("DotNet", IRMode.ARM64);
        }

        private async Task SetupCompilerTarget() {
            if(!string.IsNullOrEmpty(App.Settings.DefaultCompilerIR)) {
                await SwitchCompilerTarget(App.Settings.DefaultCompilerIR, App.Settings.DefaultIRMode);
            }
            else {
                await SwitchCompilerTarget("UTC");
            }
        }

        private async Task SwitchCompilerTarget(string name, IRMode irMode = IRMode.Default) {
            //? TODO: Use a list of registered IRs
            switch (name) {
                case "UTC": {
                    await SwitchCompilerTarget(new UTCCompilerInfoProvider(this));
                    break;
                }
                case "LLVM": {
                    await SwitchCompilerTarget(new LLVMCompilerInfoProvider());
                    break;
                }
                case "ASM": {
                    await SwitchCompilerTarget(new ASMCompilerInfoProvider(irMode, this));
                    break;
                }
                case "DotNet": {
                    await SwitchCompilerTarget(new DotNetCompilerInfoProvider(irMode, this));
                    break;
                }
                default: {
                    await SwitchCompilerTarget(new UTCCompilerInfoProvider(this));
                    break;
                }
            }

            App.Settings.SwitchDefaultCompilerIR(compilerInfo_.CompilerIRName, irMode);
            App.SaveApplicationSettings();
        }

        private void ShareButton_Click(object sender, RoutedEventArgs e) {
            var width = Math.Max(SessionSharingPanel.MinimumWidth,
                                  Math.Min(MainGrid.ActualWidth, SessionSharingPanel.DefaultWidth));
            var height = Math.Max(SessionSharingPanel.MinimumHeight,
                                   Math.Min(MainGrid.ActualHeight, SessionSharingPanel.DefaultHeight));
            var position = MainGrid.PointToScreen(new Point(236, MainMenu.ActualHeight + 1));
            var sharingPanel = new SessionSharingPanel(position, width, height, this, this);
            sharingPanel.IsOpen = true;
        }

        private void FindButton_Click(object sender, RoutedEventArgs e) {
            ShowDocumentSearchPanel();
        }

        private bool documentSearchVisible_;
        private DocumentSearchPanel documentSearchPanel_;

        private void ShowDocumentSearchPanel() {
            if (documentSearchVisible_) {
                return;
            }

            if (sessionState_ == null || sessionState_.Documents.Count == 0) {
                // No proper session started yet.
                return;
            }

            var position = new Point(236, MainMenu.ActualHeight + 1);
            documentSearchPanel_ = new DocumentSearchPanel(position, 800, 500, this, this, sessionState_.Documents[0]);
            documentSearchPanel_.PopupClosed += DocumentSearchPanel__PopupClosed;
            documentSearchPanel_.PopupDetached += DocumentSearchPanel__PopupDetached;
            documentSearchPanel_.IsOpen = true;
            documentSearchVisible_ = true;
        }

        private void DocumentSearchPanel__PopupDetached(object sender, EventArgs e) {
            RegisterDetachedPanel(documentSearchPanel_);
        }

        private void DocumentSearchPanel__PopupClosed(object sender, EventArgs e) {
            CloseDocumentSearchPanel();
        }

        private void CloseDocumentSearchPanel() {
            if (!documentSearchVisible_) {
                return;
            }

            if (documentSearchPanel_.IsDetached) {
                UnregisterDetachedPanel(documentSearchPanel_);
            }

            documentSearchPanel_.IsOpen = false;
            documentSearchPanel_.PopupClosed -= DocumentSearchPanel__PopupClosed;
            documentSearchPanel_.PopupDetached -= DocumentSearchPanel__PopupDetached;
            documentSearchPanel_ = null;
            documentSearchVisible_ = false;
        }

        private bool SaveDockLayout() {
            return SaveDockLayout(App.GetDefaultDockLayoutFilePath());
        }

        private bool SaveDockLayout(string dockLayoutFile) {
            try {
                var serializer = new XmlLayoutSerializer(DockManager);
                serializer.Serialize(dockLayoutFile);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save dock layout: {ex}");
                return false;
            }
        }

        public ProfileData ProfileData => sessionState_?.ProfileData;

        private async void MenuItem_OnClick2(object sender, RoutedEventArgs e) {
            SectionPanel.ShowModuleReport();
        }

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

        private void WorkspaceCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (WorkspaceCombobox.SelectedIndex == 0) {
                RestoreDockLayout();
            }
            else {
                var prof = App.GetDockLayoutFilePath("profiling");

                if (File.Exists(prof)) {
                    RestoreDockLayout(prof);
                }
                //else {
                //    SaveDockLayout(prof);
                //}
            }
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
            Trace.Flush();

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

            var args = new OpenSectionEventArgs(node.Function.Sections[0], openMode);
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
            var panel = FindPanel(ToolPanelKind.Source) as SourceFilePanel;

            if (panel != null) {
                await panel.LoadSourceFile(node.Function.Sections[0]);
                return true;
            }
            
            return false;
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
                    panel.SelectFunction(func);
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

        private CancelableTaskInstance updateProfileTask_ = new CancelableTaskInstance();
        
        public async Task<bool> ProfileSampleRangeSelected(SampleTimeRangeInfo range) {
            using var cancelableTask = await updateProfileTask_.CancelPreviousAndCreateTaskAsync();
            
            //? TODO: If an event fires during the call tree/sample filtering,
            //? either ignore it or better ruin it after the filtering is done
            if (ProfileData.CallTree == null) {
                return false;
            }
            
            var funcs = await Task.Run(() => FindFunctionsForSamples(range.StartSampleIndex, range.EndSampleIndex, 
                                                                     range.ThreadId, ProfileData));
            var sectinPanel = FindPanel(ToolPanelKind.Section) as SectionPanelPair;
            sectinPanel?.MarkFunctions(funcs.ToList());

            var nodes = await Task.Run(() => FindCallTreeNodesForSamples(funcs, ProfileData));
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

            if (sourcePanelKind != ToolPanelKind.CallerCallee) {
                await SwitchActiveFunction(node.Function);
            }

            var callTreePanel = FindPanel(ToolPanelKind.CallTree) as CallTreePanel;
            callTreePanel?.SelectFunction(node.Function);
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

            int index = 0;

            //? Also here - Abstract parallel run chunks to take action per sample

            for (int i = sampleStartIndex; i < sampleEndIndex; i++) {
                var (sample, stack) = profile.Samples[i];
                foreach (var stackFrame in stack.StackFrames) {
                    if (stackFrame.IsUnknown)
                        continue;

                    if (stackFrame.Info.Function.Value.Equals(node.Function)) {
                        var threadList = threadListMap.GetOrAddValue(stack.Context.ThreadId);
                        threadList.Add(new SampleIndex(index, sample.Time));
                        allThreadsList.Add(new SampleIndex(index, sample.Time));

                        break;
                    }
                }

                index++;
            }

            Trace.WriteLine($"FindSamples took: {sw.ElapsedMilliseconds} for {allThreadsList.Count} samples");
            return threadListMap;
        }


        private HashSet<IRTextFunction> FindFunctionsForSamples(int sampleStartIndex, int sampleEndIndex, int threadId, ProfileData profile) {
            var funcSet = new HashSet<IRTextFunction>();

            //? Abstract parallel run chunks to take action per sample (ComputeFunctionProfile)
            for (int i = sampleStartIndex; i < sampleEndIndex; i++) {
                var (sample, stack) = profile.Samples[i];

                if (threadId != -1 && stack.Context.ThreadId != threadId) {
                    continue;
                }

                foreach (var stackFrame in stack.StackFrames) {
                    if (stackFrame.IsUnknown)
                        continue;

                    if (stackFrame.Info.Function == null) {
                        Utils.WaitForDebugger();
                        continue;
                    }
                    
                    funcSet.Add(stackFrame.Info.Function);
                }
            }

            return funcSet;
        }

        private List<ProfileCallTreeNode> FindCallTreeNodesForSamples(HashSet<IRTextFunction> funcs, ProfileData profile) {
            var callNodes = new List<ProfileCallTreeNode>(funcs.Count);

            //? TODO: If an event fires during the call tree/sample filtering,
            //? either ignore it or better ruin it after the filtering is done
            if (ProfileData.CallTree == null) {
                return callNodes;
            }
            
            foreach (var func in funcs) {
                if (func == null) {
                    Utils.WaitForDebugger();
                }
                
                var nodes = profile.CallTree.GetCallTreeNodes(func);
                if (nodes != null) {
                    callNodes.AddRange(nodes);
                }
            }

            return callNodes;
        }

    }
}