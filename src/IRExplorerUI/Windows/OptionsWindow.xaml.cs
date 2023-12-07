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
    LoadSettings();

    this.Closing += async (sender, args) => {
      await SaveAndReloadSettings();
    };
  }

  private void LoadSettings() {
    SummaryOptionsPanel.Initialize(this, App.Settings.SectionSettings, Session);
    DocumentOptionsPanel.Initialize(this, App.Settings.DocumentSettings, Session);
    GraphOptionsPanel.Initialize(this, App.Settings.FlowGraphSettings, Session);
    ExpressionGraphOptionsPanel.Initialize(this, App.Settings.ExpressionGraphSettings, Session);
    DiffOptionsPanel.Initialize(this, App.Settings.DiffSettings, Session);
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
      LoadSettings();
      await SaveAndReloadSettings();
    }
  }
}