﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels;

public partial class GeneralOptionsPanel : OptionsPanelBase {
  private GeneralSettings settings_;
  public const double DefaultHeight = 320;
  public const double MinimumHeight = 200;
  public const double DefaultWidth = 350;
  public const double MinimumWidth = 350;

  public GeneralOptionsPanel() {
    InitializeComponent();
    PreviewMouseUp += SectionOptionsPanel_PreviewMouseUp;
  }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
    settings_ = (GeneralSettings)Settings;
  }

  public override void OnSettingsChanged(object newSettings) {
    settings_ = (GeneralSettings)newSettings;
  }

  private void SectionOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
    NotifySettingsChanged();
  }

  private void NotifySettingsChanged() {
    DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
      RaiseSettingsChanged(null);
    });
  }

  private void ResetUIZoomButton_Click(object sender, RoutedEventArgs e) {
    settings_.WindowScaling = 1.0;
    ReloadSettings();
  }
}