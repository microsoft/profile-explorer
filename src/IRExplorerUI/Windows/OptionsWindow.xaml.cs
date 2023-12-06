// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Threading.Tasks;
using System.Windows;

namespace IRExplorerUI;

public partial class OptionsWindow : Window {
  public ISession Session { get; set; }
  
  public OptionsWindow(ISession session) {
    InitializeComponent();
    Session = session;
    
    SummaryOptionsPanel.Initialize(this, App.Settings.SectionSettings, session);
    DocumentOptionsPanel.Initialize(this, App.Settings.DocumentSettings, session);
    GraphOptionsPanel.Initialize(this, App.Settings.FlowGraphSettings, session);
    ExpressionGraphOptionsPanel.Initialize(this, App.Settings.ExpressionGraphSettings, session);
    DiffOptionsPanel.Initialize(this, App.Settings.DiffSettings, session);

    this.Closing += async (sender, args) => {
      await SaveAndReloadSettings();
    };
  }

  private async Task SaveAndReloadSettings() {
    App.SaveApplicationSettings();
    await Session.ReloadSettings();
  }

  private async void CloseButton_OnClick(object sender, RoutedEventArgs e) {
    DialogResult = true;
    Close();
  }

  private async void ApplyButton_OnClick(object sender, RoutedEventArgs e) {
    await SaveAndReloadSettings();
  }

  private async void ResetButton_OnClick(object sender, RoutedEventArgs e) {
    if (Utils.ShowYesNoMessageBox("Do you want to reset all settings to their default values?", this) ==
        MessageBoxResult.Yes) {
      App.Settings.Reset();
      await SaveAndReloadSettings();
    }
  }
}