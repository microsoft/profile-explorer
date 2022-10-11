// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DocumentFormat.OpenXml.Office2010.PowerPoint;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using IRExplorerUI.Profile.ETW;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Win32;
using PEFile;

namespace IRExplorerUI {
    public class RecordingSessionEx : BindableObject {
        private ProfileDataReport report_;
        private bool isLoadedFile_;

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

        private bool isNewSession_;

        public bool IsNewSession {
            get => isNewSession_;
            set => SetAndNotify(ref isNewSession_, value);
        }

        private string title_;

        public string Title {
            get {
                if (!string.IsNullOrEmpty(title_)) {
                    return title_;
                }
                else if (isLoadedFile_) {
                    return report_.TraceInfo.TraceFilePath;
                }
                else if (report_.SessionOptions.HasTitle) {
                    return report_.SessionOptions.Title;
                }

                return Utils.TryGetFileName(report_.SessionOptions.ApplicationPath);
            }
            set => SetAndNotify(ref title_, value);
        }

        public string ToolTip {
            get => isLoadedFile_ ? report_?.TraceInfo.TraceFilePath : $"{report_?.SessionOptions.ApplicationPath} {report_?.SessionOptions.ApplicationArguments}";
        }

        private string description_;

        public string Description {
            get {
                if (!string.IsNullOrEmpty(description_)) {
                    return description_;
                }
                else if (!IsNewSession) {
                    return isLoadedFile_ ? $"Process: {report_?.Process.ImageFileName}" : $"Args: {report_.SessionOptions.ApplicationArguments}";
                }

                return "";
            }
            set => SetAndNotify(ref description_, value);
        }

        public bool ShowDescription {
            get {
                if (!string.IsNullOrEmpty(description_)) {
                    return true;
                }
                else if (!IsNewSession) {
                    return !string.IsNullOrEmpty(isLoadedFile_ ? report_?.Process.ImageFileName : report_.SessionOptions.ApplicationArguments);
                }

                return false;
            }
        }

        public string Time {
            get {
                return IsNewSession ? "" : $"{report_.TraceInfo.ProfileStartTime.ToShortDateString()}, {ToRelativeDate(report_.TraceInfo.ProfileStartTime)}";
            }
        }

        static readonly SortedList<double, Func<TimeSpan, string>> offsets =
            new SortedList<double, Func<TimeSpan, string>>
            {
                { 0.75, x => $"{x.TotalSeconds:F0} seconds"},
                { 1.5, x => "a minute"},
                { 45, x => $"{x.TotalMinutes:F0} minutes"},
                { 90, x => "an hour"},
                { 1440, x => $"{x.TotalHours:F0} hours"},
                { 2880, x => "a day"},
                { 43200, x => $"{x.TotalDays:F0} days"},
                { 86400, x => "a month"},
                { 525600, x => $"{x.TotalDays / 30:F0} months"},
                { 1051200, x => "a year"},
                { double.MaxValue, x => $"{x.TotalDays / 365:F0} years"}
            };

        public static string ToRelativeDate(DateTime input) {
            TimeSpan x = DateTime.Now - input;
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
        private ProcessSummary selectedProcSummary_;
        private List<ProcessSummary> recoredProcSummaries_;
        private bool windowClosed_;
        private RecordingSessionEx currentSession_;

        public ProfileLoadWindow(ISession session, bool recordMode) {
            InitializeComponent();
            DataContext = this;
            Session = session;
            loadTask_ = new CancelableTaskInstance(false);
            IsRecordMode = recordMode;

            if (IsRecordMode) {
                Title = "Record profile trace";
            }

            Options = App.Settings.ProfileOptions;
            SymbolOptions = App.Settings.SymbolOptions;
            RecordingOptions = new ProfileRecordingSessionOptions();

            UpdatePerfCounterList();
            SetupSessionList();
            this.Closing += ProfileLoadWindow_Closing;
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

        private int enabledPerfCounters_;
        private ICollectionView perfCountersFilter_;
        private ICollectionView metricsFilter_;
        private string profileFilePath_;
        private string binaryFilePath_;
        private bool ignoreProfilePathChange_;

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

        public ProfileRecordingSessionOptions RecordingOptions {
            get {
                return recordingOptions_;
            }
            set {
                recordingOptions_ = value;
                OnPropertyChange(nameof(RecordingOptions));
            }
        }

        public ProfileDataProviderOptions Options {
            get {
                return options_;
            }
            set {
                options_ = value;
                OnPropertyChange(nameof(Options));
            }
        }

        public SymbolFileSourceOptions SymbolOptions {
            get {
                return symbolOptions_;
            }
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

        private bool FilterCounterList(object value) {
            var text = CounterFilter.Text.Trim();

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
            var text = MetricFilter.Text.Trim();

            if (text.Length < 2) {
                return true;
            }

            var counter = (PerformanceMetricConfig)value;

            if (counter.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            return false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
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
                return false;
            }
            
            if (IsRecordMode) {
               

                report.SessionOptions = recordingOptions_.Clone();
                var binSearchOptions = symbolOptions_.WithSymbolPaths(recordingOptions_.ApplicationPath);

                if (recordingOptions_.HasWorkingDirectory) {
                    binSearchOptions = binSearchOptions.WithSymbolPaths(recordingOptions_.WorkingDirectory);
                }

                success = await Session.LoadProfileData(recordedProfile_, selectedProcSummary_.Process,
                                                        options_, binSearchOptions, report,
                                                        ProfileLoadProgressCallback, task);
            }
            else {
                success = await Session.LoadProfileData(ProfileFilePath, selectedProcSummary_.Process,
                                                        options_, symbolOptions_, report,
                                                        ProfileLoadProgressCallback, task);
            }

            report.TraceInfo ??= new ProfileTraceInfo(); // Not set on failure.
            report.TraceInfo.TraceFilePath = ProfileFilePath;
            IsLoadingProfile = false;

            if (!success && !task.IsCanceled) {
                MessageBox.Show($"Failed to load profile file {ProfileFilePath}", "IR Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                ProfileReportPanel.ShowReport(report, Session);
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
            Dispatcher.BeginInvoke((Action)(() => {
                if (progressInfo == null) {
                    return;
                }

                LoadProgressBar.Maximum = progressInfo.Total;
                LoadProgressBar.Value = progressInfo.Current;
                LoadProgressLabel.Text = progressInfo.Stage switch {
                    ProfileLoadStage.TraceLoading => "Loading trace",
                    ProfileLoadStage.TraceProcessing => "Processing trace",
                    ProfileLoadStage.BinaryLoading => "Loading binaries",
                    ProfileLoadStage.SymbolLoading => "Loading symbols",
                    ProfileLoadStage.PerfCounterProcessing => "Processing CPU perf. counters",
                    _ => ""
                };

                if (progressInfo.Total != 0 && progressInfo.Total != progressInfo.Current) {
                    double percentage = (double)progressInfo.Current / (double)progressInfo.Total;
                    ProgressPercentLabel.Text = $"{Math.Round(percentage * 100)} %";
                    LoadProgressBar.IsIndeterminate = false;
                }
                else {
                    LoadProgressBar.IsIndeterminate = true;
                }
            }));
        }

        private void ProcessListProgressCallback(ProcessListProgress progressInfo) {
            Dispatcher.BeginInvoke((Action)(() => {
                if (progressInfo == null) {
                    return;
                }

                LoadProgressBar.Maximum = progressInfo.Total;
                LoadProgressBar.Value = progressInfo.Current;

                if (progressInfo.Total != 0 && progressInfo.Total != progressInfo.Current) {
                    double percentage = (double)progressInfo.Current / (double)progressInfo.Total;
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
            loadTask_.CancelTask();
            await loadTask_.WaitForTaskAsync();
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

                processList_ = await ETWProfileDataProvider.FindTraceProcesses(ProfileFilePath, options_, ProcessListProgressCallback, task);
                IsLoadingProfile = false;
                IsLoadingProcessList = false;

                if (task.IsCanceled) {
                    return true;
                }

                return processList_ != null;
            }

            return false;
        }

        private void ResetProcessList() {
            processList_ = null;
            selectedProcSummary_ = null;
        }

        private void ApplicationBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowExecutableOpenFileDialog(ApplicationAutocompleteBox);

        private void ProcessList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ProcessList.SelectedItem != null) {
                selectedProcSummary_ = (ProcessSummary)ProcessList.SelectedItem;
                BinaryFilePath = selectedProcSummary_.Process.Name;
            }
        }

        private string SetSessionApplicationPath() {
            recordingOptions_.WorkingDirectory = Utils.CleanupPath(recordingOptions_.WorkingDirectory);
            var appPath = Utils.CleanupPath(recordingOptions_.ApplicationPath);

            if (!File.Exists(appPath) && recordingOptions_.HasWorkingDirectory) {
                appPath = Path.Combine(recordingOptions_.WorkingDirectory, appPath);
            }

            recordingOptions_.ApplicationPath = appPath;
            SaveCurrentOptions();
            return appPath;
        }

        private async void StartCaptureButton_OnClick(object sender, RoutedEventArgs e) {
            await StartRecordingSession();
        }

        private async Task StartRecordingSession() {
            var appPath = SetSessionApplicationPath();

            if (options_.RecordingSessionOptions.SessionKind ==   ProfileSessionKind.StartProcess &&
                !File.Exists(appPath)) {
                MessageBox.Show($"Could not find profiled application: {appPath}", "IR Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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
            DispatcherTimer timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Render,
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

            if (recordedProfile_ != null && !task.IsCanceled) {
                processList_ = await Task.Run(() => recordedProfile_.BuildProcessSummary());
                DisplayProcessList(processList_);
            }
            else {
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

        private void DisplayProcessList(List<ProcessSummary> list, ProcessSummary selectedProcSummary = null) {
            list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            ProcessList.ItemsSource = null;
            ProcessList.ItemsSource = new ListCollectionView(list);
            ShowProcessList = true;

            if (selectedProcSummary != null) {
                // Keep selected process after updating list.
                ProcessList.SelectedItem = list.Find(item => item.Process == selectedProcSummary.Process);
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
                //if (ignoreProfilePathChange_) {
                //    ignoreProfilePathChange_ = false;
                //    return;
                //}

                if (await LoadProcessList()) {
                    if (processList_ == null) {
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
            perfCountersFilter_.Refresh();
        }

        private void MetricFilter_TextChanged(object sender, TextChangedEventArgs e) {
            metricsFilter_.Refresh();
        }

        private void SessionReportButton_Click(object sender, RoutedEventArgs e) {
            var sessionEx = ((Button)sender).DataContext as RecordingSessionEx;
            var report = sessionEx?.Report;

            if (report != null) {
                ProfileReportPanel.ShowReport(report, Session);
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
            if (IsRecordMode) {
                RecordingOptions = report.SessionOptions.Clone();
            }
            else {
                ProfileFilePath = report.TraceInfo.TraceFilePath;
                BinaryFilePath = report.Process.ImageFileName;
            }
        }
    }
}