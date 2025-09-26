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
public partial class TimelineOptionsPanelViewModel : OptionsPanelBaseViewModel<TimelineSettings> {
  [ObservableProperty]
  private bool syncSelection_;

  [ObservableProperty]
  private bool showCallStackPopup_;

  [ObservableProperty]
  private int callStackPopupDuration_;

  [ObservableProperty]
  private bool useThreadColors_;

  public override void Initialize(FrameworkElement parent, TimelineSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(TimelineSettings settings) {
    SyncSelection_ = settings.SyncSelection;
    ShowCallStackPopup_ = settings.ShowCallStackPopup;
    CallStackPopupDuration_ = settings.CallStackPopupDuration;
    UseThreadColors_ = settings.UseThreadColors;
  }

  [RelayCommand]
  private void ResetCallStackPopupDuration() {
    CallStackPopupDuration_ = TimelineSettings.DefaultCallStackPopupDuration;
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
      Settings_.SyncSelection = SyncSelection_;
      Settings_.ShowCallStackPopup = ShowCallStackPopup_;
      Settings_.CallStackPopupDuration = CallStackPopupDuration_;
      Settings_.UseThreadColors = UseThreadColors_;
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public TimelineSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new TimelineSettings();
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