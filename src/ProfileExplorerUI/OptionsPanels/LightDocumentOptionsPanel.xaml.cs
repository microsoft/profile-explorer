// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;

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