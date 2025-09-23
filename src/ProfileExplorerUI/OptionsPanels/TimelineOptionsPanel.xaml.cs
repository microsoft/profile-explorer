// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class TimelineOptionsPanel : UserControl, IMvvmOptionsPanel {
  private TimelineOptionsPanelViewModel viewModel_;

  public TimelineOptionsPanel() {
    InitializeComponent();
    viewModel_ = new TimelineOptionsPanelViewModel();
    DataContext = viewModel_;
  }

  public double DefaultHeight => 320;
  public double MinimumHeight => 200;
  public double DefaultWidth => 380;
  public double MinimumWidth => 380;

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    viewModel_.Initialize(parent, (TimelineSettings)settings, session);
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