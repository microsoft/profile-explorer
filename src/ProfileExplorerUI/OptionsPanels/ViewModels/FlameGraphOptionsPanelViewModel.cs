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
public partial class FlameGraphOptionsPanelViewModel : OptionsPanelBaseViewModel<FlameGraphSettings> {
  [ObservableProperty]
  FunctionDetailsPanelViewModel functionDetailsPanelViewModel_;

  [ObservableProperty]
  private bool showDetailsPanel_;

  [ObservableProperty]
  private bool syncSelection_;

  [ObservableProperty]
  private bool syncSourceFile_;

  [ObservableProperty]
  private bool prependModuleToFunction_;

  [ObservableProperty]
  private bool appendPercentageToFunction_;

  [ObservableProperty]
  private bool appendDurationToFunction_;

  [ObservableProperty]
  private bool showNodePopup_;

  [ObservableProperty]
  private int nodePopupDuration_;

  [ObservableProperty]
  private bool useCompactMode_;

  [ObservableProperty]
  private bool useKernelColorPalette_;

  [ObservableProperty]
  private bool useManagedColorPalette_;

  [ObservableProperty]
  private string defaultColorPalette_;

  [ObservableProperty]
  private string kernelColorPalette_;

  [ObservableProperty]
  private string managedColorPalette_;

  [ObservableProperty]
  private Color nodeTextColor_;

  [ObservableProperty]
  private Color kernelNodeTextColor_;

  [ObservableProperty]
  private Color managedNodeTextColor_;

  [ObservableProperty]
  private Color nodeModuleColor_;

  [ObservableProperty]
  private Color nodePercentageColor_;

  [ObservableProperty]
  private Color nodeWeightColor_;

  [ObservableProperty]
  private Color nodeBorderColor_;

  [ObservableProperty]
  private Color kernelNodeBorderColor_;

  [ObservableProperty]
  private Color managedNodeBorderColor_;

  [ObservableProperty]
  private Color selectedNodeColor_;

  [ObservableProperty]
  private Color selectedNodeBorderColor_;

  [ObservableProperty]
  private Color searchResultMarkingColor_;

  [ObservableProperty]
  private Color searchedNodeColor_;

  [ObservableProperty]
  private Color searchedNodeBorderColor_;

  public override void Initialize(FrameworkElement parent, FlameGraphSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);

    FunctionDetailsPanelViewModel_ = new FunctionDetailsPanelViewModel();
    FunctionDetailsPanelViewModel_.Initialize(parent, App.Settings.CallTreeNodeSettings, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(FlameGraphSettings settings) {
    ShowDetailsPanel_ = settings.ShowDetailsPanel;
    SyncSelection_ = settings.SyncSelection;
    SyncSourceFile_ = settings.SyncSourceFile;
    PrependModuleToFunction_ = settings.PrependModuleToFunction;
    AppendPercentageToFunction_ = settings.AppendPercentageToFunction;
    AppendDurationToFunction_ = settings.AppendDurationToFunction;
    ShowNodePopup_ = settings.ShowNodePopup;
    NodePopupDuration_ = settings.NodePopupDuration;

    UseCompactMode_ = settings.UseCompactMode;
    UseKernelColorPalette_ = settings.UseKernelColorPalette;
    UseManagedColorPalette_ = settings.UseManagedColorPalette;
    DefaultColorPalette_ = settings.DefaultColorPalette;
    KernelColorPalette_ = settings.KernelColorPalette;
    ManagedColorPalette_ = settings.ManagedColorPalette;
    NodeTextColor_ = settings.NodeTextColor;
    KernelNodeTextColor_ = settings.KernelNodeTextColor;
    ManagedNodeTextColor_ = settings.ManagedNodeTextColor;
    NodeModuleColor_ = settings.NodeModuleColor;
    NodePercentageColor_ = settings.NodePercentageColor;
    NodeWeightColor_ = settings.NodeWeightColor;
    NodeBorderColor_ = settings.NodeBorderColor;
    KernelNodeBorderColor_ = settings.KernelNodeBorderColor;
    ManagedNodeBorderColor_ = settings.ManagedNodeBorderColor;
    SelectedNodeColor_ = settings.SelectedNodeColor;
    SelectedNodeBorderColor_ = settings.SelectedNodeBorderColor;
    SearchResultMarkingColor_ = settings.SearchResultMarkingColor;
    SearchedNodeColor_ = settings.SearchedNodeColor;
    SearchedNodeBorderColor_ = settings.SearchedNodeBorderColor;
  }

  [RelayCommand]
  private void ResetNodePopupDuration() {
    NodePopupDuration_ = UISectionSettings.DefaultCallStackPopupDuration;
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
    if (FunctionDetailsPanelViewModel_ != null) {
      FunctionDetailsPanelViewModel_.SaveSettings();
    }

    if (Settings_ != null) {
      Settings_.ShowDetailsPanel = ShowDetailsPanel_;
      Settings_.SyncSelection = SyncSelection_;
      Settings_.SyncSourceFile = SyncSourceFile_;
      Settings_.PrependModuleToFunction = PrependModuleToFunction_;
      Settings_.AppendPercentageToFunction = AppendPercentageToFunction_;
      Settings_.AppendDurationToFunction = AppendDurationToFunction_;
      Settings_.ShowNodePopup = ShowNodePopup_;
      Settings_.NodePopupDuration = NodePopupDuration_;

      Settings_.UseCompactMode = UseCompactMode_;
      Settings_.UseKernelColorPalette = UseKernelColorPalette_;
      Settings_.UseManagedColorPalette = UseManagedColorPalette_;
      Settings_.DefaultColorPalette = DefaultColorPalette_;
      Settings_.KernelColorPalette = KernelColorPalette_;
      Settings_.ManagedColorPalette = ManagedColorPalette_;
      Settings_.NodeTextColor = NodeTextColor_;
      Settings_.KernelNodeTextColor = KernelNodeTextColor_;
      Settings_.ManagedNodeTextColor = ManagedNodeTextColor_;
      Settings_.NodeModuleColor = NodeModuleColor_;
      Settings_.NodePercentageColor = NodePercentageColor_;
      Settings_.NodeWeightColor = NodeWeightColor_;
      Settings_.NodeBorderColor = NodeBorderColor_;
      Settings_.KernelNodeBorderColor = KernelNodeBorderColor_;
      Settings_.ManagedNodeBorderColor = ManagedNodeBorderColor_;
      Settings_.SelectedNodeColor = SelectedNodeColor_;
      Settings_.SelectedNodeBorderColor = SelectedNodeBorderColor_;
      Settings_.SearchResultMarkingColor = SearchResultMarkingColor_;
      Settings_.SearchedNodeColor = SearchedNodeColor_;
      Settings_.SearchedNodeBorderColor = SearchedNodeBorderColor_;
    }
  }

  /// <summary>
  /// Gets the current settings with all changes applied
  /// </summary>
  public FlameGraphSettings GetCurrentSettings() {
    SaveSettings(); // Ensure all changes are saved to the settings object
    return Settings_;
  }

  /// <summary>
  /// Resets the settings to default values
  /// </summary>
  public void ResetSettings() {
    if (Settings_ != null) {
      // Reset to defaults - create a new default settings object
      var defaultSettings = new FlameGraphSettings();
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