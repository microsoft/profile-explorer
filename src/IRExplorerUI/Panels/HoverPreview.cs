using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerUI.Controls;

namespace IRExplorerUI;

public partial class CallTreePanel {
    public abstract class HoverPreview {
        protected UIElement control_;
        protected UIElement previewPopup_;
        private DelayedAction removeHoveredAction_;
            
        public HoverPreview(UIElement control) {
            control_ = control;
            var hover = new MouseHoverLogic(control);
            hover.MouseHover += Hover_MouseHover;
            hover.MouseHoverStopped += Hover_MouseHoverStopped;
            control.MouseLeave += OnMouseLeave;
        }

        private void OnMouseLeave(object sender, MouseEventArgs e) {
            HidePreviewPopup();
        }

        protected abstract void OnHidePopup();

        protected abstract void OnShowPopup(Point position, UIElement relativeElement);

        protected abstract UIElement FindPointedElement();

        private void HidePreviewPopup(bool force = false) {
            if (previewPopup_ != null && (force || !previewPopup_.IsMouseOver)) {
                OnHidePopup();
                previewPopup_ = null;
            }
        }

        private void Hover_MouseHover(object sender, MouseEventArgs e) {
            var hoveredItem = FindPointedElement();

            if (hoveredItem != null) {
                ShowPreviewPopup(hoveredItem);
            }
        }
        private void ShowPreviewPopup(UIElement hoveredElement) {
            if (previewPopup_ != null) {
                //if (previewPopup_.PreviewedElement == element) {
                //    return;
                //}

                HidePreviewPopup(true);
            }

            if (removeHoveredAction_ != null) {
                removeHoveredAction_.Cancel();
                removeHoveredAction_ = null;
            }

            var position = Mouse.GetPosition(hoveredElement).AdjustForMouseCursor();
            OnShowPopup(position, hoveredElement);
        }

        private void HidePreviewPopupDelayed() {
            removeHoveredAction_ = DelayedAction.StartNew(() => {
                if (removeHoveredAction_ != null) {
                    removeHoveredAction_ = null;
                    HidePreviewPopup();
                }
            });
        }
            
        private void Hover_MouseHoverStopped(object sender, MouseEventArgs e) {
            HidePreviewPopupDelayed();
        }
    }

    public class DraggablePopupHoverPreview : HoverPreview {
        private DraggablePopup PreviewPopup => (DraggablePopup)previewPopup_;
        private Func<Point, UIElement> findElement_;
        private Func<Point, UIElement, DraggablePopup> createPopup_;

        public DraggablePopupHoverPreview(UIElement control,
                                    Func<Point, UIElement> findElement,
                                    Func<Point, UIElement, DraggablePopup> createPopup) : base(control) {
            findElement_ = findElement;
            createPopup_ = createPopup;
        }

        protected override void OnHidePopup() {
            PreviewPopup.ClosePopup();
        }

        protected override void OnShowPopup(Point position, UIElement relativeElement) {
            previewPopup_ = CreatePopup(position, relativeElement);

            if (previewPopup_ != null) {
                PreviewPopup.PopupDetached += Popup_PopupDetached;
                PreviewPopup.ShowPopup();
            }
        }

        private void Popup_PopupDetached(object sender, EventArgs e) {
            var popup = (DraggablePopup)sender;

            if (popup == previewPopup_) {
                previewPopup_ = null; // Prevent automatic closing.
            }
        }

        protected virtual DraggablePopup CreatePopup(Point position, UIElement hoveredElement) {
            return createPopup_(position, hoveredElement);
        }

        protected override UIElement FindPointedElement() {
            var mousePosition = Mouse.GetPosition(control_);
            return findElement_(mousePosition);
        }
    }

    public class ToolTipHoverPreview : HoverPreview {
        private ToolTip PreviewPopup => (ToolTip)previewPopup_;
        private Func<Point, UIElement> findElement_;
        private Func<Point, UIElement, string> createPopup_;

        public ToolTipHoverPreview(UIElement control,
            Func<Point, UIElement> findElement,
            Func<Point, UIElement, string> createPopup) : base(control) {
            findElement_ = findElement;
            createPopup_ = createPopup;
        }

        protected override void OnHidePopup() {
            PreviewPopup.IsOpen = false;
        }

        protected override void OnShowPopup(Point position, UIElement relativeElement) {
            var text = createPopup_(position, relativeElement);

            if (!string.IsNullOrEmpty(text)) {
                previewPopup_ = new ToolTip() { Content = text, IsOpen = true, FontFamily = new FontFamily("Consolas") };
            }
        }

        protected override UIElement FindPointedElement() {
            var mousePosition = Mouse.GetPosition(control_);
            return findElement_(mousePosition);
        }
    }
}