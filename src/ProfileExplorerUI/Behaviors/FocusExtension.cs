// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Controls;

namespace ProfileExplorer.UI.Behaviors;

/// <summary>
/// Attached property to enable setting focus on a TextBox from the view model
/// </summary>
public static class FocusExtension {
  public static readonly DependencyProperty IsFocusedProperty =
    DependencyProperty.RegisterAttached(
      "IsFocused",
      typeof(bool),
      typeof(FocusExtension),
      new UIPropertyMetadata(false, OnIsFocusedPropertyChanged));

  public static bool GetIsFocused(DependencyObject obj) {
    return (bool)obj.GetValue(IsFocusedProperty);
  }

  public static void SetIsFocused(DependencyObject obj, bool value) {
    obj.SetValue(IsFocusedProperty, value);
  }

  private static void OnIsFocusedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
    if (d is TextBox textBox && (bool)e.NewValue) {
      // Use Dispatcher to ensure the TextBox is ready for focus
      textBox.Dispatcher.BeginInvoke(new System.Action(() => {
        textBox.Focus();
        textBox.SelectAll();
        
        // Clear the IsFocused property after focusing to allow for subsequent focus operations
        SetIsFocused(textBox, false);
      }), System.Windows.Threading.DispatcherPriority.Input);
    }
  }
}