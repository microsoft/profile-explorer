using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerUI.Controls;

namespace IRExplorerUI;

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
    protected abstract void OnShowPopup(Point mousePoint, Point position);

    private void HidePreviewPopup(bool force = false) {
        if (previewPopup_ != null && (force || !previewPopup_.IsMouseOver)) {
            OnHidePopup();
            previewPopup_ = null;
        }
    }

    private void Hover_MouseHover(object sender, MouseEventArgs e) {
        ShowPreviewPopup(e.GetPosition(control_));
    }

    private void ShowPreviewPopup(Point mousePosition) {
        if (previewPopup_ != null) {
            HidePreviewPopup(true);
        }

        if (removeHoveredAction_ != null) {
            removeHoveredAction_.Cancel();
            removeHoveredAction_ = null;
        }

        var position = mousePosition.AdjustForMouseCursor();
        OnShowPopup(mousePosition, position);
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
        //HidePreviewPopupDelayed();
        HidePreviewPopup();
    }
}

public class DraggablePopupHoverPreview : HoverPreview {
    private DraggablePopup PreviewPopup => (DraggablePopup)previewPopup_;
    private Func<Point, Point, DraggablePopup> createPopup_;

    public DraggablePopupHoverPreview(UIElement control,
                                Func<Point, Point, DraggablePopup> createPopup) : base(control) {
        createPopup_ = createPopup;
    }

    protected override void OnShowPopup(Point mousePoint, Point position) {
        previewPopup_ = CreatePopup(mousePoint, position);

        if (previewPopup_ != null) {
            PreviewPopup.PopupDetached += Popup_PopupDetached;
            PreviewPopup.ShowPopup();
        }
    }

    protected virtual DraggablePopup CreatePopup(Point mousePoint, Point position) {
        return createPopup_(mousePoint, position);
    }

    protected override void OnHidePopup() {
        PreviewPopup.ClosePopup();
    }

    private void Popup_PopupDetached(object sender, EventArgs e) {
        var popup = (DraggablePopup)sender;

        if (popup == previewPopup_) {
            previewPopup_ = null; // Prevent automatic closing.
        }
    }
}

public class ToolTipHoverPreview : HoverPreview {
    private ToolTip PreviewPopup => (ToolTip)previewPopup_;
    private Func<Point, Point, string> createPopup_;

    public ToolTipHoverPreview(UIElement control,
        Func<Point, Point, string> createPopup) : base(control) {
        createPopup_ = createPopup;
    }

    protected override void OnHidePopup() {
        PreviewPopup.IsOpen = false;
    }

    protected override void OnShowPopup(Point mousePoint, Point position) {
        var text = createPopup_(mousePoint, position);

        if (!string.IsNullOrEmpty(text)) {
            previewPopup_ = new ToolTip() { Content = text, IsOpen = true, FontFamily = new FontFamily("Consolas") };
        }
    }
}