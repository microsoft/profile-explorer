﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ProfileExplorer.UI.Controls;

public class DraggablePopup : Popup {
  private bool duringMinimize_;
  private bool isAlwaysOnTop_;

  public DraggablePopup() {
    MouseDown += (sender, e) => {
      if (ShouldStartDragging(e)) {
        Thumb.RaiseEvent(e);
      }
    };

    Thumb.DragDelta += (sender, e) => {
      HorizontalOffset += e.HorizontalChange;
      VerticalOffset += e.VerticalChange;
    };

    // Bring popup over other popups on click anywhere inside it.
    PreviewMouseDown += (sender, args) => BringToFront();
  }

  public RelayCommand<SelectedColorEventArgs> PopupColorSelectedCommand =>
    new(async e => {
      SetPanelAccentColor(e.SelectedColor);
    });
  public Thumb Thumb { get; private set; } = new() {Width = 0, Height = 0};

  public bool IsAlwaysOnTop {
    get => isAlwaysOnTop_;
    set {
      isAlwaysOnTop_ = value;
      UpdateAlwaysOnTop(value);
    }
  }

  public bool IsDetached { get; private set; }

  protected virtual void SetPanelAccentColor(Color color) {
  }

  public event EventHandler PopupClosed;
  public event EventHandler PopupDetached;

  public virtual bool ShouldStartDragging(MouseButtonEventArgs e) {
    return e.LeftButton == MouseButtonState.Pressed;
  }

  public virtual void DetachPopup() {
    IsDetached = true;
    StaysOpen = true;
    PopupDetached?.Invoke(this, null);
  }

  public virtual void ShowPopup() {
    IsOpen = true;
  }

  public virtual void PopupOpened() {
  }

  protected override void OnOpened(EventArgs e) {
    base.OnOpened(e);
    PopupOpened();
  }

  public virtual void ClosePopup() {
    IsOpen = false;
    PopupClosed?.Invoke(this, null);
  }

  public void UpdatePosition(Point position, UIElement referenceElement) {
    // Due to various DPI settings, the Window coordinates needs
    // some adjustment of the values based on the monitor.
    var screenPosition = position;

    if (referenceElement != null &&
        PresentationSource.FromVisual(referenceElement) != null) {
      screenPosition = referenceElement.PointToScreen(position);
      screenPosition = Utils.CoordinatesToScreen(screenPosition, referenceElement);
    }

    HorizontalOffset = screenPosition.X;
    VerticalOffset = screenPosition.Y;
  }

  public void UpdateSize(double width, double height) {
    Width = width;
    Height = height;
  }

  public void Initialize(Point position, double width, double height,
                         UIElement referenceElement) {
    UpdatePosition(position, referenceElement);
    UpdateSize(width, height);
  }

  public void Initialize(Point position, UIElement referenceElement) {
    UpdatePosition(position, referenceElement);
  }

  public void BringToFront() {
    Utils.BringToFront(Child, Width, Height);
  }

  public void UpdateAlwaysOnTop(bool value) {
    Utils.SetAlwaysOnTop(Child, value, Width, Height);
  }

  public void SendToBack() {
    if (IsAlwaysOnTop) {
      return;
    }

    Utils.SendToBack(Child, Width, Height);
  }

  public void Minimize() {
    duringMinimize_ = true;
    IsOpen = false;
  }

  public void Restore() {
    IsOpen = true;
  }

  protected override void OnClosed(EventArgs e) {
    base.OnClosed(e);

    // When the application is minimized, the panel should be just hidden,
    // not completely closed.
    if (duringMinimize_) {
      duringMinimize_ = false;
      return;
    }

    PopupClosed?.Invoke(this, e);
  }

  protected override void OnPreviewKeyDown(KeyEventArgs e) {
    base.OnPreviewKeyDown(e);

    if (e.Key == Key.Escape) {
      ClosePopup();
      e.Handled = true;
    }
  }

  protected override void OnInitialized(EventArgs e) {
    base.OnInitialized(e);

    RemoveLogicalChild(Child);
    var surrogateChild = new Grid();
    surrogateChild.Children.Add(Thumb);
    surrogateChild.Children.Add(Child);
    AddLogicalChild(surrogateChild);
    Child = surrogateChild;
  }
}