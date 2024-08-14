// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ProfileExplorer.UI;

public sealed class SelectedIconEventArgs : EventArgs {
  public string SelectedIconName { get; set; }
}

public partial class IconSelector : UserControl {
  public static readonly DependencyProperty CommandTargetProperty =
    DependencyProperty.Register("CommandTarget", typeof(IInputElement), typeof(IconSelector),
                                new UIPropertyMetadata(null));
  public static DependencyProperty IconSelectedCommandProperty =
    DependencyProperty.Register("IconSelectedCommand", typeof(ICommand), typeof(IconSelector));
  private static readonly Color[] ButtonIcons;

  public IconSelector() {
    InitializeComponent();
    Focusable = true;
    Loaded += IconSelector_Loaded;
    DataContext = this;
  }

  public event EventHandler<SelectedIconEventArgs> IconSelected;
  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;

  public ICommand IconSelectedCommand {
    get => (ICommand)GetValue(IconSelectedCommandProperty);
    set => SetValue(IconSelectedCommandProperty, value);
  }

  public IInputElement CommandTarget {
    get => (IInputElement)GetValue(CommandTargetProperty);
    set => SetValue(CommandTargetProperty, value);
  }

  private void IconSelector_Loaded(object sender, RoutedEventArgs e) {
    Focus();
  }

  private void RaiseSelectedIconEvent(string iconName) {
    if (IconSelectedCommand == null && IconSelected == null) {
      return;
    }

    var parentHost = Utils.FindParentHost(this);

    if (parentHost != null) {
      parentHost.Focus();
    }

    var args = new SelectedIconEventArgs {
      SelectedIconName = iconName
    };

    if (IconSelectedCommand != null) {
      if (IconSelectedCommand.CanExecute(args)) {
        IconSelectedCommand.Execute(args);
      }
    }
    else {
      IconSelected?.Invoke(this, args);
    }
  }

  private UIElement FindParentHost() {
    var logicalRoot = LogicalTreeHelper.GetParent(this);

    while (logicalRoot != null) {
      if (logicalRoot is UserControl || logicalRoot is Window) {
        break;
      }

      logicalRoot = LogicalTreeHelper.GetParent(logicalRoot);
    }

    return logicalRoot as UIElement;
  }

  private void CloseParentMenu() {
    // Close the context menu hosting the control.
    var logicalRoot = LogicalTreeHelper.GetParent(this);

    while (logicalRoot != null) {
      if (logicalRoot is ContextMenu menu) {
        menu.IsOpen = false;
        break;
      }

      if (logicalRoot is Popup popup) {
        popup.IsOpen = false;
        break;
      }

      logicalRoot = LogicalTreeHelper.GetParent(logicalRoot);
    }
  }

  private void Button_MouseUp(object sender, MouseButtonEventArgs e) {
    var button = sender as Button;
    RaiseSelectedIconEvent((string)button.Tag);
    Utils.CloseParentMenu(this);
    e.Handled = true;
  }
}