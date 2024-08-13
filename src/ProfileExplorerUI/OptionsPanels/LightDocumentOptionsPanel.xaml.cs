// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Input;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class LightDocumentOptionsPanel : OptionsPanelBase {
  public LightDocumentOptionsPanel() {
    InitializeComponent();
  }

  public bool SyntaxFileChanged { get; set; }

  public override void Initialize(FrameworkElement parent, SettingsBase settings, ISession session) {
    base.Initialize(parent, settings, session);
  }

  public override void OnSettingsChanged(object newSettings) {
  }

  public override void PanelClosing() {
  }

  public override void PanelResetting() {
  }
}