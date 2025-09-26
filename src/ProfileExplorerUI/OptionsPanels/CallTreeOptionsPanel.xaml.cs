// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class CallTreeOptionsPanel : UserControl, IMvvmOptionsPanel {
  private CallTreeOptionsPanelViewModel viewModel_;

  public CallTreeOptionsPanel() {
    InitializeComponent();
    viewModel_ = new CallTreeOptionsPanelViewModel();
    DataContext = viewModel_;
  }

  public double DefaultHeight => 450;
  public double DefaultWidth => 400;
  public double MinimumHeight => 200;
  public double MinimumWidth => 380;

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    viewModel_.Initialize(parent, (CallTreeSettings)settings, session);
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