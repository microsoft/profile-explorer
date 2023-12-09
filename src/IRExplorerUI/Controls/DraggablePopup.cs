// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace IRExplorerUI.Controls;

public class DraggablePopup : Popup {
  private bool duringMinimize_;
  private bool isDetached_;
  private Popup colorPopup_;
  private Button colorButton_;
  private Brush colorPopupBackground_;

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
  }

  private void HideColorSelector() {
    if (colorPopup_ != null) {
      colorPopup_.IsOpen = false;
      colorPopup_ = null;
    }
  }

  protected void RegisterColorButton(Button button, Brush colorPopupBackground) {
    colorButton_ = button;
    colorPopupBackground_ = colorPopupBackground;
    colorButton_.Click += ColorButton_Click;
  }

  private void ColorButton_Click(object sender, RoutedEventArgs e) {
    var colorSelector = new ColorSelector();
    colorSelector.BorderBrush = SystemColors.ActiveBorderBrush;
    colorSelector.BorderThickness = new Thickness(1);
    colorSelector.Background = colorPopupBackground_;
    colorSelector.ColorSelected += ColorSelector_ColorSelected;

    colorPopup_ = new Popup();
    colorPopup_.Placement = PlacementMode.Bottom;
    colorPopup_.PlacementTarget = colorButton_;
    colorPopup_.StaysOpen = true;
    colorPopup_.Child = colorSelector;
    colorPopup_.IsOpen = true;
  }

  private void ColorSelector_ColorSelected(object sender, SelectedColorEventArgs e) {
    SetPanelAccentColor(e.SelectedColor);
    HideColorSelector();
  }

  protected virtual void SetPanelAccentColor(Color color) {

  }

  public event EventHandler PopupClosed;
  public event EventHandler PopupDetached;
  public Thumb Thumb { get; private set; } = new Thumb {Width = 0, Height = 0};

  public IntPtr PopupHandle {
    get {
      var source = PresentationSource.FromVisual(Child) as HwndSource;
      return source?.Handle ?? IntPtr.Zero;
    }
  }

  public bool IsDetached => isDetached_;

  public virtual bool ShouldStartDragging(MouseButtonEventArgs e) {
    HideColorSelector();
    return e.LeftButton == MouseButtonState.Pressed;
  }

  public virtual void DetachPopup() {
    isDetached_ = true;
    StaysOpen = true;
    PopupDetached?.Invoke(this, null);
  }

  public virtual void ShowPopup() {
    IsOpen = true;
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
    NativeMethods.RECT rect;

    if (!NativeMethods.GetWindowRect(PopupHandle, out rect)) {
      return;
    }

    NativeMethods.SetWindowPos(PopupHandle, NativeMethods.HWND_TOPMOST, rect.Left, rect.Top, (int)Width, (int)Height,
                               NativeMethods.TOPMOST_FLAGS);
  }

  public void SendToBack() {
    NativeMethods.RECT rect;

    if (!NativeMethods.GetWindowRect(PopupHandle, out rect)) {
      return;
    }

    NativeMethods.SetWindowPos(PopupHandle, NativeMethods.HWND_NOTOPMOST, rect.Left, rect.Top, (int)Width, (int)Height,
                               NativeMethods.TOPMOST_FLAGS);
  }

  public void Minimize() {
    duringMinimize_ = true;
    IsOpen = false;
  }

  public void Restore() {
    IsOpen = true;
  }

  public void Close() {
    IsOpen = false;
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

  protected override void OnPreviewMouseDown(MouseButtonEventArgs e) {
    base.OnPreviewMouseDown(e);
    //? TODO: This breaks the ComboBox in the documents options panel
    //? by bringing the panel on top of the ComboBox on mouse down when selecting item
    //BringToFront();
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
