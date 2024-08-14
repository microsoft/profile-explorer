// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class ColumnOptionsPanel : OptionsPanelBase {
  public override double DefaultHeight => 340;
  public override double MinimumHeight => 340;
  public override double DefaultWidth => 280;
  public override double MinimumWidth => 300;
  public Dictionary<OptionalColumnStyle.PartVisibility, string>
    PartVisibilityKinds { get; } =
    new() {
      {OptionalColumnStyle.PartVisibility.Always, "Always"},
      {OptionalColumnStyle.PartVisibility.Never, "Never"},
      {OptionalColumnStyle.PartVisibility.IfActiveColumn, "If Active Column"}
    };

  public ColumnOptionsPanel() {
    InitializeComponent();
    ShowIconsComboBox.ItemsSource = PartVisibilityKinds;
    ShowBackgroundComboBox.ItemsSource = PartVisibilityKinds;
    ShowPercentageComboBox.ItemsSource = PartVisibilityKinds;
  }
}