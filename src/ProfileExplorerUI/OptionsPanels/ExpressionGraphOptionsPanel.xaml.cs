// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class ExpressionGraphOptionsPanel : UserControl, IMvvmOptionsPanel {
  private ExpressionGraphOptionsPanelViewModel viewModel_;

  public ExpressionGraphOptionsPanel() {
    InitializeComponent();
    viewModel_ = new ExpressionGraphOptionsPanelViewModel();
    DataContext = viewModel_;
  }

  public double DefaultHeight => 450;
  public double MinimumHeight => 200;
  public double DefaultWidth => 380;
  public double MinimumWidth => 380;

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    viewModel_.Initialize(parent, (ExpressionGraphSettings)settings, session);
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