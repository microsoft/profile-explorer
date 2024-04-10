// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels;

public partial class CallTreeOptionsPanel : OptionsPanelBase {
  public const double DefaultHeight = 320;
  public const double MinimumHeight = 200;
  public const double DefaultWidth = 350;
  public const double MinimumWidth = 350;

  public CallTreeOptionsPanel() {
    InitializeComponent();
    PreviewMouseUp += SectionOptionsPanel_PreviewMouseUp;
  }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
    FunctionMarkingOptionsPanel.Initialize(parent, App.Settings.MarkingSettings, session);
  }
  
  private void SectionOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
    NotifySettingsChanged();
  }

  private void NotifySettingsChanged() {
    DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
      RaiseSettingsChanged(null);
    });
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