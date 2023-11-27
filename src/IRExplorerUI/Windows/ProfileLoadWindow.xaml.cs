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
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;

namespace IRExplorerUI;

public class RecordingSessionEx : BindableObject {
  private static readonly SortedList<double, Func<TimeSpan, string>> offsets =
    new SortedList<double, Func<TimeSpan, string>> {
      {0.75, x => $"{x.TotalSeconds:F0} seconds"},
      {1.5, x => "a minute"},
      {45, x => $"{x.TotalMinutes:F0} minutes"},
      {90, x => "an hour"},
      {1440, x => $"{x.TotalHours:F0} hours"},
      {2880, x => "a day"},
      {43200, x => $"{x.TotalDays:F0} days"},
      {86400, x => "a month"},
      {525600, x => $"{x.TotalDays / 30:F0} months"},
      {1051200, x => "a year"},
      {double.MaxValue, x => $"{x.TotalDays / 365:F0} years"}
    };
  private ProfileDataReport report_;
  private bool isLoadedFile_;
  private bool isNewSession_;
  private string title_;
  private string description_;

  public RecordingSessionEx(ProfileDataReport report, bool isLoadedFile,
                            bool isNewSession = false,
                            string title = null, string description = null) {
    report_ = report;
    isLoadedFile_ = isLoadedFile;
    isNewSession_ = isNewSession;
    title_ = title;
    description_ = description;
  }

  public ProfileDataReport Report => report_;

  public bool IsNewSession {
    get => isNewSession_;
    set => SetAndNotify(ref isNewSession_, value);
  }

  public string Title {
    get {
      if (!string.IsNullOrEmpty(title_)) {
        return title_;
      }

      if (isLoadedFile_) {
        return report_.TraceInfo.TraceFilePath;
      }

      if (report_.SessionOptions.HasTitle) {
        return report_.SessionOptions.Title;
      }

      if (report_.IsAttachToProcessSession) {
        return $"Attached to {report_.Process.Name}";
      }

      if (Report.IsStartProcessSession) {
        return Utils.TryGetFileName(report_.SessionOptions.ApplicationPath);
      }

      return null;
    }
    set => SetAndNotify(ref title_, value);
  }

  public string ToolTip {
    get {
      if (report_ == null) {
        return null;
      }

      if (isLoadedFile_) {
        return report_?.TraceInfo.TraceFilePath;
      }

      if (report_.IsStartProcessSession) {
        return $"{report_?.SessionOptions.ApplicationPath} {report_?.SessionOptions.ApplicationArguments}";
      }

      return null;
    }
  }

  public string Description {
    get {
      if (!string.IsNullOrEmpty(description_)) {
        return description_;
      }

      if (!IsNewSession) {
        if (isLoadedFile_) {
          return $"Process: {report_?.Process.ImageFileName}";
        }

        if (report_.IsAttachToProcessSession) {
          return $"Id: {report_.Process.ProcessId}";
        }

        if (report_.IsStartProcessSession) {
          return $"Args: {report_.SessionOptions.ApplicationArguments}";
        }
      }

      return null;
    }
    set => SetAndNotify(ref description_, value);
  }

  public bool ShowDescription {
    get {
      if (!string.IsNullOrEmpty(description_)) {
        return true;
      }

      if (!IsNewSession) {
        return !string.IsNullOrEmpty(isLoadedFile_ ? report_?.Process.ImageFileName
                                       : report_.SessionOptions.ApplicationArguments);
      }

      return false;
    }
  }

  public string Time => IsNewSession ? ""
    : $"{report_.TraceInfo.ProfileStartTime.ToShortDateString()}, {ToRelativeDate(report_.TraceInfo.ProfileStartTime)}";

  public static string ToRelativeDate(DateTime input) {
    var x = DateTime.Now - input;
    x = new TimeSpan(Math.Abs(x.Ticks));
    return offsets.First(n => x.TotalMinutes < n.Key).Value(x) + " ago";
  }
}

public partial class ProfileLoadWindow : Window, INotifyPropertyChanged {
  private CancelableTaskInstance loadTask_;
  private bool isLoadingProfile_;
  private ProfileDataProviderOptions options_;
  private ProfileRecordingSessionOptions recordingOptions_;
  private SymbolFileSourceOptions symbolOptions_;
  private RawProfileData recordedProfile_;
  private bool isLoadingProcessList_;
  private bool showProcessList_;
  private bool isRecordingProfile_;
  private List<ProcessSummary> processList_;
  private List<ProcessSummary> selectedProcSummary_;
  private List<ProcessSummary> recoredProcSummaries_;
  private bool windowClosed_;
  private RecordingSessionEx currentSession_;
  private int enabledPerfCounters_;
  private ICollectionView perfCountersFilter_;
  private ICollectionView metricsFilter_;
  private ICollectionView processFilter_;
  private string profileFilePath_;
  private string binaryFilePath_;

        public ProfileLoadWindow(ISession session, bool recordMode, bool isOnLaunch = false) {
            InitializeComponent();
            DataContext = this;
            Session = session;
            loadTask_ = new CancelableTaskInstance(false);
            IsRecordMode = recordMode;
            IsOnLaunch = isOnLaunch;

    if (IsRecordMode) {
      Title = "Record profile trace";
    }

    Options = App.Settings.ProfileOptions;
    SymbolOptions = App.Settings.SymbolOptions;
    RecordingOptions = new ProfileRecordingSessionOptions();

    UpdatePerfCounterList();
    SetupSessionList();
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
            get {
                return recordingOptions_;
            }
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

  public SymbolFileSourceOptions SymbolOptions {
    get => symbolOptions_;
    set {
      symbolOptions_ = value;
      OnPropertyChange(nameof(SymbolOptions));
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
    var sessionList = new List<RecordingSessionEx>();

    if (IsRecordMode) {
      sessionList.Add(new RecordingSessionEx(null, false, true,
                                             "Record", "Start a new session"));

      foreach (var prevSession in Options.PreviousRecordingSessions) {
        sessionList.Add(new RecordingSessionEx(prevSession, false));
      }
    }
    else {
      sessionList.Add(new RecordingSessionEx(null, false, true,
                                             "Open", "Load a trace"));

      foreach (var prevSession in Options.PreviousLoadedSessions) {
        sessionList.Add(new RecordingSessionEx(prevSession, true));
      }
    }

    currentSession_ = sessionList[0];
    SessionList.ItemsSource = null; // Force update.
    SessionList.ItemsSource = new ListCollectionView(sessionList);
  }

  private void ProfileLoadWindow_Closing(object sender, CancelEventArgs e) {
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
    await OpenFilesAndComplete();
  }

  private async Task OpenFilesAndComplete() {
    if (await OpenFiles() && !windowClosed_) {
      DialogResult = true;
      Close();
    }
  }

  private async Task<bool> OpenFiles() {
    ProfileFilePath = Utils.CleanupPath(ProfileFilePath);
    BinaryFilePath = Utils.CleanupPath(BinaryFilePath);

    if (!IsRecordMode &&
        !Utils.ValidateFilePath(ProfileFilePath, ProfileAutocompleteBox, "profile", this)) {
      return false;
    }

    using var task = await loadTask_.CancelPreviousAndCreateTaskAsync();

    var report = new ProfileDataReport();
    report.RunningProcesses = processList_;
    report.SymbolOptions = symbolOptions_.Clone();

    bool success = false;
    IsLoadingProfile = true;
    LoadProgressBar.Value = 0;

    if (selectedProcSummary_ == null) {
      IsLoadingProfile = false;
      return false;
    }

    var processIds = selectedProcSummary_.ConvertAll(proc => proc.Process.ProcessId);

    if (IsRecordMode) {
      report.SessionOptions = recordingOptions_.Clone();
      var binSearchOptions = symbolOptions_.WithSymbolPaths(recordingOptions_.ApplicationPath);

      if (recordingOptions_.HasWorkingDirectory) {
        binSearchOptions = binSearchOptions.WithSymbolPaths(recordingOptions_.WorkingDirectory);
      }

      success = await Session.LoadProfileData(recordedProfile_, processIds,
                                              options_, binSearchOptions, report,
                                              ProfileLoadProgressCallback, task);
    }
    else {
      success = await Session.LoadProfileData(ProfileFilePath, processIds,
                                              options_, symbolOptions_, report,
                                              ProfileLoadProgressCallback, task);
    }

    if (report.TraceInfo == null) {
      report.TraceInfo = new ProfileTraceInfo(); // Not set on failure.
      report.TraceInfo.TraceFilePath = ProfileFilePath;
    }

    IsLoadingProfile = false;

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
      }

      App.SaveApplicationSettings();
    }

    return success;
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
        ProfileLoadStage.TraceLoading => "Loading trace",
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
    Dispatcher.Invoke((Action)(() => {
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
    }));
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
        ProfileLoadStage.TraceLoading => $", {progressInfo.Total / 1000}K samples",
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
    var sessionEx = ((Button)sender).DataContext as RecordingSessionEx;
    var report = sessionEx?.Report;

    if (report != null) {
      ProfileReportPanel.ShowReportWindow(report, Session);
    }
  }

  private void SessionRemoveButton_Click(object sender, RoutedEventArgs e) {
    var sessionEx = ((Button)sender).DataContext as RecordingSessionEx;
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
            symbolOptions_.InsertSymbolPath(symbolPath);

            ProfileProcess process = new ProfileProcess(processId, imageFileName);
            selectedProcSummary_ = new List<ProcessSummary>() {
                new ProcessSummary(process, TimeSpan.Zero)
            };

            await OpenFilesAndComplete();
        }

        private async void SessionList_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            var sessionEx = SessionList.SelectedItem as RecordingSessionEx;
            if (sessionEx?.Report == null) {
                return;
            }

    LoadPreviousSession(sessionEx.Report);

    if (IsRecordMode) {
      await StartRecordingSession();
    }
    else {
      await OpenFilesAndComplete();
    }
  }

  private void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    var sessionEx = SessionList.SelectedItem as RecordingSessionEx;

    if (sessionEx == null) {
      return;
    }

    var report = sessionEx.Report;

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
    currentSession_ = sessionEx;
  }

  private void LoadPreviousSession(ProfileDataReport report) {
    if (report.Process != null) {
      // Set previous selected process.
      selectedProcSummary_ = new List<ProcessSummary> {
        new ProcessSummary(report.Process, TimeSpan.Zero)
      };
    }

    if (IsRecordMode) {
      RecordingOptions = report.SessionOptions.Clone();
    }
    else {
      ProfileFilePath = report.TraceInfo.TraceFilePath;
      BinaryFilePath = report.Process.ImageFileName;
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

      var procProfile = new ProfileProcess(proc.Id, -1, proc.ProcessName, proc.ProcessName, "");
      list.Add(new ProcessSummary(procProfile, TimeSpan.Zero));

      try {
        procProfile.ImageFileName = proc.MainWindowTitle;

        //? TODO: Detecting .NET procs is extremely slow done like below
        // bool isnet = false;
        //foreach (var mod in proc.Modules) {
        //    if (mod is ProcessModule module &&
        //        module.ModuleName.Contains("mscor", StringComparison.OrdinalIgnoreCase)) {
        //        isnet = true;
        //        break;
        //    }
        //}
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
}
