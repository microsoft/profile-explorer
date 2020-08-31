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
using IRExplorerUI.Compilers.UTC;
using IRExplorerUI.Compilers.LLVM;
using IRExplorerUI.Controls;

namespace IRExplorerUI {
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
        public static readonly RoutedUICommand SwapDiffDocuments =
            new RoutedUICommand("Untitled", "SwapDiffDocuments", typeof(Window));
        public static readonly RoutedUICommand ShowDocumentSearch =
            new RoutedUICommand("Untitled", "ShowDocumentSearch", typeof(Window));
    }

    public partial class MainWindow : Window, ISession {
        private LayoutDocumentPane activeDocumentPanel_;
        private AddressMetadataTag addressTag_;
        private bool appIsActivated_;
        private DispatcherTimer autoSaveTimer_;
        private Dictionary<string, DateTime> changedDocuments_;
        private ICompilerInfoProvider compilerInfo_;
        private MainWindowState fullScreenRestoreState_;

        private Dictionary<GraphKind, GraphLayoutCache> graphLayout_;
        private bool ignoreDiffModeButtonEvent_;
        private LoadedDocument mainDocument_;
        private LoadedDocument diffDocument_;
        private Dictionary<ToolPanelKind, List<PanelHostInfo>> panelHostSet_;
        private IRTextSection previousDebugSection_;
        private SessionStateManager sessionState_;
        private bool sideBySidePanelsCreated_;
        private DispatcherTimer updateTimer_;
        private List<DraggablePopup> detachedPanels_;
        private Point previousWindowPosition_;
        private DateTime lastDocumentLoadTime_;

        public MainWindow() {
            App.WindowShowTime = DateTime.UtcNow;
            InitializeComponent();

            App.Session = this;
            panelHostSet_ = new Dictionary<ToolPanelKind, List<PanelHostInfo>>();
            compilerInfo_ = new UTCCompilerInfoProvider();
            changedDocuments_ = new Dictionary<string, DateTime>();
            detachedPanels_ = new List<DraggablePopup>();

            SetupMainWindow();
            SetupGraphLayoutCache();

            DockManager.LayoutUpdated += DockManager_LayoutUpdated;
            ContentRendered += MainWindow_ContentRendered;
            StateChanged += MainWindow_StateChanged;
            LocationChanged += MainWindow_LocationChanged;
            Closing += MainWindow_Closing;
            Activated += MainWindow_Activated;
            Deactivated += MainWindow_Deactivated;
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

        public async Task SwitchDocumentSection(OpenSectionEventArgs args, IRDocument document) {
            var documentHost = FindDocumentHost(document);

            if (documentHost != null) {
                // Use the existing editor to show the section.
                await SwitchDocumentSection(args, documentHost);
            }
            else {
                // Open a new editor to show the section.
                await SwitchDocumentSection(args);
            }

            SectionPanel.SelectSection(args.Section, false);
        }

        public async Task SwitchGraphsAsync(GraphPanel graphPanel, IRTextSection section,
                                            IRDocument document) {
            var action = GetComputeGraphAction(graphPanel.PanelKind);
            await SwitchGraphsAsync(graphPanel, section, document, action);
        }

        public void ShowAllReferences(IRElement element, IRDocument document) {
            var panelInfo = FindTargetPanel(document, ToolPanelKind.References);
            var refPanel = panelInfo.Panel as ReferencesPanel;
            panelInfo.Host.IsSelected = true;
            refPanel.FindAllReferences(element);
        }

        public void ShowSSAUses(IRElement element, IRDocument document) {
            var panelInfo = FindTargetPanel(document, ToolPanelKind.References);
            var refPanel = panelInfo.Panel as ReferencesPanel;
            panelInfo.Host.IsSelected = true;
            refPanel.FindSSAUses(element);
        }



        private void MainWindow_Deactivated(object sender, EventArgs e) {
            appIsActivated_ = false;
        }

        private async void MainWindow_Activated(object sender, EventArgs e) {
            appIsActivated_ = true;

            foreach (var pair in changedDocuments_) {
                if (pair.Value < lastDocumentLoadTime_) {
                    continue; // Event happened before the last document reload, ignore.
                }

                if (ShowDocumentReloadQuery(pair.Key)) {
                    await ReloadDocument(pair.Key);
                }
            }

            changedDocuments_.Clear();
        }

        private void SetupMainWindow() {
            PopulateRecentFilesMenu();
            ThemeCombobox.SelectedIndex = App.Settings.ThemeIndex;
            DiffModeButton.IsEnabled = false;
            IRTypeLabel.Content = compilerInfo_.CompilerIRName;
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
                await OpenDocument((string)menuItem.Tag);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e) {
            // Save settings, including the window state.
            App.Settings.MainWindowPlacement = WindowPlacement.GetPlacement(this);
            App.Settings.ThemeIndex = ThemeCombobox.SelectedIndex;
            App.SaveApplicationSettings();

            if (sessionState_ == null) {
                return;
            }

            if (sessionState_.Info.IsFileSession) {
                using var centerForm = new DialogCenteringHelper(this);

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
                    using var centerForm = new DialogCenteringHelper(this);

                    if (MessageBox.Show("Save file changes as a new session before closing?", "IR Explorer",
                                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No,
                                        MessageBoxOptions.DefaultDesktopOnly) ==
                        MessageBoxResult.Yes) {
                        SaveDocumentExecuted(this, null);
                    }
                }
            }

            EndSession();
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e) {
            SetupStartPagePanel();

            if (sessionState_ == null) {
                ShowStartPage();
            }

            //? TODO: DEV ONLY
            var now = DateTime.UtcNow;
            var time = now - App.AppStartTime;
            DevMenuStartupTime.Header = $"Startup time: {time.TotalMilliseconds} ms";

            DelayedAction.StartNew(TimeSpan.FromSeconds(30), () => {
                Dispatcher.BeginInvoke(() =>
                    CheckForUpdate()
                );
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

        private void ShowSectionPanelDiffs(LoadedDocument result) {
            SectionPanel.DiffSummary = result.Summary;
            SectionPanel.DiffTitle = result.FileName;
        }

        private void ShowProgressBar() {
            documentLoadProgressVisible_ = true;
            DocumentLoadProgressPanel.Visibility = Visibility.Visible;
        }

        private void HideProgressBar() {
            DocumentLoadProgressPanel.Visibility = Visibility.Collapsed;
            documentLoadProgressVisible_ = false;
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

        private async void Window_Loaded(object sender, RoutedEventArgs e) {
            SectionPanel.OpenSection += SectionPanel_OpenSection;
            SectionPanel.EnterDiffMode += SectionPanel_EnterDiffMode;
            SearchResultsPanel.OpenSection += SectionPanel_OpenSection;

            RegisterDefaultToolPanels();
            ResetStatusBar();

            //? TODO: This needs a proper arg parsing lib
            var args = Environment.GetCommandLineArgs();

            if (args.Length >= 3) {
                string baseFilePath = args[1];
                string diffFilePath = args[2];

                if (File.Exists(baseFilePath) && File.Exists(diffFilePath)) {
                    await OpenBaseDiffIRDocumentsImpl(baseFilePath, diffFilePath);
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

                        var session = new ScriptSession(null, this) {
                            SilentMode = true,
                            SessionName = Path.GetFileNameWithoutExtension(baseFilePath)
                        };

                        if (script.Execute(session) && script.ScriptResult) {
                            if (args.Length >= 7) {
                                if (args[5].EndsWith("out")) {
                                    string scriptOutPath = args[6];
                                    session.SaveOutput(scriptOutPath);
                                }
                            }
                        }

                        this.Close();
                    }
                    else if (args[3].EndsWith("func")) {
                        var funcName = args[4];
                        var func = mainDocument_.Summary.FindFunction(funcName);

                        if (func != null) {
                            SectionPanel.SelectFunction(func);

                            if (args.Length >= 7) {
                                if (args[5].EndsWith("section")) {
                                    var sectionName = args[6];
                                    var section = func.FindSection(sectionName);

                                    if (section != null) {
                                        SectionPanel.SelectSection(section);
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
            await OpenBaseDiffsDocuments(e.Item1, e.Item2);
        }

        private async void StartPage_OpenRecentDocument(object sender, string e) {
            await OpenDocument(e);
        }

        private void UpdateStartPagePanelPosition() {
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

        private void CommandBinding_PreviewCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = true;
            e.Handled = true;
        }

        private string GetSectionName(IRTextSection section) {
            string name = compilerInfo_.NameProvider.GetSectionName(section);
            return $"({section.Number}) {name}";
        }

        private string GetDocumentDescription(IRTextSection section) {
            var docInfo = sessionState_.FindLoadedDocument(section);
            return $"{section.ParentFunction.Name.Trim()} ({docInfo.FileName})";
        }

        private void MenuItem_Click_1(object sender, RoutedEventArgs e) {
            EndSession();
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

        private async void MenuItem_Click(object sender, RoutedEventArgs e) {
            //throw new InvalidOperationException("Crash Handler test assert");
            var loadedDoc = sessionState_.FindLoadedDocument(MainDocumentSummary);
            var cg = new CallGraph(MainDocumentSummary, loadedDoc.Loader, CompilerInfo.IR);
            cg.CallGraphNodeCreated += Cg_CallGraphNodeCreated;

            var active = FindActiveDocumentView();

            if (active != null) {
                cg.Execute(active.Section.ParentFunction, "Tuples after Reader (-db7 == DB_INITIAL)");
            }
            else {
                cg.Execute("Tuples after Reader (-db7 == DB_INITIAL)");
            }

            var options = new CallGraphPrinterOptions() {
                ////UseSingleIncomingEdge = true,
                //UseStraightLines = true,
                UseExternalNode = true
            };

            var printer = new CallGraphPrinter(cg, options);
            var result = printer.PrintGraph();
            var graphText = printer.CreateGraph(result, new CancelableTask());

            var panel = new CallGraphPanel();
            panel.Session = this;
            panel.OnRegisterPanel();



            //var window = new Window();
            //window.Content = panel;
            //window.Width = 1000;
            //window.Height = 900;

            var graphReader = new GraphvizReader(GraphKind.CallGraph, graphText, printer.CreateNodeDataMap());
            var layoutGraph = graphReader.ReadGraph();
            layoutGraph.GraphOptions = options;

            AddNewPanel(panel);
            DisplayNewPanel(panel, null, DuplicatePanelKind.Floating);
            panel.DisplayGraph(layoutGraph);
            //window.Show();
        }

        private void Cg_CallGraphNodeCreated(object sender, CallGraphEventArgs e) {
            int instrs = e.Function.InstructionCount;

            if (instrs == 0) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(0, 10));
            }
            else if (instrs <= 2) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(2, 10));
            }
            else if (instrs <= 5) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(4, 10));
            }
            else if (instrs <= 10) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(5, 10));
            }
            else if (instrs <= 20) {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(6, 10));
            }
            else {
                e.FunctionNode.AddTag(GraphNodeTag.MakeHeatMap2(8, 10));
            }

            e.FunctionNode.GetTag<GraphNodeTag>().Label = $"{instrs} instrs";
        }

        private void MenuItem_Click_3(object sender, RoutedEventArgs e) {
            CreateDefaultSideBySidePanels();
        }

        private void ShowPanelMenuClicked(object sender, RoutedEventArgs e) {
            switch (((MenuItem)sender).Tag) {
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

            SetupStartPagePanel();

            MessageBox.Show($"IR Explorer\n© 2019 Microsoft Corporation\n\nVersion: {version}", "About",
                            MessageBoxButton.OK);
        }

        private void MenuItem_Click_7(object sender, RoutedEventArgs e) {
            CreateQueryPanel();
        }

        private QueryPanel CreateQueryPanel() {
            var documentHost = Utils.FindChildLogical<LayoutDocumentPaneGroupControl>(this);
            var position = new Point();

            if (documentHost != null) {
                var left = documentHost.ActualWidth - QueryPanel.DefaultWidth - 32;
                var top = documentHost.ActualHeight - QueryPanel.DefaultHeight - 32;
                position = documentHost.PointToScreen(new Point(left, top));
            }

            var queryPanel = new QueryPanel(position, QueryPanel.DefaultWidth, QueryPanel.DefaultHeight, documentHost, this);
            queryPanel.PanelTitle = "Queries";
            queryPanel.ShowAddButton = true;
            queryPanel.PopupClosed += QueryPanel_Closed;
            queryPanel.IsOpen = true;
            queryPanel.StaysOpen = true;
            RegisterDetachedPanel(queryPanel);
            return queryPanel;
        }

        private void QueryPanel_Closed(object sender, EventArgs e) {
            var queryPanel = (QueryPanel)sender;
            queryPanel.PopupClosed -= QueryPanel_Closed;
            queryPanel.IsOpen = false;
            UnregisterDetachedPanel(queryPanel);
        }

        public void LoadDocumentQuery(QueryDefinition query, IRDocument document) {
            //? TODO: Show the panel over the associated document
            var queryPanel = CreateQueryPanel();
            queryPanel.AddQuery(query);
        }

        private void SetOptionalStatus(string text, string tooltip = "") {
            OptionalStatusText.Text = text;
            OptionalStatusText.Foreground = Brushes.DarkGreen;
            OptionalStatusText.ToolTip = tooltip;
        }

        private void MenuItem_Click_8(object sender, RoutedEventArgs e) {
            StartGrpcServer();
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

        private void SwitchCompilerTarget(ICompilerInfoProvider compilerInfo) {
            EndSession();
            compilerInfo_ = compilerInfo;
            SetupMainWindow();
        }

        private void LLVMMenuItem_Click(object sender, RoutedEventArgs e) {
            SwitchCompilerTarget(new LLVMCompilerInfoProvider());
        }

        private void UTCMenuItem_Click(object sender, RoutedEventArgs e) {
            SwitchCompilerTarget(new UTCCompilerInfoProvider());
        }

        private async void ShareButton_Click(object sender, RoutedEventArgs e) {
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

            if (sessionState_ == null) {
                // No session started yet.
                return;
            }

            var position = MainGrid.PointToScreen(new Point(236, MainMenu.ActualHeight + 1));
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
    }
}
