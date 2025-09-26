// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class DocumentOptionsPanel : UserControl, IMvvmOptionsPanel {
  private DocumentOptionsPanelViewModel viewModel_;

  public DocumentOptionsPanel() {
    InitializeComponent();
    viewModel_ = new DocumentOptionsPanelViewModel();
    DataContext = viewModel_;
  }

  public double DefaultHeight => 470;
  public double MinimumHeight => 200;
  public double DefaultWidth => 380;
  public double MinimumWidth => 380;

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    viewModel_.Initialize(parent, (DocumentSettings)settings, session);
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