// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ProfileExplorer.UI.Controls;

public partial class WebViewPopup : DraggablePopup, INotifyPropertyChanged {
  private string panelTitle_;

  public WebViewPopup(Point position, double width, double height,
                      UIElement referenceElement) {
    InitializeComponent();
    Initialize(position, width, height, referenceElement);
    PanelResizeGrip.ResizedControl = this;
    DataContext = this;
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;

  public string PanelTitle {
    get => panelTitle_;
    set {
      if (panelTitle_ != value) {
        panelTitle_ = value;
        OnPropertyChange(nameof(PanelTitle));
      }
    }
  }

  public event PropertyChangedEventHandler PropertyChanged;

  public async Task NavigateToURL(string url) {
    Browser.Source = new Uri(url);
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