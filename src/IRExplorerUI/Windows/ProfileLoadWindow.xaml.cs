// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace IRExplorerUI {
    public partial class ProfileLoadWindow : Window {
        public ProfileLoadWindow() {
            InitializeComponent();
            DataContext = this;
        }

        public string ProfileFilePath { get; set; }
        public string BinaryFilePath { get; set; }
        public string DebugFilePath { get; set; }

        private void UpdateButton_Click(object sender, RoutedEventArgs e) {
            ProfileFilePath = ProfileAutocompleteBox.Text.Trim();
            BinaryFilePath = BinaryAutocompleteBox.Text.Trim();
            DebugFilePath = DebugAutocompleteBox.Text.Trim();
            OpenFiles();
        }

        private void OpenFiles() {
            if (ValidateFilePath(ProfileFilePath, ProfileAutocompleteBox, "profile") &&
                ValidateFilePath(BinaryFilePath, BinaryAutocompleteBox, "binary") &&
                ValidateFilePath(BinaryFilePath, DebugAutocompleteBox, "debug")) {
                DialogResult = true;
                Close();
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
                Filter = "Log Files|*.txt;*.log;*.ir;|All Files|*.*"
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
