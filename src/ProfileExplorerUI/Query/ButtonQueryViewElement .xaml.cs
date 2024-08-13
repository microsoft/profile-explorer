// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
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