// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using static ProfileExplorer.UI.ProfileDocumentMarkerSettings;

namespace ProfileExplorer.UI.OptionsPanels;

public partial class DocumentProfilingOptionsPanel : UserControl {
  private DocumentProfilingOptionsPanelViewModel viewModel_;

  // temp
  public bool ShowsDocumentSettings { get; set; } = true;

  public DocumentProfilingOptionsPanel() {
    InitializeComponent();
    viewModel_ = new DocumentProfilingOptionsPanelViewModel();
    DataContext = viewModel_;
  }
}