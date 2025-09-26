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
public partial class PreviewPopupOptionsPanelViewModel : OptionsPanelBaseViewModel<PreviewPopupSettings> {
  [ObservableProperty]
  private bool jumpToHottestElement_;

  [ObservableProperty]
  private bool useCompactProfilingColumns_;

  [ObservableProperty]
  private bool showPerformanceCounterColumns_;

  [ObservableProperty]
  private bool showPerformanceMetricColumns_;

  [ObservableProperty]
  private bool useSmallerFontSize_;

  [ObservableProperty]
  private bool showSourcePreviewPopup_;

  public override void Initialize(FrameworkElement parent, PreviewPopupSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(PreviewPopupSettings settings) {
    JumpToHottestElement_ = settings.JumpToHottestElement;
    UseCompactProfilingColumns_ = settings.UseCompactProfilingColumns;
    ShowPerformanceCounterColumns_ = settings.ShowPerformanceCounterColumns;
    ShowPerformanceMetricColumns_ = settings.ShowPerformanceMetricColumns;
    UseSmallerFontSize_ = settings.UseSmallerFontSize;
    ShowSourcePreviewPopup_ = settings.ShowSourcePreviewPopup;
  }

  public override void SaveSettings() {
    if (Settings_ != null) {
      Settings_.JumpToHottestElement = JumpToHottestElement_;
      Settings_.UseCompactProfilingColumns = UseCompactProfilingColumns_;
      Settings_.ShowPerformanceCounterColumns = ShowPerformanceCounterColumns_;
      Settings_.ShowPerformanceMetricColumns = ShowPerformanceMetricColumns_;
      Settings_.UseSmallerFontSize = UseSmallerFontSize_;
      Settings_.ShowSourcePreviewPopup = ShowSourcePreviewPopup_;
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public PreviewPopupSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new PreviewPopupSettings();
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