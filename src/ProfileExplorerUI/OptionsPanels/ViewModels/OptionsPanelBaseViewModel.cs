// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class OptionsPanelBaseViewModel<TSettings> : ObservableObject where TSettings : SettingsBase  {
  protected FrameworkElement parent_;
  protected IUISession session_;

  [ObservableProperty]
  protected TSettings settings_;

  public virtual void Initialize(FrameworkElement parent, TSettings settings, IUISession session) {
    parent_ = parent;
    settings_ = settings;
    session_ = session;
  }

  public virtual void SaveSettings() {
    // Base implementation does nothing
  }
}