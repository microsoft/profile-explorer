// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;

namespace IRExplorerUI;

public partial class ProfileLoadWindow : Window, INotifyPropertyChanged {
  private CancelableTaskInstance loadTask_;
  private bool isLoadingProfile_;
  private ProfileDataProviderOptions options_;
  private ProfileRecordingSessionOptions recordingOptions_;
  private SymbolFileSourceSettings symbolSettings_;
  private RawProfileData recordedProfile_;
  private bool isLoadingProcessList_;
  private bool showProcessList_;
  private bool isRecordingProfile_;
  private List<ProcessSummary> processList_;
  private List<ProcessSummary> selectedProcSummary_;
  private List<ProcessSummary> recoredProcSummaries_;
  private bool windowClosed_;
  private RecordingSession currentSession_;
  private RecordingSession loadedSession_;
  private int enabledPerfCounters_;
  private ICollectionView perfCountersFilter_;
  private ICollectionView metricsFilter_;
  private ICollectionView processFilter_;
  private string profileFilePath_;
  private string binaryFilePath_;
  private bool showOnlyManagedProcesses_;

  public ProfileLoadWindow(ISession session, bool recordMode, bool isOnLaunch = false,
                           RecordingSession loadedSession = null) {
    InitializeComponent();
    DataContext = this;
    Session = session;
    loadTask_ = new CancelableTaskInstance(false);
    IsRecordMode = recordMode;
    IsOnLaunch = isOnLaunch;
    loadedSession_ = loadedSession;

    if (IsRecordMode) {
      Title = "Record profile trace";
    }

    Options = App.Settings.ProfileOptions;
    SymbolSettings = App.Settings.SymbolSettings;
    RecordingOptions = new ProfileRecordingSessionOptions();

    UpdatePerfCounterList();
    ReloadSymbolPathsList();
    SetupSessionList();
    ContentRendered += ProfileLoadWindow_ContentRendered;
    Closing += ProfileLoadWindow_Closing;
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public ISession Session { get; set; }

  public string ProfileFilePath {
    get => profileFilePath_;
    set {
      profileFilePath_ = value;
      OnPropertyChange(nameof(ProfileFilePath));
    }
  }

  public string BinaryFilePath {
    get => binaryFilePath_;
    set {
      binaryFilePath_ = value;
      OnPropertyChange(nameof(BinaryFilePath));
    }
  }

  public bool RequiresElevation => ETWRecordingSession.RequiresElevation;
  public bool LoadingControlsVisible => !IsRecordMode || ShowProcessList;
  public bool RecentControlsVisible => !IsRecordMode && !IsLoadingProfile;

  public bool IsLoadingProfile {
    get => isLoadingProfile_;
    set {
      if (isLoadingProfile_ != value) {
        isLoadingProfile_ = value;
        OnPropertyChange(nameof(IsLoadingProfile));
        OnPropertyChange(nameof(InputControlsEnabled));
        OnPropertyChange(nameof(ProcessListEnabled));
      }
    }
  }

  public bool IsRecordingProfile {
    get => isRecordingProfile_;
    set {
      if (isRecordingProfile_ != value) {
        isRecordingProfile_ = value;
        OnPropertyChange(nameof(IsRecordingProfile));
        OnPropertyChange(nameof(RecordingControlsEnabled));
        OnPropertyChange(nameof(RecordingStopControlsEnabled));
        OnPropertyChange(nameof(LoadingControlsVisible));
      }
    }
  }

  public bool IsLoadingProcessList {
    get => isLoadingProcessList_;
    set {
      if (isLoadingProcessList_ != value) {
        isLoadingProcessList_ = value;
        OnPropertyChange(nameof(IsLoadingProcessList));
      }
    }
  }

  public bool ShowProcessList {
    get => showProcessList_;
    set {
      if (showProcessList_ != value) {
        showProcessList_ = value;
        OnPropertyChange(nameof(ShowProcessList));
        OnPropertyChange(nameof(LoadingControlsVisible));
      }
    }
  }

  public int EnabledPerfCounters {
    get => enabledPerfCounters_;
    set {
      if (enabledPerfCounters_ != value) {
        enabledPerfCounters_ = value;
        OnPropertyChange(nameof(EnabledPerfCounters));
      }
    }
  }

  public bool InputControlsEnabled => !IsLoadingProfile;
  public bool ProcessListEnabled => !IsLoadingProfile || IsLoadingProcessList;
  public bool RecordingControlsEnabled => !IsLoadingProfile && !IsRecordingProfile;
  public bool RecordingStopControlsEnabled => !IsLoadingProfile && IsRecordingProfile;
  public bool IsRecordMode { get; }
  public bool IsOnLaunch { get; }

  public ProfileRecordingSessionOptions RecordingOptions {
    get => recordingOptions_;
    set {
      recordingOptions_ = value;
      OnPropertyChange(nameof(RecordingOptions));
      OnPropertyChange(nameof(Options));
    }
  }

  public ProfileDataProviderOptions Options {
    get => options_;
    set {
      options_ = value;
      OnPropertyChange(nameof(Options));
      OnPropertyChange(nameof(RecordingOptions));
    }
  }

  public SymbolFileSourceSettings SymbolSettings {
    get => symbolSettings_;
    set {
      symbolSettings_ = value;
      OnPropertyChange(nameof(SymbolSettings));
    }
  }

  public bool ShowOnlyManagedProcesses {
    get => showOnlyManagedProcesses_;
    set {
      if (showOnlyManagedProcesses_ != value) {
        showOnlyManagedProcesses_ = value;
        UpdateRunningProcessList();
      }
    }
  }

  public void UpdatePerfCounterList() {
    var counters = ETWRecordingSession.BuiltinPerformanceCounters;

    foreach (var counter in counters) {
      recordingOptions_.AddPerformanceCounter(counter);
    }

    var metrics = ETWRecordingSession.BuiltinPerformanceMetrics;

    foreach (var metric in metrics) {
      options_.AddPerformanceMetric(metric);
    }

    var counterList = new ObservableCollectionRefresh<PerformanceCounterConfig>(recordingOptions_.PerformanceCounters);
    perfCountersFilter_ = counterList.GetFilterView();
    perfCountersFilter_.Filter = FilterCounterList;
    PerfCounterList.ItemsSource = null;
    PerfCounterList.ItemsSource = perfCountersFilter_;

    var metricList = new ObservableCollectionRefresh<PerformanceMetricConfig>(options_.PerformanceMetrics);
    metricsFilter_ = metricList.GetFilterView();
    metricsFilter_.Filter = FilterMetricsList;
    PerfMetricsList.ItemsSource = null;
    PerfMetricsList.ItemsSource = metricsFilter_;
  }

  public void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }

  private void SetupSessionList() {
    var sessionList = new List<RecordingSession>();

    if (IsRecordMode) {
      sessionList.Add(new RecordingSession(null, false, true,
                                           "Record", "Start a new session"));

      foreach (var prevSession in Options.PreviousRecordingSessions) {
        sessionList.Add(new RecordingSession(prevSession, false));
      }
    }
    else {
      sessionList.Add(new RecordingSession(null, false, true,
                                           "Open", "Load a trace"));

      foreach (var prevSession in Options.PreviousLoadedSessions) {
        sessionList.Add(new RecordingSession(prevSession, true));
      }
    }

    currentSession_ = sessionList[0];
    SessionList.ItemsSource = null; // Force update.
    SessionList.ItemsSource = new ListCollectionView(sessionList);
  }

  private void ProfileLoadWindow_ContentRendered(object sender, EventArgs e) {
    //? TODO: Use CreateSessionFromCommandLineArgs
    if (IsOnLaunch) {
      string[] args = Environment.GetCommandLineArgs();

      if (args.Length >= 6 && args[1] == "--open-trace") {
        string traceFilePath = args[2];

        if (!File.Exists(traceFilePath) || Path.GetExtension(traceFilePath) != ".etl") {
          MessageBox.Show("Trace file does not exist.");
          return;
        }

        string symbolPath = args[3];

        if (!int.TryParse(args[4], out int processId)) {
          MessageBox.Show("Process ID is not an integer.");
          return;
        }

        string imageFileName = args[5];

        LoadProfileFromArgs(traceFilePath, symbolPath, processId, imageFileName);
      }
    }
    else if (loadedSession_ != null) {
      SwitchCurrentSession(loadedSession_);
    }
  }

  private void ProfileLoadWindow_Closing(object sender, CancelEventArgs e) {
    // Don't close the window while profile is loading.
    if (IsLoadingProfile) {
      e.Cancel = true;
      return;
    }

    SaveCurrentOptions();
  }

  private void SaveCurrentOptions() {
    options_.RecordingSessionOptions = recordingOptions_;
    App.SaveApplicationSettings();
  }

  private bool FilterCounterList(object value) {
    string text = CounterFilter.Text.Trim();

    if (text.Length < 2) {
      return true;
    }

    var counter = (PerformanceCounterConfig)value;

    if (counter.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    return false;
  }

  private bool FilterMetricsList(object value) {
    string text = MetricFilter.Text.Trim();

    if (text.Length < 2) {
      return true;
    }

    var counter = (PerformanceMetricConfig)value;

    if (counter.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    return false;
  }

  private async void LoadButton_Click(object sender, RoutedEventArgs e) {
    await OpenFilesAndComplete(symbolSettings_);
  }

  private async Task OpenFilesAndComplete(SymbolFileSourceSettings symbolSettings) {
    if (await OpenFiles(symbolSettings) && !windowClosed_) {
      DialogResult = true;
      Close();
    }
  }

  private async Task<bool> OpenFiles(SymbolFileSourceSettings symbolSettings) {
    ProfileFilePath = Utils.CleanupPath(ProfileFilePath);
    BinaryFilePath = Utils.CleanupPath(BinaryFilePath);

    if (!IsRecordMode &&
        !Utils.ValidateFilePath(ProfileFilePath, ProfileAutocompleteBox, "profile", this)) {
      return false;
    }

    using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();

    var report = new ProfileDataReport {
      RunningProcesses = processList_,
      SymbolSettings = symbolSettings_.Clone()
    };

    bool success = false;
    IsLoadingProfile = true;
    LoadProgressBar.Value = 0;

    if (selectedProcSummary_ == null) {
      IsLoadingProfile = false;
      return false;
    }

    var processIds = selectedProcSummary_.ConvertAll(proc => proc.Process.ProcessId);

    if (IsRecordMode) {
      report.RecordingSessionOptions = recordingOptions_.Clone();
      symbolSettings = symbolSettings_.WithSymbolPaths(recordingOptions_.ApplicationPath);

      if (recordingOptions_.HasWorkingDirectory) {
        symbolSettings = symbolSettings.WithSymbolPaths(recordingOptions_.WorkingDirectory);
      }

      success = await Session.LoadProfileData(recordedProfile_, processIds,
                                              options_, symbolSettings, report,
                                              ProfileLoadProgressCallback, task);
    }
    else {
      symbolSettings = symbolSettings_.Clone();
      success = await Session.LoadProfileData(ProfileFilePath, processIds,
                                              options_, symbolSettings, report,
                                              ProfileLoadProgressCallback, task);
    }

    if (report.TraceInfo == null) {
      report.TraceInfo = new ProfileTraceInfo(); // Not set on failure.
      report.TraceInfo.TraceFilePath = ProfileFilePath;
    }

    IsLoadingProfile = false;
    UpdateRejectedFiles(report);

    if (!success && !task.IsCanceled) {
      using var centerForm = new DialogCenteringHelper(this);
      MessageBox.Show($"Failed to load profile file {ProfileFilePath}", "IR Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
      ProfileReportPanel.ShowReportWindow(report, Session);
    }

    if (success) {
      if (IsRecordMode) {
        App.Settings.AddRecordedProfileSession(report);
      }
      else {
        App.Settings.AddLoadedProfileSession(report);
        App.Settings.AddRecentFile(report.TraceInfo.TraceFilePath);
      }

      App.SaveApplicationSettings();
    }

    return success;
  }

  private void UpdateRejectedFiles(ProfileDataReport report) {
    if (symbolSettings_.RejectPreviouslyFailedFiles) {
      foreach (var module in report.Modules) {
        if (!module.HasBinaryLoaded && module.BinaryFileInfo != null) {
          symbolSettings_.RejectBinaryFile(module.BinaryFileInfo.BinaryFile);
        }

        if (!module.HasDebugInfoLoaded && module.DebugInfoFile != null) {
          symbolSettings_.RejectSymbolFile(module.DebugInfoFile.SymbolFile);
        }
      }

      App.Settings.SymbolSettings = symbolSettings_;
      App.SaveApplicationSettings();
    }
  }

  private void ProfileLoadProgressCallback(ProfileLoadProgress progressInfo) {
    Dispatcher.Invoke((Action)(() => {
      if (progressInfo == null) {
        return;
      }

      // With multi-threaded processing, current value is not always increasing...
      if (progressInfo.Stage == ProfileLoadStage.TraceProcessing) {
        progressInfo.Current = Math.Max(progressInfo.Current, (int)LoadProgressBar.Value);
      }

      LoadProgressBar.Maximum = progressInfo.Total;
      LoadProgressBar.Value = progressInfo.Current;

      LoadProgressLabel.Text = progressInfo.Stage switch {
        ProfileLoadStage.TraceReading => "Reading trace",
        ProfileLoadStage.TraceProcessing => "Processing trace",
        ProfileLoadStage.BinaryLoading => "Loading binaries" +
                                          (!string.IsNullOrEmpty(progressInfo.Optional) ?
                                            $" ({Utils.TrimToLength(progressInfo.Optional, 15)})" : ""),
        ProfileLoadStage.SymbolLoading => "Loading symbols" +
                                          (!string.IsNullOrEmpty(progressInfo.Optional) ?
                                            $" ({Utils.TrimToLength(progressInfo.Optional, 15)})" : ""),
        ProfileLoadStage.PerfCounterProcessing => "Processing CPU perf. counters",
        _ => ""
      };

      if (progressInfo.Total != 0) {
        double percentage = Math.Min(1.0, progressInfo.Current / (double)progressInfo.Total);
        ProgressPercentLabel.Text = $"{Math.Round(percentage * 100)} %";
        LoadProgressBar.IsIndeterminate = false;
      }
      else {
        LoadProgressBar.IsIndeterminate = true;
      }
    }));
  }

  private void ProcessListProgressCallback(ProcessListProgress progressInfo) {
    Dispatcher.Invoke(() => {
      if (progressInfo == null) {
        return;
      }

      LoadProgressBar.Maximum = progressInfo.Total;
      LoadProgressBar.Value = progressInfo.Current;

      if (progressInfo.Total != 0) {
        double percentage = progressInfo.Current / (double)progressInfo.Total;
        ProgressPercentLabel.Text = $"{Math.Round(percentage * 100)} %";
        LoadProgressLabel.Text = "Building process list";
        LoadProgressBar.IsIndeterminate = false;
      }
      else {
        LoadProgressBar.IsIndeterminate = true;
      }

      if (progressInfo.Processes != null) {
        DisplayProcessList(progressInfo.Processes, selectedProcSummary_);
      }
    });
  }

  private async void CancelButton_Click(object sender, RoutedEventArgs e) {
    bool closeWindow = !IsLoadingProfile; // Canceling task resets flags, decide now.

    if (isLoadingProfile_) {
      await CancelLoadingTask();
    }

    if (closeWindow) {
      App.SaveApplicationSettings();
      DialogResult = false;
      windowClosed_ = true;
      Close();
    }
  }

  private async Task CancelLoadingTask() {
    await loadTask_.CancelTaskAndWaitAsync();
  }

  private void ProfileBrowseButton_Click(object sender, RoutedEventArgs e) {
    Utils.ShowOpenFileDialog(ProfileAutocompleteBox, "ETW Trace Files|*.etl|All Files|*.*");
  }

  private async Task<bool> LoadProcessList() {
    await CancelLoadingTask();
    ProfileFilePath = Utils.CleanupPath(ProfileFilePath);

    if (File.Exists(ProfileFilePath)) {
      using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();

      IsLoadingProcessList = true;
      IsLoadingProfile = true;
      BinaryFilePath = "";
      LoadProgressBar.Value = 0;
      ResetProcessList();

      processList_ =
        await ETWProfileDataProvider.FindTraceProcesses(ProfileFilePath, options_, ProcessListProgressCallback, task);
      IsLoadingProfile = false;
      IsLoadingProcessList = false;

      if (task.IsCanceled) {
        return false;
      }

      return processList_ != null;
    }

    return false;
  }

  private void ResetProcessList() {
    processList_ = null;
    selectedProcSummary_ = null;
  }

  private void ApplicationBrowseButton_Click(object sender, RoutedEventArgs e) {
    Utils.ShowExecutableOpenFileDialog(ApplicationAutocompleteBox);
  }

  private void ProcessList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (ProcessList.SelectedItems.Count > 0) {
      selectedProcSummary_ = new List<ProcessSummary>(ProcessList.SelectedItems.OfType<ProcessSummary>());
      BinaryFilePath = selectedProcSummary_[0].Process.Name;
    }
    else {
      selectedProcSummary_ = null;
      BinaryFilePath = null;
    }
  }

  private string SetSessionApplicationPath() {
    recordingOptions_.WorkingDirectory = Utils.CleanupPath(recordingOptions_.WorkingDirectory);
    string appPath = Utils.CleanupPath(recordingOptions_.ApplicationPath);

    if (!File.Exists(appPath) && recordingOptions_.HasWorkingDirectory) {
      appPath = Path.Combine(recordingOptions_.WorkingDirectory, appPath);
    }

    recordingOptions_.ApplicationPath = appPath;
    return appPath;
  }

  private async void StartCaptureButton_OnClick(object sender, RoutedEventArgs e) {
    await StartRecordingSession();
  }

  private async Task StartRecordingSession() {
    if (recordingOptions_.SessionKind == ProfileSessionKind.StartProcess) {
      string appPath = SetSessionApplicationPath();

      if (!File.Exists(appPath)) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Could not find profiled application: {appPath}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }
    }
    else if (recordingOptions_.SessionKind == ProfileSessionKind.AttachToProcess) {
      if (selectedProcSummary_ == null) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show("Select a running process to attach to", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Error);
        return;
      }

      recordingOptions_.TargetProcessId = selectedProcSummary_[0].Process.ProcessId;
    }

    // Commit the recording options to the profiling ones.
    SaveCurrentOptions();

    if (recordingOptions_.RecordPerformanceCounters) {
      options_.IncludePerformanceCounters = true;
    }

    // Start ETW recording session.
    IsRecordingProfile = true;
    ProfileLoadProgress lastProgressInfo = null;
    using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
    using var recordingSession = new ETWRecordingSession(options_);

    // Show elapsed time in UI.
    var stopWatch = Stopwatch.StartNew();
    var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Render,
                                    (o, e) => UpdateRecordingProgress(stopWatch, lastProgressInfo), Dispatcher);
    timer.Start();

    // Start recording on another thread.
    recordedProfile_ = null;
    var recordingTask = recordingSession.StartRecording(progressInfo => { lastProgressInfo = progressInfo; }, task);

    if (recordingTask != null) {
      recordedProfile_ = await recordingTask;
    }

    // Show process list if recording successful.
    timer.Stop();
    IsRecordingProfile = false;

    if (recordedProfile_ != null) {
      processList_ = await Task.Run(() => recordedProfile_.BuildProcessSummary());
      DisplayProcessList(processList_);
    }
    else {
      using var centerForm = new DialogCenteringHelper(this);
      MessageBox.Show("Failed to record ETW sampling profile!", "IR Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }
  }

  private void UpdateRecordingProgress(Stopwatch stopWatch, ProfileLoadProgress progressInfo) {
    string status = $"{stopWatch.Elapsed.ToString(@"mm\:ss")}";

    if (progressInfo != null) {
      status += progressInfo.Stage switch {
        ProfileLoadStage.TraceReading => $", {progressInfo.Total / 1000}K samples",
        _ => ""
      };
    }

    RecordProgressLabel.Text = status;
  }

  private void DisplayProcessList(List<ProcessSummary> list, List<ProcessSummary> selectedProcSummary = null) {
    list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
    ProcessList.ItemsSource = null;
    ProcessList.ItemsSource = new ListCollectionView(list);
    ShowProcessList = true;

    if (selectedProcSummary != null) {
      // Keep selected process after updating list.
      foreach (var proc in selectedProcSummary) {
        ProcessList.SelectedItems.Add(list.Find(item => item.Process == proc.Process));
      }
    }
  }

  private async void StopCaptureButton_OnClick(object sender, RoutedEventArgs e) {
    await CancelLoadingTask();
  }

  private void RestartAppButton_OnClick(object sender, RoutedEventArgs e) {
    App.RestartApplicationAsAdmin();
  }

  private void DefaultFrequencyButton_Click(object sender, RoutedEventArgs e) {
    SamplingFrequencySlider.Value = ProfileRecordingSessionOptions.DefaultSamplingFrequency;
  }

  private void MaxFrequencyButton_Click(object sender, RoutedEventArgs e) {
    SamplingFrequencySlider.Value = ProfileRecordingSessionOptions.MaximumSamplingFrequency;
  }

  private async void ProfileAutocompleteBox_TextChanged(object sender, RoutedEventArgs e) {
    if (IsLoadingProfile) {
      return; // Ignore during load of previous session.
    }

    ShowProcessList = false;

    if (!string.IsNullOrEmpty(ProfileFilePath)) {
      if (await LoadProcessList()) {
        if (processList_ == null) {
          using var centerForm = new DialogCenteringHelper(this);
          MessageBox.Show("Failed to load ETL process list!", "IR Explorer",
                          MessageBoxButton.OK, MessageBoxImage.Exclamation);
        }

        DisplayProcessList(processList_);
      }
    }
  }

  private void ResetButton_OnClick(object sender, RoutedEventArgs e) {
    var config = (sender as Button).DataContext as PerformanceCounterConfig;

    if (config != null) {
      config.Interval = config.DefaultInterval;
      var list = (ObservableCollectionRefresh<PerformanceCounterConfig>)PerfCounterList.ItemsSource;
      list.Refresh();
    }
  }

  private void CheckBox_Checked(object sender, RoutedEventArgs e) {
    EnabledPerfCounters = recordingOptions_.EnabledPerformanceCounters.Count;
  }

  private void CheckBox_Unchecked(object sender, RoutedEventArgs e) {
    EnabledPerfCounters = recordingOptions_.EnabledPerformanceCounters.Count;
  }

  private void CounterFilter_TextChanged(object sender, TextChangedEventArgs e) {
    perfCountersFilter_?.Refresh();
  }

  private void MetricFilter_TextChanged(object sender, TextChangedEventArgs e) {
    metricsFilter_?.Refresh();
  }

  private void SessionReportButton_Click(object sender, RoutedEventArgs e) {
    var sessionEx = ((Button)sender).DataContext as RecordingSession;
    var report = sessionEx?.Report;

    if (report != null) {
      ProfileReportPanel.ShowReportWindow(report, Session);
    }
  }

  private void SessionRemoveButton_Click(object sender, RoutedEventArgs e) {
    var sessionEx = ((Button)sender).DataContext as RecordingSession;
    var report = sessionEx?.Report;

    if (report != null) {
      using var centerForm = new DialogCenteringHelper(this);

      if (MessageBox.Show("Do you want to remove the session?", "IR Explorer",
                          MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
        return;
      }

      if (IsRecordMode) {
        App.Settings.RemoveRecordedProfileSession(report);
      }
      else {
        App.Settings.RemoveLoadedProfileSession(report);
      }

      SetupSessionList();
      SaveCurrentOptions();
    }
  }

  private async void LoadProfileFromArgs(string traceFilePath, string symbolPath, int processId, string imageFileName) {
    ProfileFilePath = traceFilePath;
    BinaryFilePath = imageFileName;

    var process = new ProfileProcess(processId, imageFileName);
    selectedProcSummary_ = new List<ProcessSummary>() {
      new ProcessSummary(process, TimeSpan.Zero)
    };

    await OpenFilesAndComplete(symbolSettings_.WithSymbolPaths(symbolPath));
  }

  private async void SessionList_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
    var sessionEx = SessionList.SelectedItem as RecordingSession;

    if (sessionEx?.Report == null) {
      return;
    }

    IsLoadingProfile = true; // Prevent process list to be loaded.
    LoadPreviousSession(sessionEx.Report);

    if (IsRecordMode) {
      await StartRecordingSession();
    }
    else {
      await OpenFilesAndComplete(symbolSettings_);
    }
  }

  private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    var sessionEx = SessionList.SelectedItem as RecordingSession;

    if (sessionEx == null) {
      return;
    }

    SwitchCurrentSession(sessionEx);
  }

  private void SwitchCurrentSession(RecordingSession session) {
    var report = session.Report;

    if (IsRecordMode && currentSession_.IsNewSession) {
      SaveCurrentOptions();
    }

    if (report != null) {
      LoadPreviousSession(report);
    }
    else {
      // Reload default new options.
      if (IsRecordMode) {
        RecordingOptions = options_.RecordingSessionOptions.Clone();
      }
      else {
        ProfileFilePath = "";
        BinaryFilePath = "";
      }
    }

    UpdatePerfCounterList();
    currentSession_ = session;
  }

  private void LoadPreviousSession(ProfileDataReport report) {
    if (report.Process != null) {
      // Set previous selected process.
      selectedProcSummary_ = new List<ProcessSummary> {
        new ProcessSummary(report.Process, TimeSpan.Zero)
      };
    }

    if (IsRecordMode) {
      RecordingOptions = report.RecordingSessionOptions.Clone();
    }
    else {
      ProfileFilePath = report.TraceInfo.TraceFilePath;
      BinaryFilePath = report.Process?.ImageFileName;
    }
  }

  private void SamplingFrequencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
    // Hack to get the label to update, since RecordingOptions doesn't notify.
    OnPropertyChange(nameof(RecordingOptions));
  }

  private void ProcessListMenuItem_OnClick(object sender, RoutedEventArgs e) {
    var proc = ProcessList.SelectedItem as ProcessSummary;

    if (proc != null) {
      var sb = new StringBuilder();
      sb.AppendLine($"Name: {proc.Process.Name}");
      sb.AppendLine($"Id: {proc.Process.ProcessId}");
      sb.AppendLine($"Command line: {proc.Process.CommandLine}");
      sb.AppendLine($"Duration: {proc.Duration}");
      sb.AppendLine($"Weight: {proc.Weight}");
      Clipboard.SetText(sb.ToString());
    }
  }

  private void RefreshProcessButton_OnClick(object sender, RoutedEventArgs e) {
    UpdateRunningProcessList();
  }

  private void UpdateRunningProcessList() {
    var runningProcs = Process.GetProcesses();
    var list = new List<ProcessSummary>();
    int currentProcId = Process.GetCurrentProcess().Id;

    foreach (var proc in runningProcs) {
      if (proc.Id == currentProcId) {
        continue; // Ignore self.
      }

      try {
        // Filter list down to .NET processes if requested.
        if (ShowOnlyManagedProcesses &&
            !IsManagedProcess(proc)) {
          continue;
        }

        var procProfile = new ProfileProcess(proc.Id, -1, proc.ProcessName, proc.ProcessName, "");
        list.Add(new ProcessSummary(procProfile, TimeSpan.Zero));

        // Retrieving values below may fail.
        procProfile.ImageFileName = proc.MainWindowTitle;
        procProfile.StartTime = proc.StartTime;
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to get proc title {proc.ProcessName}: {ex.Message}");
      }
    }

    list.Sort((a, b) => a.Process.Name.CompareTo(b.Process.Name));
    var procList = new ObservableCollectionRefresh<ProcessSummary>(list);
    processFilter_ = procList.GetFilterView();
    processFilter_.Filter = FilterRunningProcessList;
    RunningProcessList.ItemsSource = null;
    RunningProcessList.ItemsSource = processFilter_;
  }

  private bool IsManagedProcess(Process proc) {
    //? TODO: Detecting .NET procs is extremely slow done like below
    foreach (object mod in proc.Modules) {
      if (mod is ProcessModule module &&
          module.ModuleName.Contains("mscor", StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  private bool FilterRunningProcessList(object value) {
    string text = ProcessFilter.Text.Trim();

    if (text.Length < 2) {
      return true;
    }

    var proc = (ProcessSummary)value;

    if (proc.Process.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        proc.Process.ProcessId.ToString().Contains(text) ||
        proc.Process.ImageFileName != null &&
        proc.Process.ImageFileName.Contains(text, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    return false;
  }

  private void ProcessFilter_TextChanged(object sender, TextChangedEventArgs e) {
    processFilter_?.Refresh();
  }

  private void RunningProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (RunningProcessList.SelectedItems.Count > 0) {
      selectedProcSummary_ = new List<ProcessSummary>(RunningProcessList.SelectedItems.OfType<ProcessSummary>());
    }
    else {
      selectedProcSummary_ = null;
    }
  }

  private void AttachRadioButton_Click(object sender, RoutedEventArgs e) {
    UpdateRunningProcessList();
  }

  private async void RunningProcessList_DoubleClick(object sender, MouseButtonEventArgs e) {
    await StartRecordingSession();
  }

  private void SystemWideRadioButton_Click(object sender, RoutedEventArgs e) {
    RecordingOptions.ProfileDotNet = false;
    OnPropertyChange(nameof(RecordingOptions));
  }

  private void AddPrivateSymbolServer_OnClick(object sender, RoutedEventArgs e) {
    SymbolSettings.AddSymbolServer(usePrivateServer: true);
    ReloadSymbolPathsList();
  }

  private void AddPublicSymbolServer_OnClick(object sender, RoutedEventArgs e) {
    SymbolSettings.AddSymbolServer(usePrivateServer: false);
    ReloadSymbolPathsList();
  }

  private void AddSymbolPathButton_Click(object sender, RoutedEventArgs e) {
    symbolSettings_.SymbolPaths.Add("");
    ReloadSymbolPathsList();

    // Wait for the UI to update
    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle , () => {
      // Get the ListViewItem for the new item
      var newItem = symbolSettings_.SymbolPaths.Last();
      Utils.FocusTextBoxListViewItem(newItem, SymbolPathsList);
    });
  }

  private void RemoveSymbolPathButton_Click(object sender, RoutedEventArgs e) {
    foreach (var item in SymbolPathsList.SelectedItems) {
      string symbolPath = item as string;
      symbolSettings_.SymbolPaths.Remove(symbolPath);
    }

    ReloadSymbolPathsList();
  }

  private void MoveSymbolPathUpButton_Click(object sender, RoutedEventArgs e) {
    if (SymbolPathsList.SelectedItems.Count != 1) {
      return; // Only remove if there is exactly one item selected
    }

    if (SymbolPathsList.SelectedIndex == 0) {
      return; // Cannot move an item up if it is already at the top of the list
    }

    var selectedIndex = SymbolPathsList.SelectedIndex;
    var selectedItem = SymbolPathsList.SelectedItem as string;
    symbolSettings_.SymbolPaths.RemoveAt(selectedIndex);
    symbolSettings_.SymbolPaths.Insert(selectedIndex - 1, selectedItem);
    ReloadSymbolPathsList();
  }

  private void MoveSymbolPathDownButton_Click(object sender, RoutedEventArgs e) {
    if (SymbolPathsList.SelectedItems.Count != 1) {
      return; // Only remove if there is exactly one item selected
    }

    if (SymbolPathsList.SelectedIndex == SymbolPathsList.Items.Count - 1) {
      return; // Cannot move an item down if it is already at the bottom of the list
    }

    var selectedIndex = SymbolPathsList.SelectedIndex;
    var selectedItem = SymbolPathsList.SelectedItem as string;
    symbolSettings_.SymbolPaths.RemoveAt(selectedIndex);
    symbolSettings_.SymbolPaths.Insert(selectedIndex + 1, selectedItem);
    ReloadSymbolPathsList();
  }

  private void ReloadSymbolPathsList() {
    var list = new ObservableCollectionRefresh<string>(symbolSettings_.SymbolPaths);
    SymbolPathsList.ItemsSource = list;
  }

  private void SymbolPath_LostFocus(object sender, RoutedEventArgs e) {
    var textBox = sender as TextBox;
    UpdateSymbolPath(textBox);
  }

  private void SymbolPath_KeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Enter) {
      var textBox = sender as TextBox;
      UpdateSymbolPath(textBox);
    }
  }

  private void UpdateSymbolPath(TextBox textBox) {
    if (textBox == null) {
      return;
    }

    var item = textBox.DataContext;
    var index = symbolSettings_.SymbolPaths.IndexOf(item as string);

    if (index == -1) {
      return;
    }

    if (string.IsNullOrEmpty(textBox.Text)) {
      // Remove entry if there is no text in it
      symbolSettings_.SymbolPaths.RemoveAt(index);
      ReloadSymbolPathsList();

      return;
    }

    // Update list with the new text
    symbolSettings_.SymbolPaths[index] = textBox.Text;
    ReloadSymbolPathsList();
  }

  private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    if (sender is TextBox textBox) {
      Utils.SelectTextBoxListViewItem(textBox, SymbolPathsList);
      e.Handled = true;
    }
  }

  private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) {
      UseShellExecute = true
    });
  }

  private async void ProcessList_PreviewKeyDown(object sender, KeyEventArgs e) {
    await OpenFilesAndComplete(symbolSettings_);
  }

  private void ClearRejectedButton_Click(object sender, RoutedEventArgs e) {
    symbolSettings_.ClearRejectedFiles();
    OnPropertyChange(nameof(SymbolSettings));
    App.Settings.SymbolSettings = symbolSettings_;
    App.SaveApplicationSettings();
  }
}