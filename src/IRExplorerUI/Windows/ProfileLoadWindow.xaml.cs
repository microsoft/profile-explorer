// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IRExplorerCore;
using IRExplorerUI.Profile;
using Microsoft.Win32;

namespace IRExplorerUI {
    public partial class ProfileLoadWindow : Window {
        private CancelableTaskInstance loadTask_;

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

                if (await Session.LoadProfileData(ProfileFilePath, BinaryFilePath, DebugFilePath, progressInfo => {
                    Dispatcher.BeginInvoke((Action)(() => {
                        LoadProgressPanel.Visibility = Visibility.Visible;
                        LoadProgressBar.Maximum = progressInfo.Total;
                        LoadProgressBar.Value = progressInfo.Current;

                        LoadProgressLabel.Text = progressInfo.Stage switch {
                            ProfileLoadStage.TraceLoading => "Loading trace",
                            ProfileLoadStage.TraceProcessing => "Processing trace",
                            ProfileLoadStage.SymbolLoading => "Loading symbols",
                        };
                    }));
                }, task)) {
                    DialogResult = true;
                    Close();
                }
                else {
                    MessageBox.Show($"Filed to load profile file {ProfileFilePath}", "Compiler Studio",
                                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
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

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
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
