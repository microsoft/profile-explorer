using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace IRExplorerUI.Document {
    public class DraggablePopup : Popup {
        [System.Runtime.InteropServices.DllImport("User32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

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

        public Thumb Thumb { get; private set; } = new Thumb {
            Width = 0,
            Height = 0,
        };

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

        public IntPtr PopupHandle => ((HwndSource)PresentationSource.FromVisual(Child)).Handle;
        public bool IsDetached => isDetached_;

        public void BringToFront() {
            SetForegroundWindow(PopupHandle);
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
            BringToFront();
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
