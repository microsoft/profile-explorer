// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IRExplorerCore;
using IRExplorerUI.Profile;
using Microsoft.Win32;

namespace IRExplorerUI {
    public partial class ProfileLoadWindow : Window, INotifyPropertyChanged {
        private CancelableTaskInstance loadTask_;
        private bool isLoadingProfile_;
        private ProfileDataProviderOptions options_;

        public ProfileLoadWindow(ISession session) {
            InitializeComponent();
            DataContext = this;
            Session = session;
            loadTask_ = new CancelableTaskInstance();

            Options = App.Settings.ProfileOptions;
            var loadedDoc = Session.SessionState.FindLoadedDocument(Session.MainDocumentSummary);

            if (loadedDoc.BinaryFileExists) {
                BinaryFilePath = loadedDoc.BinaryFilePath;

                if (loadedDoc.DebugInfoFileExists) {
                    var debugPath = Utils.TryGetDirectoryName(loadedDoc.DebugInfoFilePath);

                    if (!options_.Symbols.HasSymbolPath(debugPath)) {
                        Options.Symbols.SymbolSearchPaths.Insert(0, debugPath);
                    }

                }
            }
        }

        public ISession Session { get; set; }
        public string ProfileFilePath { get; set; }
        public string BinaryFilePath { get; set; }

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

        public bool InputControlsEnabled => !isLoadingProfile_;

        public ProfileDataProviderOptions Options {
            get {
                return options_;
            }
            set {
                options_ = value;
                OnPropertyChange(nameof(Options));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e) {
            await OpenFiles();
        }

        private async Task OpenFiles() {
            ProfileFilePath = Utils.CleanupPath(ProfileFilePath);
            BinaryFilePath = Utils.CleanupPath(BinaryFilePath);

            if (Utils.ValidateFilePath(ProfileFilePath, ProfileAutocompleteBox, "profile", this)
                //Utils.ValidateFilePath(SymbolPath, SymbolAutocompleteBox, "debug", this)
                ) {
                //? TODO: Save the entire state offor the Recent menu - could serialize the Options 
                //? and save it as a string
                App.Settings.AddRecentProfileFiles(ProfileFilePath, BinaryFilePath, "");
                App.SaveApplicationSettings();

                //? TODO: Disable buttons
                var task = loadTask_.CreateTask();
                IsLoadingProfile = true;


                if (await Session.LoadProfileData(ProfileFilePath, BinaryFilePath, options_, progressInfo => {
                    Dispatcher.BeginInvoke((Action)(() => {
                        LoadProgressBar.Maximum = progressInfo.Total;
                        LoadProgressBar.Value = progressInfo.Current;

                        LoadProgressLabel.Text = progressInfo.Stage switch {
                            ProfileLoadStage.TraceLoading => "Loading trace",
                            ProfileLoadStage.TraceProcessing => "Processing trace",
                            ProfileLoadStage.SymbolLoading => "Loading symbols",
                            ProfileLoadStage.PerfCounterProcessing => "Processing perf. counters"
                        };

                        if (progressInfo.Total != 0) {
                            double percentage = (double)progressInfo.Current / (double)progressInfo.Total;
                            ProgressPercentLabel.Text = $"{Math.Round(percentage * 100)} %";
                            LoadProgressBar.IsIndeterminate = false;
                        }
                        else {
                            LoadProgressBar.IsIndeterminate = true;
                        }
                    }));
                }, task)) {
                    DialogResult = true;
                    Close();
                }
                else if(!task.IsCanceled) {
                    MessageBox.Show($"Filed to load profile file {ProfileFilePath}", "IR Explorer",
                                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }

                IsLoadingProfile = false;
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e) {
            if (isLoadingProfile_) {
                loadTask_.CancelTask();
                await loadTask_.WaitForTaskAsync();
            }

            DialogResult = false;
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            ProfileAutocompleteBox.Focus();
        }

        private void ProfileBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(ProfileAutocompleteBox, "ETW Trace Files|*.etl|All Files|*.*");

        private void BinaryBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(BinaryAutocompleteBox, "Binary Files|*.exe;*.dll;*.sys;|All Files|*.*");

        private void DebugBrowseButton_OnClick(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(SymbolAutocompleteBox, "Debug Info Files|*.pdb|All Files|*.*");

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

            var clearMenuItem = new MenuItem {
                Header = "Clear"
            };

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
                SymbolAutocompleteBox.Text = pathPair.Item3;
                await OpenFiles();
            }
        }
    }
}
