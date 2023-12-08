// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DocumentFormat.OpenXml.Drawing;
using IRExplorerUI.Settings;

namespace IRExplorerUI.Windows;

public class WorkspaceNameValidator : ValidationRule {
  public override ValidationResult Validate(object value, CultureInfo cultureInfo) {
    if (value is not string stringValue ||
        string.IsNullOrWhiteSpace(stringValue)) {
      return new ValidationResult(false, "Invalid workspace name");
    }

    if (App.Settings.WorkspaceOptions.HasWorkspace(stringValue)) {
      return new ValidationResult(false, "Workspace name already exists");
    }

    return ValidationResult.ValidResult;
  }
}

public partial class WorkspacesWindow : Window {
  private WorkspaceSettings settings_;

  public WorkspacesWindow() {
    InitializeComponent();
    settings_ = App.Settings.WorkspaceOptions;
    ReloadWorkspacesList();

    Closing += (sender, args) => {
      settings_.SaveWorkspaces();
      App.SaveApplicationSettings();
    };
  }

  private void ReloadWorkspacesList() {
    var list = new ObservableCollectionRefresh<Workspace>(settings_.Workspaces);
    WorkspacesList.ItemsSource = list;
  }

  private void DefaultButton_OnClick(object sender, RoutedEventArgs e) {
    if(Utils.ShowYesNoMessageBox("Restore builtin, default workspaces?", this) == MessageBoxResult.No) {
      return;
    }

    if (!settings_.RestoreDefaultWorkspaces()) {
      Utils.ShowErrorMessageBox("Failed to restore default workspaces.", this);
    }

    ReloadWorkspacesList();
  }

  private void SaveButton_OnClick(object sender, RoutedEventArgs e) {
    var ws = settings_.CreateWorkspace("Untitled");
    var mainWindow = Application.Current.MainWindow as MainWindow;

    if (!mainWindow.SaveDockLayout(ws.FilePath)) {
      Utils.ShowErrorMessageBox("Failed to create workspace.", this);
      settings_.RemoveWorkspace(ws);
      return;
    }

    ReloadWorkspacesList();
    Utils.SelectEditableListViewItem(WorkspacesList, ws.Order);
  }

  private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin) {
      if (e.OriginalSource.GetType().Name == "TextBoxView") {
        e.Handled = true;
        textBox.Focus();
        textBox.SelectAll();
      }
    }
  }

  private void RemoveButton_Click(object sender, RoutedEventArgs e) {
    var selectedWs = WorkspacesList.SelectedItem as Workspace;

    if (selectedWs != null &&
        Utils.ShowYesNoMessageBox("Do you want to remove the selected workspace?", this) ==
        MessageBoxResult.Yes) {
      settings_.RemoveWorkspace(selectedWs);
      ReloadWorkspacesList();
    }
  }

  private void ExportButton_Click(object sender, RoutedEventArgs e) {
    string path = Utils.ShowSaveFileDialog("ZIP archive|*.zip", "*.zip", "Export workspaces");

    if (!string.IsNullOrEmpty(path)) {
      if(!settings_.SaveToArchive(path)) {
        Utils.ShowErrorMessageBox("Failed to export workspaces.", this);
      }
    }
  }

  private void ImportButton_Click(object sender, RoutedEventArgs e) {
    string path = Utils.ShowOpenFileDialog("ZIP archive|*.zip", "*.zip", "Import workspaces");

    if (!string.IsNullOrEmpty(path)) {
      int loadedCount = 0;
      var newSettings = WorkspaceSettings.LoadFromArchive(path, out loadedCount);

      if (newSettings == null) {
        Utils.ShowErrorMessageBox("Failed to import workspaces.", this);
        return;
      }

      settings_ =   newSettings;
      App.Settings.WorkspaceOptions = newSettings;
      ReloadWorkspacesList();
      Utils.ShowMessageBox($"Successfully imported {loadedCount} workspaces.", this);
    }
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    Close();
  }
}