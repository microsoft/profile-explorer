// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class TimelineOptionsPanel : OptionsPanelBase {
  public TimelineOptionsPanel() {
    InitializeComponent();
  }

  private void ResetNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((TimelineSettings)Settings).CallStackPopupDuration = TimelineSettings.DefaultCallStackPopupDuration;
    ReloadSettings();
  }

  private void ShortNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((TimelineSettings)Settings).CallStackPopupDuration = HoverPreview.HoverDurationMs;
    ReloadSettings();
  }

  private void LongNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((TimelineSettings)Settings).CallStackPopupDuration = HoverPreview.LongHoverDurationMs;
    ReloadSettings();
  }
}