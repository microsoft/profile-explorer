// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class GeneralOptionsPanel : UserControl {
  private GeneralOptionsPanelViewModel viewModel_;

  public GeneralOptionsPanel() {
    InitializeComponent();
    viewModel_ = new GeneralOptionsPanelViewModel();
    DataContext = viewModel_;
  }

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    viewModel_.Initialize(parent, (GeneralSettings)settings, session);
  }

  public void SaveSettings() {
    viewModel_.SaveSettings();
  }
}