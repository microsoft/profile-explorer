// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class GeneralOptionsPanelViewModel : OptionsPanelBaseViewModel<GeneralSettings> {
  [ObservableProperty]
  private bool checkForUpdates_;

  [ObservableProperty]
  private double windowScaling_;

  [ObservableProperty]
  private bool disableAnimations_;

  [ObservableProperty]
  private bool disableHardwareRendering_;

  [ObservableProperty]
  private double cpuCoreLimit_;

  public override void Initialize(FrameworkElement parent, GeneralSettings settings, IUISession session) {
    base.Initialize(parent, settings, session);
    
    // Initialize observable properties from GeneralSettings
    CheckForUpdates_ = settings.CheckForUpdates;
    WindowScaling_ = settings.WindowScaling;
    DisableAnimations_ = settings.DisableAnimations;
    DisableHardwareRendering_ = settings.DisableHardwareRendering;
    CpuCoreLimit_ = settings.CpuCoreLimit;
  }

  [RelayCommand]
  private void ResetUIZoom() {
    WindowScaling_ = 1.0;
  }

  [RelayCommand]
  private void ResetCpuCoreLimit() {
    CpuCoreLimit_ = GeneralSettings.DefaultCpuCoreLimit;
  }
  
  public override void SaveSettings() {
    if (Settings_ != null) {
      Settings_.CheckForUpdates = CheckForUpdates_;
      Settings_.WindowScaling = WindowScaling_;
      Settings_.DisableAnimations = DisableAnimations_;
      Settings_.DisableHardwareRendering = DisableHardwareRendering_;
      Settings_.CpuCoreLimit = CpuCoreLimit_;
    }
  }
}