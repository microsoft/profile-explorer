// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;
using IRExplorerUI.Diff;
using Microsoft.Win32;

namespace IRExplorerUI.OptionsPanels;

public partial class DiffOptionsPanel : OptionsPanelBase {
  public const double DefaultHeight = 650;
  public const double MinimumHeight = 200;
  public const double DefaultWidth = 360;
  public const double MinimumWidth = 360;

  public DiffOptionsPanel() {
    InitializeComponent();
    ExternalAppPathTextbox.ExtensionFilter = "*.exe";
  }

  private void ExternalAppPathButton_Click(object sender, RoutedEventArgs e) {
    using var centerForm = new DialogCenteringHelper(this);

    var fileDialog = new OpenFileDialog {
      DefaultExt = "bcompare.exe",
      Filter = "BC executables|bcompare.exe"
    };

    bool? result = fileDialog.ShowDialog();

    if (result.HasValue && result.Value) {
      ExternalAppPathTextbox.Text = fileDialog.FileName;
    }
  }

  private void DefaultAppPathButton_Click(object sender, RoutedEventArgs e) {
    string path = BeyondCompareDiffBuilder.FindBeyondCompareExecutable();

    if (!string.IsNullOrEmpty(path)) {
      ExternalAppPathTextbox.Text = path;
    }
    else {
      using var centerForm = new DialogCenteringHelper(this);
      MessageBox.Show("Could not find Beyond Compare executable", "IR Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }
  }
}