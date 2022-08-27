// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using IRExplorerUI.Profile.ETW;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Win32;
using PEFile;

namespace IRExplorerUI {
    public class RecordingSessionEx : BindableObject {
        private ProfileDataReport report_;

        public RecordingSessionEx(ProfileDataReport report,
                                  bool isNewSession = false,
                                  string title = null, string description = null) {
            report_ = report;
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
            get => !string.IsNullOrEmpty(title_) ? title_ : Utils.TryGetFileName(report_.SessionOptions.ApplicationPath);
            set => SetAndNotify(ref title_, value);
        }

        private string description_;
        public string Description {
            get => !string.IsNullOrEmpty(description_) ? description_ : $"Args: {report_.SessionOptions.ApplicationArguments}";
            set => SetAndNotify(ref description_, value);
        }
    }

    public partial class ProfileLoadWindow : Window, INotifyPropertyChanged {
        private CancelableTaskInstance loadTask_;
        private bool isLoadingProfile_;
        private ProfileDataProviderOptions options_;
        private SymbolFileSourceOptions symbolOptions_;
        private RawProfileData recordedProfile_;
        private bool isLoadingProcessList_;
        private bool showProcessList_;
        private bool isRecordingProfile_;
        private List<ProcessSummary> processList_;
        private ProcessSummary selectedProcSummary_;
        private List<ProcessSummary> recoredProcSummaries_;
        private bool windowClosed_;

        public ProfileLoadWindow(ISession session, bool recordMode) {
            InitializeComponent();
            DataContext = this;
            Session = session;
            loadTask_ = new CancelableTaskInstance();
            IsRecordMode = recordMode;

            if (IsRecordMode) {
                Title = "Record profile trace";
            }

            Options = App.Settings.ProfileOptions;
            SymbolOptions = App.Settings.SymbolOptions;
            UpdatePerfCounterList();
            SetupSessionList();
            this.Closing += ProfileLoadWindow_Closing;
        }

        private void SetupSessionList() {
            var sessionList = new List<RecordingSessionEx>();
            sessionList.Add(new RecordingSessionEx(null, true,
                "Record", "Start a new session"));

            foreach (var prevSession in Options.PreviousRecordingSessions) {
                sessionList.Add(new RecordingSessionEx(prevSession));
            }

            SessionList.ItemsSource = new ListCollectionView(sessionList);
        }

        private void ProfileLoadWindow_Closing(object sender, CancelEventArgs e) {
            App.SaveApplicationSettings();
        }

        public ISession Session { get; set; }
        public string ProfileFilePath { get; set; }
        public string BinaryFilePath { get; set; }

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
        public bool RecordingControlsEnabled => !IsLoadingProfile && !IsRecordingProfile;
        public bool RecordingStopControlsEnabled => !IsLoadingProfile && IsRecordingProfile;
        public bool IsRecordMode { get; }

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
                options_.RecordingSessionOptions.AddPerformanceCounter(counter);
            }

            var metrics = ETWRecordingSession.BuiltinPerformanceMetrics;

            foreach (var metric in metrics) {
                options_.AddPerformanceMetric(metric);
            }
            
            var counterList = new ObservableCollectionRefresh<PerformanceCounterConfig>(options_.RecordingSessionOptions.PerformanceCounters);
            perfCountersFilter_ = counterList.GetFilterView();
            perfCountersFilter_.Filter = FilterCounterList;
            PerfCounterList.ItemsSource = perfCountersFilter_;

            var metricList = new ObservableCollectionRefresh<PerformanceMetricConfig>(options_.PerformanceMetrics);
            metricsFilter_ = metricList.GetFilterView();
            metricsFilter_.Filter = FilterMetricsList;
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

            var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
            var report = new ProfileDataReport();
            report.RunningProcesses = processList_;
            report.SymbolOptions = (SymbolFileSourceOptions)symbolOptions_.Clone();

            bool success = false;
            IsLoadingProfile = true;

            if (IsRecordMode) {
                if (selectedProcSummary_ == null) {
                    return false;
                }

                report.SessionOptions = (ProfileRecordingSessionOptions)options_.RecordingSessionOptions.Clone();
                var binSearchOptions = symbolOptions_.WithSymbolPaths(options_.RecordingSessionOptions.ApplicationPath);

                if (options_.RecordingSessionOptions.HasWorkingDirectory) {
                    binSearchOptions = binSearchOptions.WithSymbolPaths(options_.RecordingSessionOptions.WorkingDirectory);
                }

                success = await Session.LoadProfileData(recordedProfile_, selectedProcSummary_.Process,
                                                        options_, binSearchOptions, report,
                                                        ProfileLoadProgressCallback, task);
            }
            else {
                success = await Session.LoadProfileData(ProfileFilePath, BinaryFilePath,
                                                        options_, symbolOptions_, report,
                                                        ProfileLoadProgressCallback, task);
            }

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

        private async void CancelButton_Click(object sender, RoutedEventArgs e) {
            if (isLoadingProfile_) {
                loadTask_.CancelTask();
                await loadTask_.WaitForTaskAsync();
            }

            App.SaveApplicationSettings();
            DialogResult = false;
            windowClosed_ = true;
            Close();
        }

        private void ProfileBrowseButton_Click(object sender, RoutedEventArgs e) {
            Utils.ShowOpenFileDialog(ProfileAutocompleteBox, "ETW Trace Files|*.etl|All Files|*.*");
        }

        private async Task<bool> LoadProcessList() {
            ProfileFilePath = Utils.CleanupPath(ProfileFilePath);

            if (File.Exists(ProfileFilePath)) {
                IsLoadingProcessList = true;
                var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
                processList_ = await ETWProfileDataProvider.FindTraceImages(ProfileFilePath, options_, task);
                IsLoadingProcessList = false;
                return processList_ != null;
            }

            return false;
        }

        private void ApplicationBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowExecutableOpenFileDialog(ApplicationAutocompleteBox);

        private void BinaryBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowExecutableOpenFileDialog(BinaryAutocompleteBox);
        
        private async void RecentMenuItem_Click(object sender, RoutedEventArgs e) {
            var menuItem = sender as MenuItem;

            if (menuItem.Tag == null) {
                
            }
            else {
                var pathPair = menuItem.Tag as Tuple<string, string, string>;
                ProfileAutocompleteBox.Text = pathPair.Item1;
                BinaryAutocompleteBox.Text = pathPair.Item2;
                await LoadProcessList();
                await OpenFilesAndComplete();
            }
        }

        private void ProcessList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ProcessList.SelectedItem != null) {
                selectedProcSummary_ = (ProcessSummary)ProcessList.SelectedItem;
                BinaryAutocompleteBox.Text = selectedProcSummary_.Process.Name;
            }
        }

        private string SetSessionApplicationPath() {
            options_.RecordingSessionOptions.WorkingDirectory = Utils.CleanupPath(options_.RecordingSessionOptions.WorkingDirectory);
            var appPath = Utils.CleanupPath(options_.RecordingSessionOptions.ApplicationPath);

            if (!File.Exists(appPath) && options_.RecordingSessionOptions.HasWorkingDirectory) {
                appPath = Path.Combine(options_.RecordingSessionOptions.WorkingDirectory, appPath);
            }

            options_.RecordingSessionOptions.ApplicationPath = appPath;
            return appPath;
        }

        private async void StartCaptureButton_OnClick(object sender, RoutedEventArgs e) {
            var appPath = SetSessionApplicationPath();

            if (!File.Exists(appPath)) {
                MessageBox.Show($"Could not find profiled application: {appPath}", "IR Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (options_.RecordingSessionOptions.RecordPerformanceCounters) {
                options_.IncludePerformanceCounters = true;
            }

            using var recordingSession = new ETWRecordingSession(options_);
            
            IsRecordingProfile = true;
            var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
            ProfileLoadProgress lastProgressInfo = null;

            var stopWatch = Stopwatch.StartNew();
            DispatcherTimer timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Render,
                (o, e)=>  UpdateRecordProgress(stopWatch, lastProgressInfo), Dispatcher);
            timer.Start();
            
            recordedProfile_ = null;

            var recordingTask = recordingSession.StartRecording(progressInfo => {
                lastProgressInfo = progressInfo;
            }, task);

            if (recordingTask != null) {
                recordedProfile_ = await recordingTask;
                //StateSerializer.Serialize(@"C:\test\profile.dat", recordedProfile_);
            }

            timer.Stop();
            IsRecordingProfile = false;

            if (recordedProfile_ != null) {
                processList_ = await Task.Run(() => recordedProfile_.BuildProcessSummary());
                DisplayProcessList();
            }
            else {
                MessageBox.Show("Failed to record ETW sampling profile!", "IR Explorer", 
                                MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        private void UpdateRecordProgress(Stopwatch stopWatch, ProfileLoadProgress progressInfo) {
            string status = $"{stopWatch.Elapsed.ToString(@"mm\:ss")}";

            if (progressInfo != null) {
                status += progressInfo.Stage switch {
                    ProfileLoadStage.TraceLoading => $", {progressInfo.Total / 1000}K samples",
                    _ => ""
                };
            }

            RecordProgressLabel.Text = status;
        }

        private void DisplayProcessList() {
            ShowProcessList = false;
            
            if (processList_ != null) {
                processList_.Sort((a, b) => b.SampleCount.CompareTo(a.SampleCount));
                ProcessList.ItemsSource = new ListCollectionView(processList_);
            }
            else {
                MessageBox.Show("Failed to load ETL process list!", "IR Explorer",
                                MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }

            ShowProcessList = true;
        }

        private void StopCaptureButton_OnClick(object sender, RoutedEventArgs e) {
            loadTask_.CancelTask();
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
            if (!string.IsNullOrEmpty(ProfileFilePath)) {
                if (await LoadProcessList()) {
                    DisplayProcessList();
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
            EnabledPerfCounters = options_.RecordingSessionOptions.EnabledPerformanceCounters.Count;
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e) {
            EnabledPerfCounters = options_.RecordingSessionOptions.EnabledPerformanceCounters.Count;
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
            }
        }
    }
}
