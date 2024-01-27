// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRExplorerUI.Controls;

namespace IRExplorerUI.OptionsPanels;

public partial class FlameGraphOptionsPanel : OptionsPanelBase {
  public const double DefaultHeight = 320;
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
    PreviewKeyUp += SectionOptionsPanel_PreviewKeyUp;
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
    ((FlameGraphSettings)Settings).NodePopupDuration = FlameGraphSettings.DefaultNodePopupDuration;
    ReloadSettings();
  }

  private void ShortNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((FlameGraphSettings)Settings).NodePopupDuration = HoverPreview.HoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void LongNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((FlameGraphSettings)Settings).NodePopupDuration = HoverPreview.LongHoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void ResetDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration = CallTreeNodeSettings.DefaultPreviewPopupDuration;
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
}

public class ComboBoxItemTemplateSelector : DataTemplateSelector {
  public DataTemplate SelectedTemplate { get; set; }

  public override DataTemplate SelectTemplate(object item, DependencyObject container) {
    return SelectedTemplate;
  }
}