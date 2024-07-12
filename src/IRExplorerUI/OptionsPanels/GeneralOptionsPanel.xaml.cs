// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels;

public partial class GeneralOptionsPanel : OptionsPanelBase {
  private GeneralSettings settings_;
  public GeneralOptionsPanel() {
    InitializeComponent();
  }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
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