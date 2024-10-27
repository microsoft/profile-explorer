// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class FlameGraphOptionsPanel : OptionsPanelBase {
  private FlameGraphSettings settings_;

  public FlameGraphOptionsPanel() {
    InitializeComponent();
    DefaultPaletteSelector.PalettesSource = ColorPalette.BuiltinPalettes;
    KernelPaletteSelector.PalettesSource = ColorPalette.BuiltinPalettes;
    ManagedPaletteSelector.PalettesSource = ColorPalette.BuiltinPalettes;

    //? TODO: Change to calling Initialize
    DetailsPanel.DataContext = App.Settings.CallTreeNodeSettings;
    FunctionListOptionsPanel.DataContext = App.Settings.CallTreeNodeSettings.FunctionListViewFilter;
  }

  public override double DefaultHeight => 450;
  public override double DefaultWidth => 400;

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
    settings_ = (FlameGraphSettings)Settings;
  }

  public override void OnSettingsChanged(object newSettings) {
    settings_ = (FlameGraphSettings)newSettings;
    ReloadAdditionalSettings();
  }

  public override void ReloadSettings() {
    base.ReloadSettings();
    ReloadAdditionalSettings();
  }

  private void ReloadAdditionalSettings() {
    DetailsPanel.DataContext = null;
    DetailsPanel.DataContext = App.Settings.CallTreeNodeSettings;
    FunctionListOptionsPanel.DataContext = null;
    FunctionListOptionsPanel.DataContext = App.Settings.CallTreeNodeSettings.FunctionListViewFilter;
  }

  public override void PanelResetting() {
    base.PanelResetting();
    App.Settings.CallTreeNodeSettings.Reset();
  }

  private void ResetNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    settings_.NodePopupDuration = FlameGraphSettings.DefaultNodePopupDuration;
    ReloadSettings();
  }

  private void ShortNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    settings_.NodePopupDuration = HoverPreview.HoverDurationMs;
    ReloadSettings();
  }

  private void LongNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    settings_.NodePopupDuration = HoverPreview.LongHoverDurationMs;
    ReloadSettings();
  }

  private void ResetDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration =
      CallTreeNodeSettings.DefaultPreviewPopupDuration;
    ReloadSettings();
  }

  private void ShortDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration = HoverPreview.HoverDurationMs;
    ReloadSettings();
  }

  private void LongDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration = HoverPreview.LongHoverDurationMs;
    ReloadSettings();
  }

  private void ResetFilterWeightButton_Click(object sender, RoutedEventArgs e) {
    ((ProfileListViewFilter)FunctionListOptionsPanel.DataContext).MinWeight = ProfileListViewFilter.DefaultMinWeight;
    ReloadSettings();
  }
}