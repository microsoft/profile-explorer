// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.ComponentModel;
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
      App.SaveApplicationSettings();
    };
  }

  private void ReloadWorkspacesList() {
    var list = new ObservableCollectionRefresh<Workspace>(settings_.Workspaces);
    WorkspacesList.ItemsSource = list;
  }

  private void DefaultButton_OnClick(object sender, RoutedEventArgs e) {
    if (!settings_.RestoreDefaultWorkspaces()) {
      MessageBox.Show("Failed to restore default workspaces.");
    }

    ReloadWorkspacesList();
  }

  private void SaveButton_OnClick(object sender, RoutedEventArgs e) {
    var ws = settings_.CreateWorkspace("Untitled");
    var mainWindow = App.Current.MainWindow as MainWindow;
    
    if(!mainWindow.SaveDockLayout(ws.FilePath)) {
      MessageBox.Show("Failed to create workspace.");
      settings_.RemoveWorkspace(ws);
      return;
    }

    ReloadWorkspacesList();
    Utils.SelectEditableListViewItem(WorkspacesList, ws.Order);
  }


  private void TextBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
    if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin) {
      if (e.OriginalSource.GetType().Name == "TextBoxView") {
        e.Handled = true;
        textBox.Focus();
        textBox.SelectAll();
      }
    }
  }

  private void RemoveButton_Click(object sender, RoutedEventArgs e) {
    var selectedWs = WorkspacesList.SelectedItem as Workspace; ;

    if (selectedWs != null &&
        MessageBox.Show("Do you want to remove the selected workspace?", "IR Explorer",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) ==
        MessageBoxResult.Yes) {
      settings_.RemoveWorkspace(selectedWs);
      ReloadWorkspacesList();
    }
  }
}