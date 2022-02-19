using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace IRExplorerUI.Controls {
    public class DraggablePopup : Popup {
        private bool duringMinimize_;
        private bool isDetached_;

        public DraggablePopup() {
            MouseDown += (sender, e) => {
                if (ShouldStartDragging()) {
                    Thumb.RaiseEvent(e);
                }
            };

            Thumb.DragDelta += (sender, e) => {
                HorizontalOffset += e.HorizontalChange;
                VerticalOffset += e.VerticalChange;
            };
        }

        public event EventHandler PopupClosed;
        public event EventHandler PopupDetached;

        public Thumb Thumb { get; private set; } = new Thumb {Width = 0, Height = 0,};

        public void UpdatePosition(Point position, UIElement referenceElement) {
            // Due to various DPI settings, the Window coordinates needs
            // some adjustment of the values based on the monitor.
            var screenPosition = position;

            if (referenceElement != null) {
                screenPosition = Utils.CoordinatesToScreen(position, referenceElement);
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

        public virtual bool ShouldStartDragging() {
            return true;
        }

        public virtual void DetachPopup() {
            isDetached_ = true;
            StaysOpen = true;
            PopupDetached?.Invoke(this, null);
        }

        public virtual void ClosePopup() {
            PopupClosed?.Invoke(this, null);
        }

        public IntPtr PopupHandle {
            get {
                var source = PresentationSource.FromVisual(Child) as HwndSource;
                return source?.Handle ?? IntPtr.Zero;
            }
        }

        public bool IsDetached => isDetached_;

        public void BringToFront() {
            NativeMethods.RECT rect;

            if (!NativeMethods.GetWindowRect(PopupHandle, out rect)) {
                return;
            }

            NativeMethods.SetWindowPos(PopupHandle, NativeMethods.HWND_TOPMOST, rect.Left, rect.Top, (int)Width, (int)Height, NativeMethods.TOPMOST_FLAGS);
        }

        public void SendToBack() {
            NativeMethods.RECT rect;

            if (!NativeMethods.GetWindowRect(PopupHandle, out rect)) {
                return;
            }

            NativeMethods.SetWindowPos(PopupHandle, NativeMethods.HWND_NOTOPMOST, rect.Left, rect.Top, (int)Width, (int)Height, NativeMethods.TOPMOST_FLAGS);
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
}
