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

public partial class FunctionDetailsPanelViewModel : OptionsPanelBaseViewModel<CallTreeNodeSettings> {
  [ObservableProperty]
  FunctionListOptionsPanelViewModel functionListOptionsPanelViewModel_;

  [ObservableProperty]
  private bool expandInstances_;

  [ObservableProperty]
  private bool expandHistogram_;

  [ObservableProperty]
  private bool expandThreads_;

  [ObservableProperty]
  private bool alternateListRows_;

  [ObservableProperty]
  private bool showPreviewPopup_;

  [ObservableProperty]
  private int previewPopupDuration_;

  [ObservableProperty]
  private Color histogramBarColor_;

  [ObservableProperty]
  private Color histogramAverageColor_;

  [ObservableProperty]
  private Color histogramMedianColor_;

  [ObservableProperty]
  private Color histogramCurrentColor_;

  public override void Initialize(FrameworkElement parent, CallTreeNodeSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    FunctionListOptionsPanelViewModel_ = new FunctionListOptionsPanelViewModel();
    FunctionListOptionsPanelViewModel_.Initialize(parent, settings.FunctionListViewFilter, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(CallTreeNodeSettings settings) {
    ExpandInstances_ = settings.ExpandInstances;
    ExpandHistogram_ = settings.ExpandHistogram;
    ExpandThreads_ = settings.ExpandThreads;
    AlternateListRows_ = settings.AlternateListRows;
    ShowPreviewPopup_ = settings.ShowPreviewPopup;
    PreviewPopupDuration_ = settings.PreviewPopupDuration;
    HistogramBarColor_ = settings.HistogramBarColor;
    HistogramAverageColor_ = settings.HistogramAverageColor;
    HistogramMedianColor_ = settings.HistogramMedianColor;
    HistogramCurrentColor_ = settings.HistogramCurrentColor;
  }

  [RelayCommand]
  private void ResetDetailsPopupDuration() {
    PreviewPopupDuration_ = CallTreeNodeSettings.DefaultPreviewPopupDuration;
  }

  [RelayCommand]
  private void SetShortDetailsPopupDuration() {
    PreviewPopupDuration_ = HoverPreview.HoverDurationMs;
  }

  [RelayCommand]
  private void SetLongDetailsPopupDuration() {
    PreviewPopupDuration_ = HoverPreview.LongHoverDurationMs;
  }

  public override void SaveSettings() {
    if (FunctionListOptionsPanelViewModel_ != null) {
      FunctionListOptionsPanelViewModel_.SaveSettings();
    }

    if (Settings_ != null) {
      Settings_.ExpandInstances = ExpandInstances_;
      Settings_.ExpandHistogram = ExpandHistogram_;
      Settings_.ExpandThreads = ExpandThreads_;
      Settings_.AlternateListRows = AlternateListRows_;
      Settings_.ShowPreviewPopup = ShowPreviewPopup_;
      Settings_.PreviewPopupDuration = PreviewPopupDuration_;
      Settings_.HistogramBarColor = HistogramBarColor_;
      Settings_.HistogramAverageColor = HistogramAverageColor_;
      Settings_.HistogramMedianColor = HistogramMedianColor_;
      Settings_.HistogramCurrentColor = HistogramCurrentColor_;
    }
  }
}