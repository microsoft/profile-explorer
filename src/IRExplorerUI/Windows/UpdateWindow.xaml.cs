// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Diagnostics;
using System.Windows;
using AutoUpdaterDotNET;
using Microsoft.Web.WebView2.Core;

namespace IRExplorerUI;

public partial class UpdateWindow : Window {
  private UpdateInfoEventArgs updateInfo_;

  public UpdateWindow(UpdateInfoEventArgs args) {
    updateInfo_ = args;
    InitializeComponent();
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;
  public bool InstallUpdate { get; set; }

  private void UpdateButton_Click(object sender, RoutedEventArgs e) {
    using var centerForm = new DialogCenteringHelper(this);

    if (MessageBox.Show(
          "Download and update to the latest version?\nThis will close the current session and restart the application.",
          "IR Explorer", MessageBoxButton.YesNo, MessageBoxImage.Information) !=
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
      MessageBox.Show($"Failed to download update: {ex}", "IR Explorer", MessageBoxButton.OK,
                      MessageBoxImage.Error);
    }
  }

  private async void Window_Loaded(object sender, RoutedEventArgs e) {
    try {
      if (updateInfo_ != null) {
        NewVersionLabel.Text = updateInfo_.CurrentVersion;
        CurrentVersionLabel.Text = updateInfo_.InstalledVersion.ToString();

        if (!string.IsNullOrEmpty(updateInfo_.ChangelogURL)) {
          // Force light mode for the WebView2 control for now.
          await Browser.EnsureCoreWebView2Async();
          Browser.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Light;
          Browser.Source = new Uri(updateInfo_.ChangelogURL);
        }
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to release notes: {ex}");
    }
  }

  private void CancelButton_Click(object sender, RoutedEventArgs e) {
    Close();
  }
}