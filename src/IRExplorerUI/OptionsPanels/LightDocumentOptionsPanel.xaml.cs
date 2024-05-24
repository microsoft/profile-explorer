// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels;

public partial class LightDocumentOptionsPanel : OptionsPanelBase {
  public const double DefaultHeight = 500;
  public const double MinimumHeight = 300;
  public const double DefaultWidth = 360;
  public const double MinimumWidth = 360;

  public LightDocumentOptionsPanel() {
    InitializeComponent();
    PreviewMouseUp += DocumentOptionsPanel_PreviewMouseUp;
  }

  public bool SyntaxFileChanged { get; set; }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
  }

  public override void OnSettingsChanged(object newSettings) {
  }

  public override void PanelClosing() {
  }

  public override void PanelResetting() {
  }

  private void DocumentOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
    NotifySettingsChanged();
  }

  private void NotifySettingsChanged() {
    DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
      RaiseSettingsChanged(null);
    });
  }
}
