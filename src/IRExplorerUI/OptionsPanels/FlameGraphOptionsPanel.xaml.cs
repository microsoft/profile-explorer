// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerUI.Controls;
using Microsoft.Diagnostics.Tracing.Parsers.MicrosoftWindowsWPF;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace IRExplorerUI.OptionsPanels;

public partial class FlameGraphOptionsPanel : OptionsPanelBase {
  private FlameGraphSettings settings_;
  public override double DefaultHeight => 450;
  public const double MinimumHeight = 200;
  public const double DefaultWidth = 350;
  public const double MinimumWidth = 350;

  public FlameGraphOptionsPanel() {
    InitializeComponent();
    DefaultPaletteSelector.PalettesSource = ColorPalette.BuiltinPalettes;
    KernelPaletteSelector.PalettesSource = ColorPalette.BuiltinPalettes;
    ManagedPaletteSelector.PalettesSource = ColorPalette.BuiltinPalettes;

    DetailsPanel.DataContext = App.Settings.CallTreeNodeSettings;
    PreviewPopupOptionsPanel.DataContext = App.Settings.PreviewPopupSettings;
    FunctionListOptionsPanel.DataContext = App.Settings.CallTreeNodeSettings.FunctionListViewFilter;
    PreviewMouseUp += SectionOptionsPanel_PreviewMouseUp;
  }


  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
    settings_ = (FlameGraphSettings)Settings;
    ReloadModuleList();
  }

  public override void OnSettingsChanged(object newSettings) {
    settings_ = (FlameGraphSettings)newSettings;
    ReloadModuleList();
  }

  private void ReloadModuleList() {
    var list = new ObservableCollectionRefresh<FlameGraphSettings.ModuleStyle>(settings_.ModuleColors);
    ModuleList.ItemsSource = list;
  }

  protected override void ReloadSettings() {
    base.ReloadSettings();
    DetailsPanel.DataContext = null;
    DetailsPanel.DataContext = App.Settings.CallTreeNodeSettings;
    PreviewPopupOptionsPanel.DataContext = null;
    PreviewPopupOptionsPanel.DataContext = App.Settings.PreviewPopupSettings;
    FunctionListOptionsPanel.DataContext = null;
    FunctionListOptionsPanel.DataContext = App.Settings.CallTreeNodeSettings.FunctionListViewFilter;
  }

  public override void PanelResetting() {
    base.PanelResetting();
    App.Settings.CallTreeNodeSettings.Reset();
    App.Settings.PreviewPopupSettings.Reset();
  }

  private void SectionOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
    NotifySettingsChanged();
  }

  private void SectionOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
    NotifySettingsChanged();
  }

  private void NotifySettingsChanged() {
    DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
      RaiseSettingsChanged(null);
    });
  }

  private void ResetNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    settings_.NodePopupDuration = FlameGraphSettings.DefaultNodePopupDuration;
    ReloadSettings();
  }

  private void ShortNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    settings_.NodePopupDuration = HoverPreview.HoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void LongNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    settings_.NodePopupDuration = HoverPreview.LongHoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void ResetDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration =
      CallTreeNodeSettings.DefaultPreviewPopupDuration;
    ReloadSettings();
  }

  private void ShortDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration = HoverPreview.HoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void LongDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration = HoverPreview.LongHoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void ResetFilterWeightButton_Click(object sender, RoutedEventArgs e) {
    ((ProfileListViewFilter)FunctionListOptionsPanel.DataContext).MinWeight = ProfileListViewFilter.DefaultMinWeight;
    ReloadSettings();
  }

  private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    if (sender is TextBox textBox) {
      Utils.SelectTextBoxListViewItem(textBox, ModuleList);
      e.Handled = true;
    }
  }

  private void ModuleRemove_Click(object sender, RoutedEventArgs e) {
    if (ModuleList.SelectedItem is FlameGraphSettings.ModuleStyle pair) {
      settings_.ModuleColors.Remove((pair));
      ReloadModuleList();
      NotifySettingsChanged();
    }
  }

  private void ModuleAdd_Click(object sender, RoutedEventArgs e) {
    settings_.ModuleColors.Add(new FlameGraphSettings.ModuleStyle("", Colors.White));
    ReloadModuleList();
    NotifySettingsChanged();

    Dispatcher.BeginInvoke(() => {
      var newItem = settings_.ModuleColors.Last();
      Utils.FocusTextBoxListViewItem(newItem, ModuleList);
    }, DispatcherPriority.ContextIdle);
  }

  private void ClearModule_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to clear the list?", this) == MessageBoxResult.Yes) {
      settings_.ModuleColors.Clear();
      ReloadModuleList();
      NotifySettingsChanged();
    }
  }
}