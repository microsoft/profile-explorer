// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProfileExplorer.Core.Session;

namespace ProfileExplorer.UI;

public partial class DiffOpenWindow : Window {
  public DiffOpenWindow(ISession session) {
    Session = session;
    InitializeComponent();
    DataContext = this;
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;
  public ISession Session { get; set; }
  public string BaseFilePath { get; set; }
  public string DiffFilePath { get; set; }

  private void UpdateButton_Click(object sender, RoutedEventArgs e) {
    BaseFilePath = BaseAutocompleteBox.Text.Trim();
    DiffFilePath = DiffAutocompleteBox.Text.Trim();
    OpenFiles();
  }

  private void OpenFiles() {
    if (Utils.ValidateFilePath(BaseFilePath, BaseAutocompleteBox, "base", this) &&
        Utils.ValidateFilePath(DiffFilePath, DiffAutocompleteBox, "diff", this)) {
      App.Settings.AddRecentComparedFiles(BaseFilePath, DiffFilePath);
      App.SaveApplicationSettings();

      DialogResult = true;
      Close();
    }
  }

  private void CancelButton_Click(object sender, RoutedEventArgs e) {
    DialogResult = false;
    Close();
  }

  private void RecentButton_Click(object sender, RoutedEventArgs e) {
    var menu = RecentButton.ContextMenu;
    menu.Items.Clear();

    foreach (var pathPair in App.Settings.RecentComparedFiles) {
      var item = new MenuItem();
      item.Header = $"Base: {pathPair.Item1}\nDiff: {pathPair.Item2}";
      item.Tag = pathPair;
      item.Click += RecentMenuItem_Click;
      menu.Items.Add(item);
      menu.Items.Add(new Separator());
    }

    var clearMenuItem = new MenuItem {
      Header = "Clear"
    };

    clearMenuItem.Click += RecentMenuItem_Click;
    menu.Items.Add(clearMenuItem);
    menu.IsOpen = true;
  }

  private void RecentMenuItem_Click(object sender, RoutedEventArgs e) {
    RecentButton.ContextMenu.IsOpen = false;
    var menuItem = sender as MenuItem;

    if (menuItem.Tag == null) {
      App.Settings.ClearRecentComparedFiles();
    }
    else {
      var pathPair = menuItem.Tag as Tuple<string, string>;
      BaseFilePath = pathPair.Item1;
      DiffFilePath = pathPair.Item2;
      OpenFiles();
    }
  }

  private void Window_Loaded(object sender, RoutedEventArgs e) {
    BaseAutocompleteBox.Focus();
  }

  private void BaseBrowseButton_Click(object sender, RoutedEventArgs e) {
    string path = ShowOpenFileDialog();

    if (path != null) {
      BaseAutocompleteBox.Text = path;
    }
  }

  private void DiffBrowseButton_Click(object sender, RoutedEventArgs e) {
    string path = ShowOpenFileDialog();

    if (path != null) {
      DiffAutocompleteBox.Text = path;
    }
  }

  private string ShowOpenFileDialog() {
    var fileDialog = new OpenFileDialog {
      DefaultExt = "*.*",
      Filter = Session.CompilerInfo.OpenFileFilter
    };

    bool? result = fileDialog.ShowDialog();

    if (result.HasValue && result.Value) {
      return fileDialog.FileName;
    }

    return null;
  }
}