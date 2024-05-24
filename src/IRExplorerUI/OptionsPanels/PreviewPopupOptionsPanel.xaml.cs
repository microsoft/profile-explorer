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
    PreviewMouseUp += SectionOptionsPanel_PreviewMouseUp;
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
}
