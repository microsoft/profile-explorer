// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace IRExplorerUI;

public partial class AboutWindow : Window {
  public AboutWindow() {
    InitializeComponent();
    DataContext = this;
  }

  public string CopyrightText => $"Copyright (c) {DateTime.Now.Year} Microsoft Corporation";
  public string VersionText => $"Version {Assembly.GetExecutingAssembly().GetName().Version.ToString()}";
  public string LicenseText => App.GetLicenseText();

  private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) {
      UseShellExecute = true
    });
  }
}
