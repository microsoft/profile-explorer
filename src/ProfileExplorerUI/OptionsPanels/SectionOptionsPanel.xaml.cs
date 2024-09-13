// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class SectionOptionsPanel : OptionsPanelBase {
  public SectionOptionsPanel() {
    InitializeComponent();
  }

  public override double DefaultHeight => 450;
  public override double DefaultWidth => 400;

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
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