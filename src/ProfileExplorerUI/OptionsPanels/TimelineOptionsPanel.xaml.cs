// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;

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
    ((TimelineSettings)Settings).CallStackPopupDuration = HoverPreview.HoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void LongNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((TimelineSettings)Settings).CallStackPopupDuration = HoverPreview.LongHoverDuration.Milliseconds;
    ReloadSettings();
  }
}