﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class SectionOptionsPanel : OptionsPanelBase {
  public SectionOptionsPanel() {
    InitializeComponent();
  }

  public override double DefaultHeight => 450;
  public override double DefaultWidth => 400;

  public override void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    base.Initialize(parent, settings, session);
  }

  private void EditButton_Click(object sender, RoutedEventArgs e) {
    string settingsPath = App.GetSectionsDefinitionFilePath(Session.CompilerInfo.CompilerIRName);
    App.LaunchSettingsFileEditor(settingsPath);
  }

  private void ReloadButton_Click(object sender, RoutedEventArgs e) {
    Session.SectionStyleProvider.LoadSettings();
  }

  private void ResetCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((UISectionSettings)Settings).CallStackPopupDuration = UISectionSettings.DefaultCallStackPopupDuration;
    ReloadSettings();
  }

  private void ShortCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((UISectionSettings)Settings).CallStackPopupDuration = HoverPreview.HoverDurationMs;
    ReloadSettings();
  }

  private void LongCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((UISectionSettings)Settings).CallStackPopupDuration = HoverPreview.LongHoverDurationMs;
    ReloadSettings();
  }
}