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
public partial class SectionOptionsPanelViewModel : OptionsPanelBaseViewModel<UISectionSettings> {
  [ObservableProperty]
  private bool computeStatistics_;

  [ObservableProperty]
  private bool includeCallGraphStatistics_;

  [ObservableProperty]
  private bool showDemangledNames_;

  [ObservableProperty]
  private bool demangleNoReturnType_;

  [ObservableProperty]
  private bool demangleNoSpecialKeywords_;

  [ObservableProperty]
  private bool demangleOnlyNames_;

  [ObservableProperty]
  private bool showMangleNamesColumn_;

  [ObservableProperty]
  private bool alternateListRows_;

  [ObservableProperty]
  private bool functionSearchCaseSensitive_;

  [ObservableProperty]
  private bool sectionSearchCaseSensitive_;

  [ObservableProperty]
  private Color newSectionColor_;

  [ObservableProperty]
  private Color missingSectionColor_;

  [ObservableProperty]
  private Color changedSectionColor_;

  [ObservableProperty]
  private bool showModulePanel_;

  [ObservableProperty]
  private bool syncSelection_;

  [ObservableProperty]
  private bool syncSourceFile_;

  [ObservableProperty]
  private bool showCallStackPopup_;

  [ObservableProperty]
  private int callStackPopupDuration_;

  [ObservableProperty]
  private bool showPerformanceCounterColumns_;

  [ObservableProperty]
  private bool showPerformanceMetricColumns_;

  [ObservableProperty]
  private bool appendTimeToTotalColumn_;

  [ObservableProperty]
  private bool appendTimeToSelfColumn_;

  public override void Initialize(FrameworkElement parent, UISectionSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(UISectionSettings settings) {
    ComputeStatistics_ = settings.ComputeStatistics;
    IncludeCallGraphStatistics_ = settings.IncludeCallGraphStatistics;
    ShowDemangledNames_ = settings.ShowDemangledNames;
    DemangleNoReturnType_ = settings.DemangleNoReturnType;
    DemangleNoSpecialKeywords_ = settings.DemangleNoSpecialKeywords;
    DemangleOnlyNames_ = settings.DemangleOnlyNames;
    ShowMangleNamesColumn_ = settings.ShowMangleNamesColumn;
    AlternateListRows_ = settings.AlternateListRows;
    FunctionSearchCaseSensitive_ = settings.FunctionSearchCaseSensitive;
    SectionSearchCaseSensitive_ = settings.SectionSearchCaseSensitive;
    NewSectionColor_ = settings.NewSectionColor;
    MissingSectionColor_ = settings.MissingSectionColor;
    ChangedSectionColor_ = settings.ChangedSectionColor;
    ShowModulePanel_ = settings.ShowModulePanel;
    SyncSelection_ = settings.SyncSelection;
    SyncSourceFile_ = settings.SyncSourceFile;
    ShowCallStackPopup_ = settings.ShowCallStackPopup;
    CallStackPopupDuration_ = settings.CallStackPopupDuration;
    ShowPerformanceCounterColumns_ = settings.ShowPerformanceCounterColumns;
    ShowPerformanceMetricColumns_ = settings.ShowPerformanceMetricColumns;
    AppendTimeToTotalColumn_ = settings.AppendTimeToTotalColumn;
    AppendTimeToSelfColumn_ = settings.AppendTimeToSelfColumn;
  }

  [RelayCommand]
  private void ResetCallStackPopupDuration() {
    CallStackPopupDuration_ = UISectionSettings.DefaultCallStackPopupDuration;
  }

  [RelayCommand]
  private void SetShortCallStackPopupDuration() {
    CallStackPopupDuration_ = HoverPreview.HoverDurationMs;
  }

  [RelayCommand]
  private void SetLongCallStackPopupDuration() {
    CallStackPopupDuration_ = HoverPreview.LongHoverDurationMs;
  }

  public override void SaveSettings() {
    if (Settings_ != null) {
      Settings_.ComputeStatistics = ComputeStatistics_;
      Settings_.IncludeCallGraphStatistics = IncludeCallGraphStatistics_;
      Settings_.ShowDemangledNames = ShowDemangledNames_;
      Settings_.DemangleNoReturnType = DemangleNoReturnType_;
      Settings_.DemangleNoSpecialKeywords = DemangleNoSpecialKeywords_;
      Settings_.DemangleOnlyNames = DemangleOnlyNames_;
      Settings_.ShowMangleNamesColumn = ShowMangleNamesColumn_;
      Settings_.AlternateListRows = AlternateListRows_;
      Settings_.FunctionSearchCaseSensitive = FunctionSearchCaseSensitive_;
      Settings_.SectionSearchCaseSensitive = SectionSearchCaseSensitive_;
      Settings_.NewSectionColor = NewSectionColor_;
      Settings_.MissingSectionColor = MissingSectionColor_;
      Settings_.ChangedSectionColor = ChangedSectionColor_;
      Settings_.ShowModulePanel = ShowModulePanel_;
      Settings_.SyncSelection = SyncSelection_;
      Settings_.SyncSourceFile = SyncSourceFile_;
      Settings_.ShowCallStackPopup = ShowCallStackPopup_;
      Settings_.CallStackPopupDuration = CallStackPopupDuration_;
      Settings_.ShowPerformanceCounterColumns = ShowPerformanceCounterColumns_;
      Settings_.ShowPerformanceMetricColumns = ShowPerformanceMetricColumns_;
      Settings_.AppendTimeToTotalColumn = AppendTimeToTotalColumn_;
      Settings_.AppendTimeToSelfColumn = AppendTimeToSelfColumn_;
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public UISectionSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new UISectionSettings();
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