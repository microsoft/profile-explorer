// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows.Input;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class PreviewPopupOptionsPanel : OptionsPanelBase {
  public override double DefaultHeight => 250;

  public PreviewPopupOptionsPanel() {
    InitializeComponent();
  }
}