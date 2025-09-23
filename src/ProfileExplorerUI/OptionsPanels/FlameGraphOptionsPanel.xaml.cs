// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class FlameGraphOptionsPanel : UserControl, IMvvmOptionsPanel {
  private FlameGraphOptionsPanelViewModel viewModel_;

  public FlameGraphOptionsPanel() {
    InitializeComponent();
    viewModel_ = new FlameGraphOptionsPanelViewModel();
    DataContext = viewModel_;

    DefaultPaletteSelector.PalettesSource = ColorPalette.BuiltinPalettes;
    KernelPaletteSelector.PalettesSource = ColorPalette.BuiltinPalettes;
    ManagedPaletteSelector.PalettesSource = ColorPalette.BuiltinPalettes;
  }

  public double DefaultHeight => 450;
  public double DefaultWidth => 400;
  public double MinimumWidth => 400;
  public double MinimumHeight => 300;

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    viewModel_.Initialize(parent, (FlameGraphSettings)settings, session);
  }

  public void SaveSettings() {
    viewModel_.SaveSettings();
  }

  public SettingsBase GetCurrentSettings() {
    return viewModel_.GetCurrentSettings();
  }

  public void ResetSettings() {
    viewModel_.ResetSettings();
  }

  public void PanelClosing() {
    // Clean up any resources if needed
    viewModel_.PanelClosing();
  }
}