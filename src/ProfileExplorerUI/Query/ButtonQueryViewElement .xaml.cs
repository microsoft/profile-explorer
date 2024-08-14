// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Controls;

namespace ProfileExplorer.UI.Query;

public partial class ButtonQueryViewElement : UserControl {
  public ButtonQueryViewElement() {
    InitializeComponent();
  }

  private void Button_Click(object sender, RoutedEventArgs e) {
    var button = (QueryButton)DataContext;
    button.Action?.Invoke(button, button.Data);
  }
}