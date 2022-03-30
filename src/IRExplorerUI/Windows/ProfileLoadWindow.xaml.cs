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
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using Microsoft.Win32;

namespace IRExplorerUI {
    public partial class ProfileLoadWindow : Window, INotifyPropertyChanged {
        private CancelableTaskInstance loadTask_;
        private bool isLoadingProfile_;
        private ProfileDataProviderOptions options_;
        private SymbolFileSourceOptions symbolOptions_;

        public ProfileLoadWindow(ISession session) {
            InitializeComponent();
            DataContext = this;
            Session = session;
            loadTask_ = new CancelableTaskInstance();

            Options = App.Settings.ProfileOptions;
            SymbolOptions = App.Settings.SymbolOptions;
            var loadedDoc = Session.SessionState.FindLoadedDocument(Session.MainDocumentSummary);

            // For executables, try to set the executable directory as an optional debug source.
            if (loadedDoc.BinaryFileExists && Utils.IsExecutableFile(loadedDoc.BinaryFilePath)) {
                SetAdditionalDirectories(loadedDoc.BinaryFilePath);
            }
        }

        private void SetAdditionalDirectories(string binaryFilePath) {
            var binaryDir = Utils.TryGetDirectoryName(binaryFilePath);

            if (string.IsNullOrEmpty(binaryDir)) {
                return;
            }

            if (!Options.HasBinaryPath(binaryFilePath)) {
                Options.BinarySearchPaths.Insert(0, binaryDir);
                OnPropertyChange(nameof(Options));
            }

            //? TODO: Use InsertSymbolPath
            if (!SymbolOptions.HasSymbolPath(binaryDir)) {
                SymbolOptions.SymbolSearchPaths.Insert(0, binaryDir);
                OnPropertyChange(nameof(SymbolOptions));
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
            await OpenFiles();
        }

        private async Task OpenFiles() {
            ProfileFilePath = Utils.CleanupPath(ProfileFilePath);
            BinaryFilePath = Utils.CleanupPath(BinaryFilePath);

            if (Utils.ValidateFilePath(ProfileFilePath, ProfileAutocompleteBox, "profile", this)) {
                //? TODO: Save the entire state for the Recent menu - could serialize the Options 
                //? and save it as a string
                App.Settings.AddRecentProfileFiles(ProfileFilePath, BinaryFilePath, "");
                App.SaveApplicationSettings();

                //? TODO: Disable buttons
                var task = loadTask_.CreateTask();
                IsLoadingProfile = true;


                if (await Session.LoadProfileData(ProfileFilePath, BinaryFilePath, 
                                                  options_, symbolOptions_, progressInfo => {
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
                    MessageBox.Show($"Failed to load profile file {ProfileFilePath}", "IR Explorer",
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
        
        private void ProfileBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(ProfileAutocompleteBox, "ETW Trace Files|*.etl|All Files|*.*");

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

        private async void BinaryAutocompleteBox_OnTextChanged(object sender, RoutedEventArgs e) {
            var binaryFilePath = BinaryAutocompleteBox.Text;

            if (File.Exists(binaryFilePath) && Utils.IsExecutableFile(binaryFilePath)) {
                SetAdditionalDirectories(binaryFilePath);
            }
        }
    }
}
