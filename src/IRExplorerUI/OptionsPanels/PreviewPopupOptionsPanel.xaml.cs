// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels;

public partial class PreviewPopupOptionsPanel : OptionsPanelBase {
  public override double DefaultHeight => 250;

  public PreviewPopupOptionsPanel() {
    InitializeComponent();
  }
}