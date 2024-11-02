// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.Windows;
using AutoUpdaterDotNET;
using Microsoft.Web.WebView2.Core;

namespace ProfileExplorer.UI;

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
          "Profile Explorer", MessageBoxButton.YesNo, MessageBoxImage.Information) !=
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
      MessageBox.Show($"Failed to download update: {ex}", "Profile Explorer", MessageBoxButton.OK,
                      MessageBoxImage.Error);
    }
  }

  private async void Window_Loaded(object sender, RoutedEventArgs e) {
    try {
      if (updateInfo_ != null) {
        NewVersionLabel.Text = updateInfo_.CurrentVersion;
        CurrentVersionLabel.Text = updateInfo_.InstalledVersion.ToString();

        if (string.IsNullOrEmpty(updateInfo_.ChangelogURL)) {
          return;
        }

        // Force light mode for the WebView2 control for now.
        var webView2Environment = await CoreWebView2Environment.CreateAsync(null, App.GetSettingsDirectoryPath());

        try {
          await Browser.EnsureCoreWebView2Async(webView2Environment);

          if (Browser.CoreWebView2 == null) {
            Trace.WriteLine("Failed to initialize WebView2 control in UpdateWindow.");
            return;
          }
        }
        catch (Exception ex) {
          Trace.WriteLine($"Failed to initialize WebView2 control in UpdateWindow: {ex.Message}");
          return;
        }

        Browser.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Light;
        Browser.Source = new Uri(updateInfo_.ChangelogURL);
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