// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels;

public partial class SectionOptionsPanel : OptionsPanelBase {
  public const double DefaultHeight = 320;
  public const double MinimumHeight = 200;
  public const double DefaultWidth = 350;
  public const double MinimumWidth = 350;

  public SectionOptionsPanel() {
    InitializeComponent();
    PreviewMouseUp += SectionOptionsPanel_PreviewMouseUp;
    PreviewKeyUp += SectionOptionsPanel_PreviewKeyUp;
  }

  private void SectionOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
    NotifySettingsChanged();
  }

  private void SectionOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
    NotifySettingsChanged();
  }

  private void NotifySettingsChanged() {
    DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
      RaiseSettingsChanged(null);
    });
  }

  private void EditButton_Click(object sender, RoutedEventArgs e) {
    string settingsPath = App.GetSectionsDefinitionFilePath(Session.CompilerInfo.CompilerIRName);
    App.LaunchSettingsFileEditor(settingsPath);
  }

  private void ReloadButton_Click(object sender, RoutedEventArgs e) {
    Session.CompilerInfo.SectionStyleProvider.LoadSettings();
  }

  private void ResetCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((SectionSettings)Settings).CallStackPopupDuration = SectionSettings.DefaultCallStackPopupDuration;
    ReloadSettings();
  }
  
  private void ShortCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((SectionSettings)Settings).CallStackPopupDuration = HoverPreview.HoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void LongCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((SectionSettings)Settings).CallStackPopupDuration = HoverPreview.LongHoverDuration.Milliseconds;
    ReloadSettings();
  }
}