// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProfileExplorer.UI.Windows;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class FunctionMarkingOptionsPanel : OptionsPanelBase {
  private FunctionMarkingSettings settings_;

  public override double DefaultHeight => 550;
  public override double DefaultWidth => 400;

  public FunctionMarkingOptionsPanel() {
    InitializeComponent();
    ModulePaletteSelector.PalettesSource = ColorPalette.GradientBuiltinPalettes;
  }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
    settings_ = (FunctionMarkingSettings)Settings;
    ReloadModuleList();
    ReloadFunctionList();
    ReloadMarkingsList();
  }

  public override void OnSettingsChanged(object newSettings) {
    settings_ = (FunctionMarkingSettings)newSettings;
    ReloadModuleList();
    ReloadFunctionList();
    ReloadMarkingsList();
  }

  private void ReloadModuleList() {
    var list = new ObservableCollectionRefresh<FunctionMarkingStyle>(settings_.ModuleColors);
    ModuleList.ItemsSource = list;
  }

  private void ReloadFunctionList() {
    var list = new ObservableCollectionRefresh<FunctionMarkingStyle>(settings_.FunctionColors);
    FunctionList.ItemsSource = list;
  }

  private void ReloadMarkingsList() {
    var list = new ObservableCollectionRefresh<FunctionMarkingSet>(settings_.SavedSets);
    MarkingsList.ItemsSource = list;
  }

  private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    if (sender is TextBox textBox) {
      Utils.SelectTextBoxListViewItem(textBox, ModuleList);
    }
  }

  private void ModuleRemove_Click(object sender, RoutedEventArgs e) {
    if (ModuleList.SelectedItem is FunctionMarkingStyle pair) {
      settings_.ModuleColors.Remove(pair);
      ReloadModuleList();
      NotifySettingsChanged();
    }
  }

  private void ModuleAdd_Click(object sender, RoutedEventArgs e) {
    settings_.ModuleColors.Add(new FunctionMarkingStyle("", Colors.White));
    ReloadModuleList();
    NotifySettingsChanged();

    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => {
      Utils.SelectEditableListViewItem(ModuleList, settings_.ModuleColors.Count - 1);
    });
  }

  private void ClearModule_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to clear the list?", this) == MessageBoxResult.Yes) {
      settings_.ModuleColors.Clear();
      ReloadModuleList();
      NotifySettingsChanged();
    }
  }

  private void ClearFunction_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to clear the list?", this) == MessageBoxResult.Yes) {
      settings_.FunctionColors.Clear();
      ReloadFunctionList();
      NotifySettingsChanged();
    }
  }

  private void FunctionRemove_Click(object sender, RoutedEventArgs e) {
    if (FunctionList.SelectedItem is FunctionMarkingStyle pair) {
      settings_.FunctionColors.Remove(pair);
      ReloadFunctionList();
      NotifySettingsChanged();
    }
  }

  private void FunctionAdd_Click(object sender, RoutedEventArgs e) {
    settings_.FunctionColors.Add(new FunctionMarkingStyle("", Colors.White));
    ReloadFunctionList();
    NotifySettingsChanged();

    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => {
      Utils.SelectEditableListViewItem(FunctionList, settings_.FunctionColors.Count - 1);
    });
  }

  private void MarkingClear_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to clear the list?", this) == MessageBoxResult.Yes) {
      settings_.SavedSets.Clear();
      ReloadMarkingsList();
      NotifySettingsChanged();
    }
  }

  private void MarkingRemove_Click(object sender, RoutedEventArgs e) {
    if (MarkingsList.SelectedItem is FunctionMarkingSet set) {
      settings_.SavedSets.Remove(set);
      ReloadMarkingsList();
      NotifySettingsChanged();
    }
  }

  private void MarkingImport_Click(object sender, RoutedEventArgs e) {
    if (settings_.ImportMarkings(this)) {
      ReloadMarkingsList();
      ReloadFunctionList();
      ReloadModuleList();
      NotifySettingsChanged();
    }
  }

  private void MarkingExport_Click(object sender, RoutedEventArgs e) {
    settings_.ExportMarkings(this);
  }

  private void MarkingSave_Click(object sender, RoutedEventArgs e) {
    TextInputWindow input = new("Save marked functions/modules", "Saved marking set name:", "Save", "Cancel");

    if (input.Show(out string result, true)) {
      settings_.SaveCurrentMarkingSet(result);
      ReloadMarkingsList();
    }
  }

  private void MarkingLoad_Click(object sender, RoutedEventArgs e) {
    if (MarkingsList.SelectedItem is FunctionMarkingSet set) {
      settings_.AppendMarkingSet(set);
      NotifySettingsChanged();
    }
  }

  private void MarkingCheckBox_Changed(object sender, RoutedEventArgs e) {
    NotifySettingsChanged();
  }
}