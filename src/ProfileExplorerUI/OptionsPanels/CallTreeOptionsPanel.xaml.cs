// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class CallTreeOptionsPanel : OptionsPanelBase {
  public override double DefaultHeight => 450;
  public override double DefaultWidth => 400;

  public CallTreeOptionsPanel() {
    InitializeComponent();
  }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
  }

  private void ResetCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeSettings)Settings).NodePopupDuration = CallTreeSettings.DefaultNodePopupDuration;
    ReloadSettings();
  }

  private void ShortCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeSettings)Settings).NodePopupDuration = HoverPreview.HoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void LongCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeSettings)Settings).NodePopupDuration = HoverPreview.LongHoverDuration.Milliseconds;
    ReloadSettings();
  }
}