// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoUpdaterDotNET;
using AvalonDock.Controls;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerUI.Compilers.ASM;
using IRExplorerUI.Compilers.LLVM;
using IRExplorerUI.Controls;
using IRExplorerUI.Panels;
using IRExplorerUI.Scripting;
using IRExplorerUI.Settings;
using IRExplorerUI.Utilities;
using IRExplorerUI.Windows;

namespace IRExplorerUI;

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
  private DispatcherTimer updateTimer_;
  private List<DraggablePopup> detachedPanels_;
  private Point previousWindowPosition_;
  private DateTime lastDocumentLoadTime_;
  private DateTime lastDocumentReloadQueryTime_;
  private DelayedAction statusTextAction_;
  private object lockObject_;
  private bool documentSearchVisible_;
  private DocumentSearchPanel documentSearchPanel_;
  private bool initialDockLayoutRestored_;

  public MainWindow() {
    InitializeComponent();
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

  public SessionStateManager SessionState => sessionState_;

  private static void CheckForUpdate() {
    string autoUpdateInfo;

    switch (RuntimeInformation.OSArchitecture) {
      case Architecture.Arm64:
        autoUpdateInfo = App.AutoUpdateInfoArm64;
        break;
      case Architecture.X64:
        autoUpdateInfo = App.AutoUpdateInfox64;
        break;
      default:
        autoUpdateInfo = App.AutoUpdateInfox64;
        break;
    }

    try {
      AutoUpdater.Start(autoUpdateInfo);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed update check: {ex}");
    }
  }

  private static void InstallExtension() {
    App.InstallExtension();
  }

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

  public Task SwitchActiveFunction(IRTextFunction function, bool handleProfiling = true) {
    return SectionPanel.SelectFunction(function, handleProfiling);
  }

  protected override void OnSourceInitialized(EventArgs e) {
    base.OnSourceInitialized(e);
    WindowPlacement.SetPlacement(this, App.Settings.MainWindowPlacement);
  }

  private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) {
    UpdateStartPagePanelPosition();
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

    foreach (string documentPath in reloadedDocuments) {
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
    App.Session = this;
    PopulateRecentFilesMenu();
    PopulateWorkspacesCombobox();
    ThemeCombobox.SelectedIndex = App.Settings.ThemeIndex;
    DiffModeButton.IsEnabled = false;
  }

  private void SetupMainWindowCompilerTarget() {
    IRTypeLabel.Content = compilerInfo_.CompilerDisplayName;
    RestoreDockLayout();
  }

  private void AddRecentFile(string path) {
    App.Settings.AddRecentFile(path);
    App.SaveApplicationSettings();
    PopulateRecentFilesMenu();
  }

  private void PopulateRecentFilesMenu() {
    var savedItems = new List<object>();

    foreach (object item in RecentFilesMenu.Items) {
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

    foreach (object item in savedItems) {
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
    
    SaveDockLayout();
    App.SaveApplicationSettings();
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

  private async void MainWindow_ContentRendered(object sender, EventArgs e) {
    SetupStartPagePanel();

    if (sessionState_ == null) {
      ShowStartPage();
    }

    var time = DateTime.UtcNow - App.AppStartTime;
    DevMenuStartupTime.Header = $"Startup time: {time.TotalMilliseconds} ms";

    DelayedAction.StartNew(TimeSpan.FromSeconds(3), () => {
      Dispatcher.BeginInvoke(new Action(() => {
        CheckForUpdate();
      }));
    });

    var args = Environment.GetCommandLineArgs();

    if (args.Length > 1 && args[1] == "--open-trace") {
      var window = new ProfileLoadWindow(this, false, true);
      window.Owner = this;
      var result = window.ShowDialog();

      if (result.HasValue && result.Value) {
        await SectionPanel.RefreshModuleSummaries();
        SetOptionalStatus(TimeSpan.FromSeconds(10), "Profile data loaded");
      }
    }
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
          title +=
            $" ({Utils.TryGetFileName(ProfileData.Report.TraceInfo.TraceFilePath)}, {sessionState_.MainDocument.BinaryFile})";
        }
        else {
          title += $" ({sessionState_.MainDocument.BinaryFile})";
        }
      }
    }
    else {
      if (sessionState_.Documents.Count == 1) {
        string name = sessionState_.Documents[0].BinaryFile?.FilePath ?? sessionState_.Documents[0].FilePath;
        title += $" - {name}";
      }
      else if (sessionState_.Documents.Count == 2) {
        string baseName = sessionState_.Documents[0].BinaryFile?.FilePath ?? sessionState_.Documents[0].FilePath;
        string diffName = sessionState_.Documents[1].BinaryFile?.FilePath ?? sessionState_.Documents[1].FilePath;
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
    string[] args = Environment.GetCommandLineArgs();

    if (args.Length > 1 && args[1] == "--open-trace") {
      // Opening IR Explorer with a trace is handled once main window is rendered. maybe move all arg parsing and options to there?
    }
    else if (args.Length >= 3) {
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
          Close();
        }
        else if (args[3].EndsWith("func")) {
          // Open a certain function and section.
          string funcName = args[4];
          var func = sessionState_.MainDocument.Summary.FindFunction(funcName);

          if (func != null) {
            await SectionPanel.SelectFunction(func);

            if (args.Length >= 7) {
              if (args[5].EndsWith("section")) {
                string sectionName = args[6];
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
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) ==
        MessageBoxResult.Yes) {
      App.Settings.ClearRecentComparedFiles();
      App.SaveApplicationSettings();
      StartPage.ReloadFileList();
    }
  }

  private void StartPage_ClearRecentDocuments(object sender, EventArgs e) {
    using var centerForm = new DialogCenteringHelper(this);

    if (MessageBox.Show("Clear the list of recent documents?", "IR Explorer",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) ==
        MessageBoxResult.Yes) {
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
      double height = Math.Max(StartPage.MinHeight, documentHost.ActualHeight * 0.9);
      double left = documentHost.ActualWidth / 2 - StartPage.ActualWidth / 2;
      double top = documentHost.ActualHeight / 2 - height / 2;
      StartPage.Height = height;
      StartPage.Margin = new Thickness(left, top, 0, 0);
    }
  }

  private void ShowStartPage() {
    StartPage.ReloadFileList();
    StartPage.Visibility = Visibility.Visible;
    Utils.EnableControl(StartPage);
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
    string title = compilerInfo_.NameProvider.GetSectionName(section);

    if (sessionState_.SectionDiffState.IsEnabled) {
      if (sessionState_.SectionDiffState.LeftDocument == document) {
        return $"Base: {title}";
      }

      if (sessionState_.SectionDiffState.RightDocument == document) {
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

  private void UpdateButton_Click(object sender, RoutedEventArgs e) {
    var updateWindow = new UpdateWindow(UpdateButton.Tag as UpdateInfoEventArgs);
    updateWindow.Owner = this;
    bool? installUpdate = updateWindow.ShowDialog();

    if (installUpdate.HasValue && installUpdate.Value) {
      Close();
    }
    
    UpdateButton.Visibility = Visibility.Collapsed;
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
    var window = new AboutWindow();
    window.Owner = this;
    window.ShowDialog();
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

  private void MenuItem_Click_9(object sender, RoutedEventArgs e) {
    InstallExtension();
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
    await SwitchCompilerTarget(App.Settings.DefaultCompilerIR, App.Settings.DefaultIRMode);
  }

  private async Task SwitchCompilerTarget(string name, IRMode irMode = IRMode.Default) {
    //? TODO: Use a list of registered IRs
    switch (name) {
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
        await SwitchCompilerTarget(new ASMCompilerInfoProvider(irMode, this));
        break;
      }
    }

    App.Settings.SwitchDefaultCompilerIR(compilerInfo_.CompilerIRName, irMode);
    App.SaveApplicationSettings();
  }

  private void ShareButton_Click(object sender, RoutedEventArgs e) {
    double width = Math.Max(SessionSharingPanel.MinimumWidth,
                            Math.Min(MainGrid.ActualWidth, SessionSharingPanel.DefaultWidth));
    double height = Math.Max(SessionSharingPanel.MinimumHeight,
                             Math.Min(MainGrid.ActualHeight, SessionSharingPanel.DefaultHeight));
    var position = MainGrid.PointToScreen(new Point(236, MainMenu.ActualHeight + 1));
    var sharingPanel = new SessionSharingPanel(position, width, height, this, this);
    sharingPanel.IsOpen = true;
  }

  private void FindButton_Click(object sender, RoutedEventArgs e) {
    ShowDocumentSearchPanel();
  }

  private void MenuItem_OnClick2(object sender, RoutedEventArgs e) {
    SectionPanel.ShowModuleReport();
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

  private void ShowWorkspacesMenuClicked(object sender, RoutedEventArgs e) {
    ShowWorkspacesWindow();
  }

  private async void HelpButton_Click(object sender, RoutedEventArgs e) {
    await ShowPanel(ToolPanelKind.Help);
  }
}