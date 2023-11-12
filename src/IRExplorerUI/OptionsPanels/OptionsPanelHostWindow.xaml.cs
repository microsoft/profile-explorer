﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Controls;
using IRExplorerUI.Controls;

namespace IRExplorerUI.OptionsPanels;

public partial class OptionsPanelHostWindow : DraggablePopup, IOptionsPanel {
  private bool closed_;
  private IOptionsPanel optionsPanel_;

  public OptionsPanelHostWindow(UserControl panel, Point position,
                                double width, double height,
                                UIElement referenceElement,
                                bool showResetButton = true) {
    InitializeComponent();

    // Offset to account for drop shadow margin.
    position.Offset(6, 0);
    Initialize(position, width, height, referenceElement);

    PanelResizeGrip.ResizedControl = this;
    ShowResetButton = showResetButton;
    DataContext = this;

    optionsPanel_ = (IOptionsPanel)panel;
    optionsPanel_.PanelClosed += SettingsPanel_PanelClosed;
    optionsPanel_.PanelReset += SettingsPanel_PanelReset;
    optionsPanel_.SettingsChanged += SettingsPanel_SettingsChanged;
    optionsPanel_.StayOpenChanged += OptionsPanel_StayOpenChanged;
    PanelHost.Content = panel;
  }

  public event EventHandler PanelClosed;
  public event EventHandler PanelReset;
  public event EventHandler SettingsChanged;
  public event EventHandler<bool> StayOpenChanged;
  public bool ShowResetButton { get; set; }

  public object Settings {
    get => optionsPanel_.Settings;
    set => optionsPanel_.Settings = value;
  }

  public void Initialize(FrameworkElement parent) {
  }

  public void PanelClosing() { }
  public void PanelResetting() { }
  public void PanelResetted() { }

  protected override void OnOpened(EventArgs e) {
    base.OnOpened(e);
    optionsPanel_.Initialize(this);
  }

  protected override void OnClosed(EventArgs e) {
    base.OnClosed(e);

    optionsPanel_.PanelClosed -= SettingsPanel_PanelClosed;
    optionsPanel_.PanelReset -= SettingsPanel_PanelReset;
    optionsPanel_.SettingsChanged -= SettingsPanel_SettingsChanged;

    if (!closed_) {
      closed_ = true;
      PanelClosed?.Invoke(this, e);
    }
  }

  private void SettingsPanel_SettingsChanged(object sender, EventArgs e) {
    SettingsChanged?.Invoke(this, e);
  }

  private void SettingsPanel_PanelReset(object sender, EventArgs e) {
    PanelReset?.Invoke(this, e);
  }

  private void SettingsPanel_PanelClosed(object sender, EventArgs e) {
    closed_ = true;
    PanelClosed?.Invoke(this, e);
  }

  private void OptionsPanel_StayOpenChanged(object sender, bool staysOpen) {
    StaysOpen = staysOpen;
  }

  private void ResetButton_Click(object sender, RoutedEventArgs e) {
    using var centerForm = new DialogCenteringHelper(this);

    if (MessageBox.Show("Do you want to reset all settings?", "IR Explorer",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
      return;
    }

    optionsPanel_.PanelResetting();
    PanelReset?.Invoke(this, e);
    optionsPanel_.PanelResetted();
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    closed_ = true;
    optionsPanel_.PanelClosing();
    PanelClosed?.Invoke(this, e);
  }
}
