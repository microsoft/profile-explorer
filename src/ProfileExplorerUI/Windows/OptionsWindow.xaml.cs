// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using System.Windows;

namespace ProfileExplorer.UI;

public partial class OptionsWindow : Window {
  public OptionsWindow(IUISession session) {
    InitializeComponent();
    DataContext = this;
    Session = session;
    LoadSettings();

    Closing += async (sender, args) => {
      await SaveAndReloadSettings();
    };
  }

  public IUISession Session { get; set; }
  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;

  private void LoadSettings() {
    GeneralOptionsPanel.Initialize(this, App.Settings.GeneralSettings, Session);
    SymbolOptionsPanel.Initialize(this, App.Settings.SymbolSettings, Session);
    SummaryOptionsPanel.Initialize(this, App.Settings.SectionSettings, Session);
    DocumentOptionsPanel.Initialize(this, App.Settings.DocumentSettings, Session);
    GraphOptionsPanel.Initialize(this, App.Settings.FlowGraphSettings, Session);
    ExpressionGraphOptionsPanel.Initialize(this, App.Settings.ExpressionGraphSettings, Session);
    // DiffOptionsPanel.Initialize(this, App.Settings.DiffSettings, Session);
    TimelineOptionsPanel.Initialize(this, App.Settings.TimelineSettings, Session);
    FlameGraphOptionsPanel.Initialize(this, App.Settings.FlameGraphSettings, Session);
    CallTreeOptionsPanel.Initialize(this, App.Settings.CallTreeSettings, Session);
    CallerCalleeOptionsPanel.Initialize(this, App.Settings.CallerCalleeSettings, Session);
    SourceFileOptionsPanel.Initialize(this, App.Settings.SourceFileSettings, Session);
    PreviewPopupOptionsPanel.Initialize(this, App.Settings.PreviewPopupSettings, Session);
    FunctionMarkingOptionsPanel.Initialize(this, App.Settings.MarkingSettings, Session);
  }

  private async Task SaveAndReloadSettings() {
    // Save settings from view models back to their settings objects
    SavePanelSettings();
    
    App.SaveApplicationSettings();
    await Session.ReloadSettings();
  }
  
  private void SavePanelSettings() {
    // Save settings from panels that use the new MVVM pattern
    GeneralOptionsPanel.SaveSettings();
    SymbolOptionsPanel.SaveSettings();
    FunctionMarkingOptionsPanel.SaveSettings();
    SourceFileOptionsPanel.SaveSettings();
    SummaryOptionsPanel.SaveSettings();
    FlameGraphOptionsPanel.SaveSettings();
    TimelineOptionsPanel.SaveSettings();

    // TODO: Add SaveSettings() calls for other panels as they are migrated to MVVM
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