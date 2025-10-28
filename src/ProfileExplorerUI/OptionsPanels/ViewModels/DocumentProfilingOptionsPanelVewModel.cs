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
using ProfileExplorer.Core.Settings;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Services;
using ProfileExplorer.UI.Windows;
using static ProfileExplorer.UI.ProfileDocumentMarkerSettings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class DocumentProfilingOptionsPanelViewModel : OptionsPanelBaseViewModel<ProfileDocumentMarkerSettings> {
  [ObservableProperty]
  private bool showsDocumentSettings_;

  [ObservableProperty]
  private bool jumpToHottestElement_;

  [ObservableProperty]
  private bool markElements_;

  [ObservableProperty]
  private bool markBlocks_;

  [ObservableProperty]
  private bool markBlocksInFlowGraph_;

  [ObservableProperty]
  private bool markCallTargets_;

  [ObservableProperty]
  private bool displayIcons_;

  [ObservableProperty]
  private bool displayPercentageBar_;

  [ObservableProperty]
  private int maxPercentageBarWidth_;

  [ObservableProperty]
  private bool appendValueUnitSuffix_;

  [ObservableProperty]
  private ObservableCollection<ValueUnitKind> valueUnitOptions_;

  [ObservableProperty]
  private ValueUnitKind selectedValueUnit_;

  [ObservableProperty]
  private double elementWeightCutoff_;

  [ObservableProperty]
  private Color columnTextColor_;

  [ObservableProperty]
  private Color percentageBarBackColor_;

  [ObservableProperty]
  private Color performanceCounterBackColor_;

  [ObservableProperty]
  private Color performanceMetricBackColor_;

  [ObservableProperty]
  private Color blockOverlayTextColor_;

  [ObservableProperty]
  private Color hotBlockOverlayTextColor_;

  [ObservableProperty]
  private Color blockOverlayBorderColor_;

  public override void Initialize(FrameworkElement parent, ProfileDocumentMarkerSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    ValueUnitOptions_ = new() {
      ValueUnitKind.Second,
      ValueUnitKind.Millisecond,
      ValueUnitKind.Microsecond,
      ValueUnitKind.Nanosecond
    };

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(ProfileDocumentMarkerSettings settings) {
    JumpToHottestElement_ = settings.JumpToHottestElement;
    MarkElements_ = settings.MarkElements;
    MarkBlocks_ = settings.MarkBlocks;
    MarkBlocksInFlowGraph_ = settings.MarkBlocksInFlowGraph;
    MarkCallTargets_ = settings.MarkCallTargets;
    DisplayIcons_ = settings.DisplayIcons;
    DisplayPercentageBar_ = settings.DisplayPercentageBar;
    MaxPercentageBarWidth_ = settings.MaxPercentageBarWidth;
    AppendValueUnitSuffix_ = settings.AppendValueUnitSuffix;
    SelectedValueUnit_ = settings.ValueUnit;
    ElementWeightCutoff_ = settings.ElementWeightCutoff;
    ColumnTextColor_ = settings.ColumnTextColor;
    PercentageBarBackColor_ = settings.PercentageBarBackColor;
    PerformanceCounterBackColor_ = settings.PerformanceCounterBackColor;
    PerformanceMetricBackColor_ = settings.PerformanceMetricBackColor;
    BlockOverlayTextColor_ = settings.BlockOverlayTextColor;
    HotBlockOverlayTextColor_ = settings.HotBlockOverlayTextColor;
    BlockOverlayBorderColor_ = settings.BlockOverlayBorderColor;
  }

  [RelayCommand]
  private void ResetMaxWidth() {
    MaxPercentageBarWidth_ = ProfileDocumentMarkerSettings.DefaultMaxPercentageBarWidth;
  }

  [RelayCommand]
  private void ResetWeightCutoff() {
    ElementWeightCutoff_ = ProfileDocumentMarkerSettings.DefaultElementWeightCutoff;
  }

  public override void SaveSettings() {
    if (Settings_ != null) {
      Settings_.JumpToHottestElement = JumpToHottestElement_;
      Settings_.MarkElements = MarkElements_;
      Settings_.MarkBlocks = MarkBlocks_;
      Settings_.MarkBlocksInFlowGraph = MarkBlocksInFlowGraph_;
      Settings_.MarkCallTargets = MarkCallTargets_;
      Settings_.DisplayIcons = DisplayIcons_;
      Settings_.DisplayPercentageBar = DisplayPercentageBar_;
      Settings_.MaxPercentageBarWidth = MaxPercentageBarWidth_;
      Settings_.AppendValueUnitSuffix = AppendValueUnitSuffix_;
      Settings_.ValueUnit = SelectedValueUnit_;
      Settings_.ElementWeightCutoff = ElementWeightCutoff_;
      Settings_.ColumnTextColor = ColumnTextColor_;
      Settings_.PercentageBarBackColor = PercentageBarBackColor_;
      Settings_.PerformanceCounterBackColor = PerformanceCounterBackColor_;
      Settings_.PerformanceMetricBackColor = PerformanceMetricBackColor_;
      Settings_.BlockOverlayTextColor = BlockOverlayTextColor_;
      Settings_.HotBlockOverlayTextColor = HotBlockOverlayTextColor_;
      Settings_.BlockOverlayBorderColor = BlockOverlayBorderColor_;
    }
  }
}