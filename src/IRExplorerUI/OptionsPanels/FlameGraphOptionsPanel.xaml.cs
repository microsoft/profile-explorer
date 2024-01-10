// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels;

public partial class FlameGraphOptionsPanel : OptionsPanelBase {
  public const double DefaultHeight = 320;
  public const double MinimumHeight = 200;
  public const double DefaultWidth = 350;
  public const double MinimumWidth = 350;

  public FlameGraphOptionsPanel() {
    InitializeComponent();
    DetailsPanel.DataContext = App.Settings.CallTreeNodeSettings;
    PreviewPopupOptionsPanel.DataContext = App.Settings.PreviewPopupSettings;
    PreviewMouseUp += SectionOptionsPanel_PreviewMouseUp;
    PreviewKeyUp += SectionOptionsPanel_PreviewKeyUp;
  }

  protected override void ReloadSettings() {
    base.ReloadSettings();
    DetailsPanel.DataContext = null;
    DetailsPanel.DataContext = App.Settings.CallTreeNodeSettings;
    PreviewPopupOptionsPanel.DataContext = null;
    PreviewPopupOptionsPanel.DataContext = App.Settings.PreviewPopupSettings;
  }

  public override void PanelResetting() {
    base.PanelResetting();
    App.Settings.CallTreeNodeSettings.Reset();
    App.Settings.PreviewPopupSettings.Reset();
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

  private void ResetNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((FlameGraphSettings)Settings).NodePopupDuration = FlameGraphSettings.DefaultNodePopupDuration;
    ReloadSettings();
  }

  private void ShortNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((FlameGraphSettings)Settings).NodePopupDuration = HoverPreview.HoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void LongNodePopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((FlameGraphSettings)Settings).NodePopupDuration = HoverPreview.LongHoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void ResetDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration = CallTreeNodeSettings.DefaultPreviewPopupDuration;
    ReloadSettings();
  }

  private void ShortDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration = HoverPreview.HoverDuration.Milliseconds;
    ReloadSettings();
  }

  private void LongDetailsPopupDurationButton_Click(object sender, RoutedEventArgs e) {
    ((CallTreeNodeSettings)DetailsPanel.DataContext).PreviewPopupDuration = HoverPreview.LongHoverDuration.Milliseconds;
    ReloadSettings();
  }
}