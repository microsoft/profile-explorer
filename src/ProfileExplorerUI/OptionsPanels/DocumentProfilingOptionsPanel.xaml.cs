﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Windows;
using static ProfileExplorer.UI.ProfileDocumentMarkerSettings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class DocumentProfilingOptionsPanel : OptionsPanelBase {
  private bool showsDocumentSettings_;

  public DocumentProfilingOptionsPanel() {
    InitializeComponent();
    ValueUnitComboBox.ItemsSource = ValueUnitKinds;
  }

  public Dictionary<ValueUnitKind, string>
    ValueUnitKinds { get; } =
    new() {
      {ValueUnitKind.Second, "Second"},
      {ValueUnitKind.Millisecond, "Millisecond"},
      {ValueUnitKind.Microsecond, "Microsecond"},
      {ValueUnitKind.Nanosecond, "Nanosecond"}
    };

  public bool ShowsDocumentSettings {
    get => showsDocumentSettings_;
    set => SetField(ref showsDocumentSettings_, value);
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