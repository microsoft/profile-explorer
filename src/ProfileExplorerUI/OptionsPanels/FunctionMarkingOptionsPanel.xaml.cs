// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Session;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class FunctionMarkingOptionsPanel : UserControl, IMvvmOptionsPanel {
  private FunctionMarkingOptionsPanelViewModel viewModel_;

  public FunctionMarkingOptionsPanel() {
    InitializeComponent();
    ModulePaletteSelector.PalettesSource = ColorPalette.GradientBuiltinPalettes;
    viewModel_ = new FunctionMarkingOptionsPanelViewModel();
    DataContext = viewModel_;
  }

  // IMvvmOptionsPanel properties
  public double DefaultWidth => 500;
  public double DefaultHeight => 600;
  public double MinimumWidth => 400;
  public double MinimumHeight => 300;

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    viewModel_.Initialize(parent, (FunctionMarkingSettings)settings, session);
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