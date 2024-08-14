// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace ProfileExplorer.UI;

public partial class AboutWindow : Window {
  public AboutWindow() {
    InitializeComponent();
    DataContext = this;
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;
  public string CopyrightText => $"Copyright (c) {DateTime.Now.Year} Microsoft Corporation";
  public string VersionText => $"Version {Assembly.GetExecutingAssembly()?.GetName()?.Version}";
  public string LicenseText => App.GetLicenseText();

  private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) {
      UseShellExecute = true
    });
  }
}