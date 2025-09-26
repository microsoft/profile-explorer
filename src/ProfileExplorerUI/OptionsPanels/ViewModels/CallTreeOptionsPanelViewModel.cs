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
public partial class CallTreeOptionsPanelViewModel : OptionsPanelBaseViewModel<CallTreeSettings> {
  [ObservableProperty]
  private bool syncSelection_;

  [ObservableProperty]
  private bool syncSourceFile_;

  [ObservableProperty]
  private bool expandHottestPath_;

  [ObservableProperty]
  private bool prependModuleToFunction_;

  [ObservableProperty]
  private bool showNodePopup_;

  [ObservableProperty]
  private int nodePopupDuration_;

  public override void Initialize(FrameworkElement parent, CallTreeSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(CallTreeSettings settings) {
    SyncSelection_ = settings.SyncSelection;
    SyncSourceFile_ = settings.SyncSourceFile;
    ExpandHottestPath_ = settings.ExpandHottestPath;
    PrependModuleToFunction_ = settings.PrependModuleToFunction;
    ShowNodePopup_ = settings.ShowNodePopup;
    NodePopupDuration_ = settings.NodePopupDuration;
  }

  [RelayCommand]
  private void ResetNodePopupDuration() {
    NodePopupDuration_ = CallTreeSettings.DefaultNodePopupDuration;
  }

  [RelayCommand]
  private void SetShortNodePopupDuration() {
    NodePopupDuration_ = HoverPreview.HoverDurationMs;
  }

  [RelayCommand]
  private void SetLongNodePopupDuration() {
    NodePopupDuration_ = HoverPreview.LongHoverDurationMs;
  }

  public override void SaveSettings() {
    if (Settings_ != null) {
      Settings_.SyncSelection = SyncSelection_;
      Settings_.SyncSourceFile = SyncSourceFile_;
      Settings_.ExpandHottestPath = ExpandHottestPath_;
      Settings_.PrependModuleToFunction = PrependModuleToFunction_;
      Settings_.ShowNodePopup = ShowNodePopup_;
      Settings_.NodePopupDuration = NodePopupDuration_;
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public CallTreeSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new CallTreeSettings();
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