// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace IRExplorerUI;

public partial class OptionsWindow : Window {
  public ISession Session { get; set; }

  public OptionsWindow(ISession session) {
    InitializeComponent();
    Session = session;
    LoadSettings();

    Closing += async (sender, args) => {
      await SaveAndReloadSettings();
    };
  }

  private void LoadSettings() {
    SummaryOptionsPanel.Initialize(this, App.Settings.SectionSettings, Session);
    DocumentOptionsPanel.Initialize(this, App.Settings.DocumentSettings, Session);
    GraphOptionsPanel.Initialize(this, App.Settings.FlowGraphSettings, Session);
    ExpressionGraphOptionsPanel.Initialize(this, App.Settings.ExpressionGraphSettings, Session);
    DiffOptionsPanel.Initialize(this, App.Settings.DiffSettings, Session);
    TimelineOptionsPanel.Initialize(this, App.Settings.TimelineSettings, Session);
    FlameGraphOptionsPanel.Initialize(this, App.Settings.FlameGraphSettings, Session);
    CallTreeOptionsPanel.Initialize(this, App.Settings.CallTreeSettings, Session);
    CallerCalleeOptionsPanel.Initialize(this, App.Settings.CallerCalleeSettings, Session);
    SourceFileOptionsPanel.Initialize(this, App.Settings.SourceFileSettings, Session);
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

  private void ExportButton_OnClick(object sender, RoutedEventArgs e) {
    string path = Utils.ShowSaveFileDialog("ZIP archive|*.zip", "*.zip", "Export settings");

    if (!string.IsNullOrEmpty(path)) {
      App.CloseLogFile();

      if (!App.Settings.SaveToArchive(path)) {
        Utils.ShowErrorMessageBox("Failed to export settings.", this);
      }

      App.OpenLogFile();
    }
  }

  private void ImportButton_OnClick(object sender, RoutedEventArgs e) {
    string path = Utils.ShowOpenFileDialog("ZIP archive|*.zip", "*.zip", "Import settings");

    if (!string.IsNullOrEmpty(path)) {
      if (Utils.ShowYesNoMessageBox(
            "Do you want to import new settings?\nAll existing settings will be lost and application will restart.",
            this) ==
          MessageBoxResult.No) {
        return;
      }

      App.CloseLogFile();

      if (App.Settings.LoadFromArchive(path)) {
        App.Restart();
      }
      else {
        Utils.ShowErrorMessageBox("Failed to import settings.", this);
      }
    }
  }
}