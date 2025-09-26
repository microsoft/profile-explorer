// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using ProfileExplorer.UI.Compilers;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class SymbolOptionsPanel : UserControl {
  private SymbolOptionsPanelViewModel viewModel_;

  public SymbolOptionsPanel() {
    InitializeComponent();
    viewModel_ = new SymbolOptionsPanelViewModel();
    DataContext = viewModel_;
  }

  public void Initialize(FrameworkElement parent, SettingsBase settings, IUISession session) {
    viewModel_.Initialize(parent, (SymbolFileSourceSettings)settings, session);
  }
  
  public void SaveSettings() {
    viewModel_.SaveSettings();
  }
}