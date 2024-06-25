// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using static IRExplorerUI.ProfileDocumentMarkerSettings;

namespace IRExplorerUI.OptionsPanels;

public partial class DocumentProfilingOptionsPanel : OptionsPanelBase {
  public const double DefaultHeight = 320;
  public const double MinimumHeight = 200;
  public const double DefaultWidth = 350;
  public const double MinimumWidth = 350;
  private bool showsDocumentSettings_;
  public Dictionary<ValueUnitKind, string>
    ValueUnitKinds { get; } =
    new() {
      {ValueUnitKind.Second, "Second"},
      {ValueUnitKind.Millisecond, "Millisecond"},
      {ValueUnitKind.Microsecond, "Microsecond"},
      {ValueUnitKind.Nanosecond, "Nanosecond"}
    };

  public DocumentProfilingOptionsPanel() {
    InitializeComponent();
    ValueUnitComboBox.ItemsSource = ValueUnitKinds;
    PreviewMouseUp += SectionOptionsPanel_PreviewMouseUp;
  }

  public bool ShowsDocumentSettings {
    get => showsDocumentSettings_;
    set => SetField(ref showsDocumentSettings_, value);
  }

  private void SectionOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
    if (!Utils.SourceIsTextBox(e)) {
      NotifySettingsChanged();
    }
  }

  private void NotifySettingsChanged() {
    DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
      RaiseSettingsChanged(null);
    });
  }

  private void MaxWidthButton_Click(object sender, RoutedEventArgs e) {
    ((ProfileDocumentMarkerSettings)Settings).MaxPercentageBarWidth =
      DefaultMaxPercentageBarWidth;
    ReloadSettings();
  }

  private void WeightCutoffButton_Click(object sender, RoutedEventArgs e) {
    ((ProfileDocumentMarkerSettings)Settings).ElementWeightCutoff =
      DefaultElementWeightCutoff;
    ReloadSettings();
  }
}
