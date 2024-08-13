// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class SourceFileOptionsPanel : OptionsPanelBase {
  public override double DefaultHeight => 450;
  public override double DefaultWidth => 400;
  private SourceFileSettings settings_;

  public SourceFileOptionsPanel() {
    InitializeComponent();
  }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
    settings_ = (SourceFileSettings)Settings;
    ProfilingOptionsPanel.DataContext = settings_.ProfileMarkerSettings;
    ReloadMappedPathsList();
    ReloadExcludedPathsList();
  }

  public override void OnSettingsChanged(object newSettings) {
    settings_ = (SourceFileSettings)newSettings;
    ProfilingOptionsPanel.DataContext = null;
    ProfilingOptionsPanel.DataContext = settings_.ProfileMarkerSettings;
  }

  private void ReloadMappedPathsList() {
    var mappings = new List<KeyValuePair<string, string>>();

    foreach (var pair in settings_.FinderSettings.SourceMappings) {
      mappings.Add(pair);
    }

    var list = new ObservableCollectionRefresh<KeyValuePair<string, string>>(mappings);
    MappedPathsList.ItemsSource = list;
  }

  private void ReloadExcludedPathsList() {
    var list = new ObservableCollectionRefresh<string>(settings_.FinderSettings.DisabledSourceMappings);
    ExcludedPathsList.ItemsSource = list;
  }

  private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    if (sender is TextBox textBox) {
      Utils.SelectTextBoxListViewItem(textBox, ExcludedPathsList);
      e.Handled = true;
    }
  }

  private void RemoveMappedPath_Click(object sender, RoutedEventArgs e) {
    if (MappedPathsList.SelectedItem is KeyValuePair<string, string> pair) {
      settings_.FinderSettings.SourceMappings.Remove(pair.Key);
      ReloadMappedPathsList();
    }
  }

  private void ClearMappedPath_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to clear the list?", this) == MessageBoxResult.Yes) {
      settings_.FinderSettings.SourceMappings.Clear();
      ReloadMappedPathsList();
    }
  }

  private void AddExcludedPath_Click(object sender, RoutedEventArgs e) {
    settings_.FinderSettings.DisabledSourceMappings.Add("");
    ReloadExcludedPathsList();

    // Wait for the UI to update
    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => {
      Utils.SelectEditableListViewItem(ExcludedPathsList, settings_.FinderSettings.DisabledSourceMappings.Count - 1);
    });
  }

  private void ClearExcludedPath_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to clear the list?", this) == MessageBoxResult.Yes) {
      settings_.FinderSettings.DisabledSourceMappings.Clear();
      ReloadExcludedPathsList();
    }
  }

  private void RemoveExcludedPath_Click(object sender, RoutedEventArgs e) {
    if (ExcludedPathsList.SelectedItem is string path) {
      settings_.FinderSettings.DisabledSourceMappings.Remove(path);
      ReloadExcludedPathsList();
    }
  }
}