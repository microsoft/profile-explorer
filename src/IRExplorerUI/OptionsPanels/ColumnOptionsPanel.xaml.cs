// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels;

public partial class ColumnOptionsPanel : OptionsPanelBase {
  public override double DefaultHeight => 340;
  public const double MinimumHeight = 200;
  public override double DefaultWidth => 280;
  public const double MinimumWidth = 300;

  public Dictionary<OptionalColumnStyle.PartVisibility, string>
    PartVisibilityKinds { get; } =
    new Dictionary<OptionalColumnStyle.PartVisibility, string>() {
      {OptionalColumnStyle.PartVisibility.Always, "Always"},
      {OptionalColumnStyle.PartVisibility.Never, "Never"},
      {OptionalColumnStyle.PartVisibility.IfActiveColumn, "If Active Column"}
    };

  public ColumnOptionsPanel() {
    InitializeComponent();
    ShowIconsComboBox.ItemsSource = PartVisibilityKinds;
    ShowBackgroundComboBox.ItemsSource = PartVisibilityKinds;
    ShowPercentageComboBox.ItemsSource = PartVisibilityKinds;

    PreviewMouseUp += SectionOptionsPanel_PreviewMouseUp;
    PreviewKeyUp += SectionOptionsPanel_PreviewKeyUp;
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

  private void MaxWidthButton_Click(object sender, RoutedEventArgs e) {

  }
}
