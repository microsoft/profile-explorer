// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows;

namespace IRExplorerUI;

public partial class OptionsWindow : Window {
  public OptionsWindow(ISession session) {
    InitializeComponent();
    SummaryOptionsPanel.Initialize(this, App.Settings.SectionSettings, session);
    DocumentOptionsPanel.Initialize(this, App.Settings.DocumentSettings, session);
    GraphOptionsPanel.Initialize(this, App.Settings.FlowGraphSettings, session);
    ExpressionGraphOptionsPanel.Initialize(this, App.Settings.ExpressionGraphSettings, session);
    DiffOptionsPanel.Initialize(this, App.Settings.DiffSettings, session);

    this.Closing += (sender, args) => {
      App.SaveApplicationSettings();
    };
  }
}