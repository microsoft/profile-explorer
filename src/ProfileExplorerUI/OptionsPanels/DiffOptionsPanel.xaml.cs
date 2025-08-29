// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using Microsoft.Win32;
using ProfileExplorer.Core.Diff;
using ProfileExplorer.UI.Diff;

namespace ProfileExplorer.UI.OptionsPanels;

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
      MessageBox.Show("Could not find Beyond Compare executable", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }
  }
}