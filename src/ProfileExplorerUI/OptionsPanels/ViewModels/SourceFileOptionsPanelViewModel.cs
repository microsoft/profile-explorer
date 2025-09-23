// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Diagnostics.Runtime;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Services;
using ProfileExplorer.UI.Windows;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class ExcludedFilePathMappingsViewModel : ObservableObject {
  [ObservableProperty]
  private string path_;

  [ObservableProperty]
  private bool isSelected_;

  public ExcludedFilePathMappingsViewModel(string path) {
    Path_ = path;
  }
}

public partial class FilePathMappingsViewModel : ObservableObject {
  [ObservableProperty]
  private string key_;

  [ObservableProperty]
  private string value_;

  [ObservableProperty]
  private bool isSelected_;

  public FilePathMappingsViewModel(KeyValuePair<string, string> mapping) {
    Key_ = mapping.Key;
    Value_ = mapping.Value;
  }
}

public partial class SourceFileOptionsPanelViewModel : OptionsPanelBaseViewModel<SourceFileSettings> {
  private IConfirmationProvider confirmationProvider_;

  [ObservableProperty]
  private DocumentProfilingOptionsPanelViewModel documentProfilingOptionsPanelViewModel_;

  [ObservableProperty]
  private bool showInlineAssembly_;

  [ObservableProperty]
  private bool autoExpandInlineAssembly_;

  [ObservableProperty]
  private bool showSourceStatements_;

  [ObservableProperty]
  private bool replaceInsignificantSourceStatements_;

  [ObservableProperty]
  private bool showSourceStatementsOnMargin_;

  [ObservableProperty]
  private bool syncLineWithDocument_;

  [ObservableProperty]
  private bool syncInlineeWithDocument_;

  [ObservableProperty]
  private bool highlightCurrentLine_;

  [ObservableProperty]
  private bool syncStyleWithDocument_;

  [ObservableProperty]
  private string fontName_;

  [ObservableProperty]
  private double fontSize_;

  [ObservableProperty]
  private Color backgroundColor_;

  [ObservableProperty]
  private Color textColor_;

  [ObservableProperty]
  private Color currentLineBorderColor_;

  [ObservableProperty]
  private Color marginBackgroundColor_;

  [ObservableProperty]
  private Color assemblyTextColor_;

  [ObservableProperty]
  private Color assemblyBackColor_;

  [ObservableProperty]
  private ObservableCollection<ExcludedFilePathMappingsViewModel> excludedFilePathMappings_;

  [ObservableProperty]
  private ObservableCollection<FilePathMappingsViewModel> filePathMappings_;

  public override void Initialize(FrameworkElement parent, SourceFileSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    confirmationProvider_ = new DialogConfirmationProvider(parent);

    DocumentProfilingOptionsPanelViewModel_ = new DocumentProfilingOptionsPanelViewModel();
    DocumentProfilingOptionsPanelViewModel_.Initialize(parent, settings.ProfileMarkerSettings, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(SourceFileSettings settings) {
    ShowInlineAssembly_ = settings.ShowInlineAssembly;
    AutoExpandInlineAssembly_ = settings.AutoExpandInlineAssembly;
    ShowSourceStatements_ = settings.ShowSourceStatements;
    ReplaceInsignificantSourceStatements_ = settings.ReplaceInsignificantSourceStatements;
    ShowSourceStatementsOnMargin_ = settings.ShowSourceStatementsOnMargin;
    SyncLineWithDocument_ = settings.SyncLineWithDocument;
    SyncInlineeWithDocument_ = settings.SyncInlineeWithDocument;
    HighlightCurrentLine_ = settings.HighlightCurrentLine;
    SyncStyleWithDocument_ = settings.SyncStyleWithDocument;
    FontName_ = settings.FontName;
    FontSize_ = settings.FontSize;
    BackgroundColor_ = settings.BackgroundColor;
    TextColor_ = settings.TextColor;
    CurrentLineBorderColor_ = settings.CurrentLineBorderColor;
    MarginBackgroundColor_ = settings.MarginBackgroundColor;
    AssemblyTextColor_ = settings.AssemblyTextColor;
    AssemblyBackColor_ = settings.AssemblyBackColor;
    ExcludedFilePathMappings_ = new ObservableCollection<ExcludedFilePathMappingsViewModel>(
      settings.FinderSettings.DisabledSourceMappings.Select(x => new ExcludedFilePathMappingsViewModel(x)));
    FilePathMappings_ = new ObservableCollection<FilePathMappingsViewModel>(
      settings.FinderSettings.SourceMappings.Select(x => new FilePathMappingsViewModel(x)));
  }

  [RelayCommand]
  private void AddExcludedPath() {
    foreach (var item in ExcludedFilePathMappings_) {
      item.IsSelected_ = false;
    }

    var newItem = new ExcludedFilePathMappingsViewModel("");
    newItem.IsSelected_ = true;
    ExcludedFilePathMappings_.Add(newItem);
  }

  [RelayCommand]
  private void RemoveExcludedPath() {
    var toRemove = ExcludedFilePathMappings_.Where(p => p.IsSelected_).ToList();
    foreach (var item in toRemove) {
      ExcludedFilePathMappings_.Remove(item);
    }
  }

  [RelayCommand]
  private async void ClearExcludedPaths() {
    if (await confirmationProvider_.RequestConfirmation("Do you want to clear the list?")) {
      ExcludedFilePathMappings_.Clear();
    }
  }

  [RelayCommand]
  private void RemoveMappedPath() {
        var toRemove = FilePathMappings_.Where(p => p.IsSelected_).ToList();
    foreach (var item in toRemove) {
      FilePathMappings_.Remove(item);
    }
  }

  [RelayCommand]
  private async void ClearMappedPaths() {
    if (await confirmationProvider_.RequestConfirmation("Do you want to clear the list?")) {
      FilePathMappings_.Clear();
    }
  }

  public override void SaveSettings() {
    if (DocumentProfilingOptionsPanelViewModel_ != null) {
      DocumentProfilingOptionsPanelViewModel_.SaveSettings();
    }
    
    if (Settings_ != null) {
      Settings_.ShowInlineAssembly = ShowInlineAssembly_;
      Settings_.AutoExpandInlineAssembly = AutoExpandInlineAssembly_;
      Settings_.ShowSourceStatements = ShowSourceStatements_;
      Settings_.ReplaceInsignificantSourceStatements = ReplaceInsignificantSourceStatements_;
      Settings_.ShowSourceStatementsOnMargin = ShowSourceStatementsOnMargin_;
      Settings_.SyncLineWithDocument = SyncLineWithDocument_;
      Settings_.SyncInlineeWithDocument = SyncInlineeWithDocument_;
      Settings_.HighlightCurrentLine = HighlightCurrentLine_;
      Settings_.SyncStyleWithDocument = SyncStyleWithDocument_;
      Settings_.FontName = FontName_;
      Settings_.FontSize = FontSize_;
      Settings_.BackgroundColor = BackgroundColor_;
      Settings_.TextColor = TextColor_;
      Settings_.CurrentLineBorderColor = CurrentLineBorderColor_;
      Settings_.MarginBackgroundColor = MarginBackgroundColor_;
      Settings_.AssemblyTextColor = AssemblyTextColor_;
      Settings_.AssemblyBackColor = AssemblyBackColor_;
      Settings_.FinderSettings.DisabledSourceMappings = ExcludedFilePathMappings_.Select(x => x.Path_).ToList();
      Settings_.FinderSettings.SourceMappings = FilePathMappings_.ToDictionary(x => x.Key_, x => x.Value_);
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public SourceFileSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new SourceFileSettings();
      Settings_ = defaultSettings;
      PopulateFromSettings(Settings_);
    }
  }

  /// <summary>
  /// Called when the panel is about to close
  /// </summary>
  public void PanelClosing() {
    // Ensure settings are saved before closing
    SaveSettings();
    // Any other cleanup can be added here
  }
}