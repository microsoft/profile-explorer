// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.Core.Session;

namespace ProfileExplorer.UI.Windows;

public partial class AttachSpgoProfileWindow : Window {
  public const string ProfileFileFilter = "All Files (*.*)|*.*";

  public AttachSpgoProfileWindow(IReadOnlyList<ILoadedDocument> targets,
                                  ILoadedDocument selectedTarget, string profilePath) {
    InitializeComponent();
    DataContext = this;
    ProfilePathBox.Text = profilePath;
    TargetBinaryBox.ItemsSource = targets;
    TargetBinaryBox.SelectedItem = selectedTarget;
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;

  private void TargetBinaryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    var target = TargetBinaryBox.SelectedItem as ILoadedDocument;
    TargetBinaryPathBox.Text = target?.BinaryFileExists == true
      ? target.BinaryFile.FilePath
      : target?.FilePath;
    PdbPathBox.Text = target?.DebugInfoFileExists == true ? target.DebugInfoFile.FilePath : "";
    TargetBinaryStatus.Text = target == null
      ? "Select a target binary."
      : target.BinaryFileExists
        ? ""
        : "The binary file has not been loaded locally. No download will be attempted.";
  }

  private void ProfileBrowseButton_Click(object sender, RoutedEventArgs e) {
    Utils.ShowOpenFileDialog(ProfilePathBox, ProfileFileFilter, "*.*", "Select SPGO profile");
  }

  private void PdbBrowseButton_Click(object sender, RoutedEventArgs e) {
    Utils.ShowOpenFileDialog(PdbPathBox, "Debug Files (*.pdb)|*.pdb|All Files (*.*)|*.*",
                             "*.pdb", "Select PDB");
  }

  private void Window_Loaded(object sender, RoutedEventArgs e) {
    TargetBinaryBox.Focus();
  }
}
