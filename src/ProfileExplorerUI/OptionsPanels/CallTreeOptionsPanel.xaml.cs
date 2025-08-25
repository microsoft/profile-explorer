// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using ProfileExplorerCore2.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class CallTreeOptionsPanel : OptionsPanelBase {
  public CallTreeOptionsPanel() {
    InitializeComponent();
  }

  public override double DefaultHeight => 450;
  public override double DefaultWidth => 400;

  public override void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    base.Initialize(parent, settings, session);
  }

  private void ResetCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeSettings)Settings).NodePopupDuration = CallTreeSettings.DefaultNodePopupDuration;
    ReloadSettings();
  }

  private void ShortCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeSettings)Settings).NodePopupDuration = HoverPreview.HoverDurationMs;
    ReloadSettings();
  }

  private void LongCallStackPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeSettings)Settings).NodePopupDuration = HoverPreview.LongHoverDurationMs;
    ReloadSettings();
  }
}