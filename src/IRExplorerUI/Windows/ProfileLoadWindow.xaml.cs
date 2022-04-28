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
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using IRExplorerUI.Profile.ETW;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Win32;

namespace IRExplorerUI {
    public partial class ProfileLoadWindow : Window, INotifyPropertyChanged {
        private CancelableTaskInstance loadTask_;
        private bool isLoadingProfile_;
        private ProfileDataProviderOptions options_;
        private SymbolFileSourceOptions symbolOptions_;
        private RawProfileData recordedProfile_;
        private int samplingFrequency_;
        private bool isLoadingProcessList_;
        private bool showProcessList_;
        private bool isRecordingProfile_;
        private List<ETWProfileDataProvider.TraceProcessSummary> processList_;
        private ETWProfileDataProvider.TraceProcessSummary selectedProcSummary_;
        private bool windowClosed_;

        public ProfileLoadWindow(ISession session, bool recordMode) {
            InitializeComponent();
            DataContext = this;
            Session = session;
            loadTask_ = new CancelableTaskInstance();
            IsRecordMode = recordMode;
            SamplingFrequency = 1000; //? TODO: Option

            Options = App.Settings.ProfileOptions;
            SymbolOptions = App.Settings.SymbolOptions;
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

        public bool InputControlsEnabled => !IsLoadingProfile;
        public bool RecordingControlsEnabled => !IsLoadingProfile && !IsRecordingProfile;
        public bool RecordingStopControlsEnabled => !IsLoadingProfile && IsRecordingProfile;
        public bool IsRecordMode { get; }

        public int SamplingFrequency {
            get => samplingFrequency_;
            set {
                if (samplingFrequency_ != value) {
                    samplingFrequency_ = value;
                    OnPropertyChange(nameof(SamplingFrequency));
                }
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

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e) {
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

            IsLoadingProfile = true;
            var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
            bool success = false;

            if (IsRecordMode) {
                success = await Session.LoadProfileData(recordedProfile_, BinaryFilePath,
                                                        options_, symbolOptions_, ProfileLoadProgressCallback, task);
            }
            else {
                success = await Session.LoadProfileData(ProfileFilePath, BinaryFilePath,
                                                       options_, symbolOptions_, ProfileLoadProgressCallback, task);
            }
            IsLoadingProfile = false;

            if (!success && !task.IsCanceled) {
                MessageBox.Show($"Failed to load profile file {ProfileFilePath}", "IR Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }

            if (success && !IsRecordMode) {
                App.Settings.AddRecentProfileFiles(ProfileFilePath, BinaryFilePath, "");
                App.SaveApplicationSettings();
            }
            
            return success;
        }

        private void ProfileLoadProgressCallback(ProfileLoadProgress progressInfo) {
            Dispatcher.BeginInvoke((Action)(() => {
                LoadProgressBar.Maximum = progressInfo.Total;
                LoadProgressBar.Value = progressInfo.Current;

                LoadProgressLabel.Text = progressInfo.Stage switch {
                    ProfileLoadStage.TraceLoading => "Loading trace",
                    ProfileLoadStage.TraceProcessing => "Processing trace",
                    ProfileLoadStage.SymbolLoading => "Loading symbols",
                    ProfileLoadStage.PerfCounterProcessing => "Processing perf. counters"
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

        private async void ProfileBrowseButton_Click(object sender, RoutedEventArgs e) {
            if (!Utils.ShowOpenFileDialog(ProfileAutocompleteBox, "ETW Trace Files|*.etl|All Files|*.*")) {
                return;
            }

            if (File.Exists(ProfileFilePath)) {
                var task = await loadTask_.CancelPreviousAndCreateTaskAsync();
                await DisplayProcessList(async () => await ETWProfileDataProvider.FindTraceImages(ProfileFilePath, task));
            }
        }

        private void BinaryBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(BinaryAutocompleteBox, Session.CompilerInfo.OpenFileFilter);

        private void RecentButton_Click(object sender, RoutedEventArgs e) {
            var menu = RecentButton.ContextMenu;
            menu.Items.Clear();

            foreach (var pathPair in App.Settings.RecentProfileFiles) {
                var item = new MenuItem();
                item.Header = $"Trace: {pathPair.Item1}\nBinary: {pathPair.Item2}";
                item.Tag = pathPair;
                item.Click += RecentMenuItem_Click;
                menu.Items.Add(item);
                menu.Items.Add(new Separator());
            }

            var clearMenuItem = new MenuItem { Header = "Clear" };

            clearMenuItem.Click += RecentMenuItem_Click;
            menu.Items.Add(clearMenuItem);
            menu.IsOpen = true;
        }

        private async void RecentMenuItem_Click(object sender, RoutedEventArgs e) {
            RecentButton.ContextMenu.IsOpen = false;
            var menuItem = sender as MenuItem;

            if (menuItem.Tag == null) {
                App.Settings.ClearRecentProfileFiles();
            }
            else {
                var pathPair = menuItem.Tag as Tuple<string, string, string>;
                ProfileAutocompleteBox.Text = pathPair.Item1;
                BinaryAutocompleteBox.Text = pathPair.Item2;
                SymbolAutocompleteBox.Text = pathPair.Item3; //? TODO: Unused
                await OpenFiles();
            }
        }

        private void ProcessList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ProcessList.SelectedItem != null) {
                selectedProcSummary_ = (ETWProfileDataProvider.TraceProcessSummary)ProcessList.SelectedItem;
                BinaryAutocompleteBox.Text = selectedProcSummary_.ProcessName;
            }
        }

        private async void StartCaptureButton_OnClick(object sender, RoutedEventArgs e) {
            using var recordingSession = new ETWRecordingSession();
            recordingSession.SamplingFrequency = samplingFrequency_;
            
            IsRecordingProfile = true;
            var task = await loadTask_.CancelPreviousAndCreateTaskAsync();

            recordedProfile_ = null;
            recordedProfile_ = await recordingSession.StartRecording(progressInfo => {
                Dispatcher.BeginInvoke((Action)(() => {
                    RecordProgressLabel.Text = progressInfo.Stage switch {
                        ProfileLoadStage.TraceLoading => $"Recording, {progressInfo.Total / 1000}K samples",
                    };
                }));
            }, task);

            IsRecordingProfile = false;

            if (recordedProfile_ != null) {
                await DisplayProcessList(async () => await Task.Run(() => recordedProfile_.BuildProcessSummary()));
            }
            else {
                MessageBox.Show("Failed to record ETW sampling profile!", "IR Explorer", 
                                MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        private async Task DisplayProcessList(Func<Task<List<ETWProfileDataProvider.TraceProcessSummary>>> func) {
            IsLoadingProcessList = true;
            ShowProcessList = false;
            processList_ = await func();
            
            if (processList_ != null) {
                processList_.Sort((a, b) => b.SampleCount.CompareTo(a.SampleCount));
                ProcessList.ItemsSource = new ListCollectionView(processList_);
            }

            IsLoadingProcessList = false;
            ShowProcessList = true;
        }

        private void StopCaptureButton_OnClick(object sender, RoutedEventArgs e) {
            loadTask_.CancelTask();
        }

        private void RestartAppButton_OnClick(object sender, RoutedEventArgs e) {
            App.RestartApplicationAsAdmin();
        }
    }
}
