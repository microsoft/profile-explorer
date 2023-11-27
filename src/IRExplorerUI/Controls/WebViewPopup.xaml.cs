// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.Controls;

public partial class WebViewPopup : DraggablePopup, INotifyPropertyChanged {
  private string panelTitle_;

  public WebViewPopup(Point position, double width, double height,
                      UIElement referenceElement) {
    InitializeComponent();
    Initialize(position, width, height, referenceElement);
    PanelResizeGrip.ResizedControl = this;
    DataContext = this;
  }

  public event PropertyChangedEventHandler PropertyChanged;

  public string PanelTitle {
    get => panelTitle_;
    set {
      if (panelTitle_ != value) {
        panelTitle_ = value;
        OnPropertyChange(nameof(PanelTitle));
      }
    }
  }

  public override bool ShouldStartDragging(MouseButtonEventArgs e) {
    if (e.LeftButton == MouseButtonState.Pressed && ToolbarPanel.IsMouseOver) {
      if (!IsDetached) {
        DetachPopup();
      }

      return true;
    }

    return false;
  }

  private void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    ClosePopup();
  }
}
