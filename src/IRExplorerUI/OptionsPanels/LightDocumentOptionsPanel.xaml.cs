// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace IRExplorerUI.OptionsPanels;

public partial class LightDocumentOptionsPanel : OptionsPanelBase {
  public const double DefaultHeight = 500;
  public const double MinimumHeight = 300;
  public const double DefaultWidth = 360;
  public const double MinimumWidth = 360;
  private DocumentSettings settings_;

  public LightDocumentOptionsPanel() {
    InitializeComponent();
    PreviewMouseUp += DocumentOptionsPanel_PreviewMouseUp;
    PreviewKeyUp += DocumentOptionsPanel_PreviewKeyUp;
  }

  public bool SyntaxFileChanged { get; set; }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
    settings_ = (DocumentSettings)Settings;
  }

  public override void OnSettingsChanged(object newSettings) {
    settings_ = (DocumentSettings)newSettings;
  }

  public override void PanelClosing() {
  }

  public override void PanelResetting() {
  }

  private void DocumentOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
    NotifySettingsChanged();
  }

  private void DocumentOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
    NotifySettingsChanged();
  }

  private void NotifySettingsChanged() {
    DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
      RaiseSettingsChanged(null);
    });
  }

  private class ColorPickerInfo {
    public ColorPickerInfo(string name, Color value) {
      Name = name;
      Value = value;
    }

    public string Name { get; set; }
    public Color Value { get; set; }
  }
}