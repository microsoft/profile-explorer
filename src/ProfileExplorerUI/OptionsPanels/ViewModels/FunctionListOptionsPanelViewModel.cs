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
public partial class FunctionListOptionsPanelViewModel : OptionsPanelBaseViewModel<ProfileListViewFilter> {
  [ObservableProperty]
  private bool filterByWeight_;

  [ObservableProperty]
  private double minWeight_;

  [ObservableProperty]
  private bool sortByExclusiveTime_;

  public override void Initialize(FrameworkElement parent, ProfileListViewFilter settings, IUISession session) {
    base.Initialize(parent, settings, session);

    // Populate view model properties from settings
    PopulateFromSettings(settings);
  }

  private void PopulateFromSettings(ProfileListViewFilter settings) {
    FilterByWeight_ = settings.FilterByWeight;
    MinWeight_ = settings.MinWeight;
    SortByExclusiveTime_ = settings.SortByExclusiveTime;
  }

  [RelayCommand]
  private void ResetFilterWeight() {
    MinWeight_ = ProfileListViewFilter.DefaultMinWeight;
  }

  public override void SaveSettings() {
    if (Settings_ != null) {
      Settings_.FilterByWeight = FilterByWeight_;
      Settings_.MinWeight = MinWeight_;
      Settings_.SortByExclusiveTime = SortByExclusiveTime_;
    }
  }
}