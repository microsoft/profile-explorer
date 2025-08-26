// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using ProfileExplorerCore.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class GeneralOptionsPanel : OptionsPanelBase {
  private GeneralSettings settings_;

  public GeneralOptionsPanel() {
    InitializeComponent();
  }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    base.Initialize(parent, settings, session);
    settings_ = (GeneralSettings)Settings;
  }

  public override void OnSettingsChanged(object newSettings) {
    settings_ = (GeneralSettings)newSettings;
  }

  private void ResetUIZoomButton_Click(object sender, RoutedEventArgs e) {
    settings_.WindowScaling = 1.0;
    ReloadSettings();
  }

  private void CpuCoreLimitButton_Click(object sender, RoutedEventArgs e) {
    settings_.CpuCoreLimit = GeneralSettings.DefaultCpuCoreLimit;
    ReloadSettings();
  }
}