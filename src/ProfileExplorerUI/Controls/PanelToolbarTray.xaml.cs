// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProfileExplorer.UI;

public class PinEventArgs : EventArgs {
  public bool IsPinned { get; set; }
}

public class DuplicateEventArgs : EventArgs {
  public DuplicatePanelKind Kind { get; set; }
}

public class BindMenuItem {
  public string Header { get; set; }
  public string ToolTip { get; set; }
  public object Tag { get; set; }
  public bool IsChecked { get; set; }
}

public class BindMenuItemsArgs : EventArgs {
  public List<BindMenuItem> MenuItems;

  public BindMenuItemsArgs() {
    MenuItems = new List<BindMenuItem>();
  }
}

public partial class PanelToolbarTray : ToolBarTray {
  public static readonly DependencyProperty HasPinButtonProperty =
    DependencyProperty.Register("HasPinButton", typeof(bool), typeof(PanelToolbarTray),
                                new PropertyMetadata(true, OnHasPinButtonPropertyChanged));
  public static readonly DependencyProperty HasDuplicateButtonProperty =
    DependencyProperty.Register("HasDuplicateButton", typeof(bool), typeof(PanelToolbarTray),
                                new PropertyMetadata(true, OnHasDuplicateButtonPropertyChanged));
  public static readonly DependencyProperty HasHelpButtonProperty =
    DependencyProperty.Register("HasHelpButton", typeof(bool), typeof(PanelToolbarTray),
                                new PropertyMetadata(true, OnHasHelpButtonPropertyChanged));
  private bool registerLeftButtonDown_;

  public PanelToolbarTray() {
    InitializeComponent();
  }

  public event EventHandler<PinEventArgs> PinnedChanged;
  public event EventHandler<DuplicateEventArgs> DuplicateClicked;
  public event EventHandler SettingsClicked;
  public event EventHandler HelpClicked;
  public event EventHandler<BindMenuItemsArgs> BindMenuOpen;
  public event EventHandler<BindMenuItem> BindMenuItemSelected;

  public bool HasPinButton {
    get => (bool)GetValue(HasPinButtonProperty);
    set => SetValue(HasPinButtonProperty, value);
  }

  public bool HasDuplicateButton {
    get => (bool)GetValue(HasDuplicateButtonProperty);
    set => SetValue(HasDuplicateButtonProperty, value);
  }

  public bool HasHelpButton {
    get => (bool)GetValue(HasHelpButtonProperty);
    set => SetValue(HasHelpButtonProperty, value);
  }

  public bool IsPinned {
    get => PinButton.IsChecked.HasValue && PinButton.IsChecked.Value;
    set => PinButton.IsChecked = value;
  }

  private static void OnHasPinButtonPropertyChanged(DependencyObject d,
                                                    DependencyPropertyChangedEventArgs e) {
    var source = d as PanelToolbarTray;
    bool visible = (bool)e.NewValue;
    source.PinButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
  }

  private static void OnHasDuplicateButtonPropertyChanged(DependencyObject d,
                                                          DependencyPropertyChangedEventArgs e) {
    var source = d as PanelToolbarTray;
    bool visible = (bool)e.NewValue;
    source.DuplicateButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
  }

  private static void OnHasHelpButtonPropertyChanged(DependencyObject d,
                                                     DependencyPropertyChangedEventArgs e) {
    var source = d as PanelToolbarTray;
    bool visible = (bool)e.NewValue;
    source.HelpButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.RemoveToolbarOverflowButton(sender as ToolBar);
  }

  private void PinButton_Checked(object sender, RoutedEventArgs e) {
    PinnedChanged?.Invoke(this, new PinEventArgs {IsPinned = true});
  }

  private void PinButton_Unchecked(object sender, RoutedEventArgs e) {
    PinnedChanged?.Invoke(this, new PinEventArgs {IsPinned = false});
  }

  private void SettingsButton_Click(object sender, RoutedEventArgs e) {
    // If popup was active when the click started, ignore it since
    // the user most likely wants to close the popup panel.
    if (!registerLeftButtonDown_) {
      registerLeftButtonDown_ = false;
      return;
    }

    registerLeftButtonDown_ = false;
    SettingsClicked?.Invoke(this, EventArgs.Empty);
  }

  private void HelpButton_Click(object sender, RoutedEventArgs e) {
    // If popup was active when the click started, ignore it since
    // the user most likely wants to close the popup panel.
    if (!registerLeftButtonDown_) {
      registerLeftButtonDown_ = false;
      return;
    }

    registerLeftButtonDown_ = false;
    HelpClicked?.Invoke(this, EventArgs.Empty);
  }

  private void DuplicateMenu_Click(object sender, RoutedEventArgs e) {
    DuplicateClicked?.Invoke(this, new DuplicateEventArgs {Kind = DuplicatePanelKind.SameSet});
  }

  private void DuplicateLeftMenu_Click(object sender, RoutedEventArgs e) {
    DuplicateClicked?.Invoke(
      this, new DuplicateEventArgs {Kind = DuplicatePanelKind.NewSetDockedLeft});
  }

  private void DuplicateRightMenu_Click(object sender, RoutedEventArgs e) {
    DuplicateClicked?.Invoke(
      this, new DuplicateEventArgs {Kind = DuplicatePanelKind.NewSetDockedRight});
  }

  private void DuplicateFloatingMenu_Click(object sender, RoutedEventArgs e) {
    DuplicateClicked?.Invoke(this, new DuplicateEventArgs {Kind = DuplicatePanelKind.Floating});
  }

  private void SettingsButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    // This is a workaround for the way clicks on the options icon are handled by WPF
    // when the popup panel is active. The user most likely wants to close the popup
    // by clicking again on the icon, but instead the popup closes and immediately opens again.
    //
    // - When the button is clicked, it Opens the popup.
    // - When the button is clicked again, the button raises the MouseDown event
    //   and the Popup closes on that event.
    // - Afterwards the Clicked event is raised, but since the Popup is already closed,
    //   it will open it again, thus causing for the Popup to be closed & opened immediately.
    //
    // The MouseLeftButtonDown is not triggered when the popup is active.
    registerLeftButtonDown_ = true;
  }

  private void ToolBar_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) {
    Utils.RemoveToolbarOverflowButton(sender as ToolBar);
  }

  private void ToolBar_OnSizeChanged(object sender, SizeChangedEventArgs e) {
    Utils.RemoveToolbarOverflowButton(sender as ToolBar);
  }
}