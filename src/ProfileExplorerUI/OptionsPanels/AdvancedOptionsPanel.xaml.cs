// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows.Input;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class AdvancedOptionsPanel : OptionsPanelBase {
  public const double DefaultHeight = 320;
  public const double MinimumHeight = 200;
  public const double DefaultWidth = 350;
  public const double MinimumWidth = 350;

  public AdvancedOptionsPanel() {
    InitializeComponent();
  }
}