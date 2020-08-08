// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using AutoUpdaterDotNET;

namespace IRExplorerUI {
    /// <summary>
    ///     Interaction logic for UpdateWindow.xaml
    /// </summary>
    public partial class UpdateWindow : Window {
        private UpdateInfoEventArgs updateInfo_;

        public UpdateWindow(UpdateInfoEventArgs args) {
            updateInfo_ = args;
            InitializeComponent();
        }

        public bool InstallUpdate { get; set; }

        private void UpdateButton_Click(object sender, RoutedEventArgs e) {
            using var centerForm = new DialogCenteringHelper(this);

            if (MessageBox.Show(
                    "Download and update to the latest version?\nThis will close the current session and restart the application.",
                    "Compiler Studio", MessageBoxButton.YesNo, MessageBoxImage.Information) !=
                MessageBoxResult.Yes) {
                return;
            }

            try {
                if (AutoUpdater.DownloadUpdate(updateInfo_)) {
                    InstallUpdate = true;
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to download update: {ex}", "Compiler Studio", MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            try {
                if (updateInfo_ != null) {
                    NewVersionLabel.Text = updateInfo_.CurrentVersion;
                    CurrentVersionLabel.Text = updateInfo_.InstalledVersion.ToString();
                    Browser.Navigate(updateInfo_.ChangelogURL);
                }
            }
            catch (Exception) { }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            Close();
        }
    }
}
