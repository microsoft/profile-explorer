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

        public ProfileLoadWindow(ISession session) {
            InitializeComponent();
            DataContext = this;
            Session = session;
            loadTask_ = new CancelableTaskInstance();
            
            ProfileAutocompleteBox.Text = @"E:\spec\leela3.etl";
            BinaryAutocompleteBox.Text = @"leela_s_base.msvc-diff";
            DebugAutocompleteBox.Text = @"E:\spec\leela_s.pdb";
        }

        public ISession Session { get; set; }
        public string ProfileFilePath { get; set; }
        public string BinaryFilePath { get; set; }
        public string DebugFilePath { get; set; }

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
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e) {
            ProfileFilePath = ProfileAutocompleteBox.Text.Trim();
            BinaryFilePath = BinaryAutocompleteBox.Text.Trim();
            DebugFilePath = DebugAutocompleteBox.Text.Trim();
            await OpenFiles();
        }

        private async Task OpenFiles() {
            if (ValidateFilePath(ProfileFilePath, ProfileAutocompleteBox, "profile") &&
                //ValidateFilePath(BinaryFilePath, BinaryAutocompleteBox, "binary") &&
                ValidateFilePath(DebugFilePath, DebugAutocompleteBox, "debug")) {

                //? TODO: Disable buttons
                //? Add cancel button
                var task = loadTask_.CreateTask();
                IsLoadingProfile = true;

                if (await Session.LoadProfileData(ProfileFilePath, BinaryFilePath, DebugFilePath, progressInfo => {
                    Dispatcher.BeginInvoke((Action)(() => {
                        LoadProgressBar.Maximum = progressInfo.Total;
                        LoadProgressBar.Value = progressInfo.Current;

                        LoadProgressLabel.Text = progressInfo.Stage switch {
                            ProfileLoadStage.TraceLoading => "Loading trace",
                            ProfileLoadStage.TraceProcessing => "Processing trace",
                            ProfileLoadStage.SymbolLoading => "Loading symbols",
                        };

                        double percentage = (double)progressInfo.Current / (double)progressInfo.Total;
                        ProgressPercentLabel.Text = $"{Math.Round(percentage * 100)} %";
                    }));
                }, task)) {
                    DialogResult = true;
                    Close();
                }
                else {
                    MessageBox.Show($"Filed to load profile file {ProfileFilePath}", "Compiler Studio",
                                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }

                IsLoadingProfile = false;
            }
        }

        private bool ValidateFilePath(string path, AutoCompleteBox box, string fileType) {
            if (!File.Exists(path)) {
                using var centerForm = new DialogCenteringHelper(this);
                MessageBox.Show($"Could not find {fileType} file {path}", "Compiler Studio",
                                MessageBoxButton.OK, MessageBoxImage.Exclamation);

                box.Focus();
                return false;
            }

            return true;
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

        private void BaseBrowseButton_Click(object sender, RoutedEventArgs e) {
            string path = ShowOpenFileDialog("ETW Trace Files|*.etl|All Files|*.*");

            if (path != null) {
                ProfileAutocompleteBox.Text = path;
            }
        }

        private void DiffBrowseButton_Click(object sender, RoutedEventArgs e) {
            string path = ShowOpenFileDialog("Binary Files|*.exe;*.dll;*.sys;|All Files|*.*");

            if (path != null) {
                BinaryAutocompleteBox.Text = path;
            }
        }

        private string ShowOpenFileDialog(string filter) {
            var fileDialog = new OpenFileDialog {
                DefaultExt = "*.*",
                Filter = filter
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                return fileDialog.FileName;
            }

            return null;
        }

        private void DebugBrowseButton_OnClick(object sender, RoutedEventArgs e) {
            string path = ShowOpenFileDialog("Debug Info Files|*.pdb|All Files|*.*");

            if (path != null) {
                DebugAutocompleteBox.Text = path;
            }
        }
    }
}
