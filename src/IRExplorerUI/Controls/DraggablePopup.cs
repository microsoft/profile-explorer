// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using IRExplorerUI.Utilities;

namespace IRExplorerUI.Controls;

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
    new RelayCommand<SelectedColorEventArgs>(async e => {
      SetPanelAccentColor(e.SelectedColor);
    });

  protected virtual void SetPanelAccentColor(Color color) {
  }

  public event EventHandler PopupClosed;
  public event EventHandler PopupDetached;
  public Thumb Thumb { get; private set; } = new Thumb {Width = 0, Height = 0};

  public bool IsAlwaysOnTop {
    get => isAlwaysOnTop_;
    set {
      isAlwaysOnTop_ = value;
      UpdateAlwaysOnTop(value);
    }
  }

  public IntPtr PopupHandle {
    get {
      var source = PresentationSource.FromVisual(Child) as HwndSource;
      return source?.Handle ?? IntPtr.Zero;
    }
  }

  public bool IsDetached { get; private set; }

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

    if (referenceElement != null) {
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
    if (NativeMethods.GetWindowRect(PopupHandle, out var rect)) {
      NativeMethods.SetWindowPos(PopupHandle, NativeMethods.HWND_TOP,
                                 rect.Left, rect.Top, (int)Width, (int)Height,
                                 NativeMethods.TOPMOST_FLAGS);
    }
  }

  public void UpdateAlwaysOnTop(bool value) {
    if (NativeMethods.GetWindowRect(PopupHandle, out var rect)) {
      NativeMethods.SetWindowPos(PopupHandle,
                                 value ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                                 rect.Left, rect.Top, (int)Width, (int)Height,
                                 NativeMethods.TOPMOST_FLAGS);
    }
  }

  public void SendToBack() {
    if (IsAlwaysOnTop) {
      return;
    }

    if (NativeMethods.GetWindowRect(PopupHandle, out var rect)) {
      NativeMethods.SetWindowPos(PopupHandle, NativeMethods.HWND_NOTOPMOST,
                                 rect.Left, rect.Top, (int)Width, (int)Height,
                                 NativeMethods.TOPMOST_FLAGS);
    }
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