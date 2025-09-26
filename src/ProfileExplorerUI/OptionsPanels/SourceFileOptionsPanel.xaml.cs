// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class SourceFileOptionsPanel : UserControl, IMvvmOptionsPanel {
  private SourceFileOptionsPanelViewModel viewModel_;

  public SourceFileOptionsPanel() {
    InitializeComponent();
    viewModel_ = new SourceFileOptionsPanelViewModel();
    DataContext = viewModel_;
  }

  public double DefaultWidth => 400;
  public double DefaultHeight => 450;
  public double MinimumHeight => 200;
  public double MinimumWidth => 380;


  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    viewModel_.Initialize(parent, (SourceFileSettings)settings, session);
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