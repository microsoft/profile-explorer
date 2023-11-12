// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerUI.Controls;
using IRExplorerUI.Utilities.UI;

namespace IRExplorerUI;

public abstract class HoverPreview {
  protected UIElement control_;
  protected UIElement previewPopup_;
  private DelayedAction removeHoveredAction_;

  public HoverPreview(UIElement control, TimeSpan hoverDuration) {
    control_ = control;
    var hover = new MouseHoverLogic(control, hoverDuration);
    hover.MouseHover += Hover_MouseHover;
    hover.MouseHoverStopped += Hover_MouseHoverStopped;
    control.MouseLeave += OnMouseLeave;
  }

  public void Hide() {
    HidePreviewPopup(true);
  }

  protected abstract void OnHidePopup();
  protected abstract void OnShowPopup(Point mousePoint, Point position);
  protected abstract bool OnHoverStopped(Point mousePosition);

  private void OnMouseLeave(object sender, MouseEventArgs e) {
    HidePreviewPopupDelayed();
  }

  private void HidePreviewPopup(bool force = false) {
    if (!ShouldHidePopup(force)) {
      return;
    }

    OnHidePopup();
  }

  private bool ShouldHidePopup(bool force = false) {
    return previewPopup_ != null && (force || !previewPopup_.IsMouseOver);
  }

  private void Hover_MouseHover(object sender, MouseEventArgs e) {
    ShowPreviewPopup(e.GetPosition(control_));
  }

  private void ShowPreviewPopup(Point mousePosition) {
    if (removeHoveredAction_ != null) {
      removeHoveredAction_.Cancel();
      removeHoveredAction_ = null;
    }

    var position = mousePosition.AdjustForMouseCursor();
    OnShowPopup(mousePosition, position);
  }

  private void HidePreviewPopupDelayed() {
    removeHoveredAction_?.Cancel();
    removeHoveredAction_ = DelayedAction.StartNew(() => {
      if (removeHoveredAction_ != null) {
        removeHoveredAction_ = null;
        HidePreviewPopup();
      }
    });
  }

  private void Hover_MouseHoverStopped(object sender, MouseEventArgs e) {
    if (OnHoverStopped(e.GetPosition(control_))) {
      HidePreviewPopupDelayed();
    }
  }
}

public class DraggablePopupHoverPreview : HoverPreview {
  private Func<Point, Point, DraggablePopup> createPopup_;
  private Action<DraggablePopup> detachPopup_;
  private Func<Point, DraggablePopup, bool> hoverStopped_;

  public DraggablePopupHoverPreview(UIElement control, TimeSpan hoverDuration,
                                    Func<Point, Point, DraggablePopup> createPopup,
                                    Func<Point, DraggablePopup, bool> hoverStopped,
                                    Action<DraggablePopup> detachPopup) :
    base(control, hoverDuration) {
    createPopup_ = createPopup;
    hoverStopped_ = hoverStopped;
    detachPopup_ = detachPopup;
  }

  public DraggablePopupHoverPreview(UIElement control,
                                    Func<Point, Point, DraggablePopup> createPopup,
                                    Func<Point, DraggablePopup, bool> hoverStopped,
                                    Action<DraggablePopup> detachPopup) :
    this(control, TimeSpan.MaxValue, createPopup, hoverStopped, detachPopup) {
  }

  public DraggablePopup PreviewPopup {
    get => (DraggablePopup)previewPopup_;
    set => previewPopup_ = value;
  }

  protected virtual DraggablePopup CreatePopup(Point mousePoint, Point position) {
    return createPopup_(mousePoint, position);
  }

  protected override void OnShowPopup(Point mousePoint, Point position) {
    var popup = CreatePopup(mousePoint, position);

    if (popup != null) {
      if (popup != PreviewPopup) {
        PreviewPopup = popup;
        PreviewPopup.PopupDetached += Popup_PopupDetached;
      }

      PreviewPopup.ShowPopup();
    }
  }

  protected override bool OnHoverStopped(Point mousePosition) {
    if (hoverStopped_ != null) {
      return hoverStopped_(mousePosition, PreviewPopup);
    }

    return true;
  }

  protected override void OnHidePopup() {
    PreviewPopup.ClosePopup();
    //previewPopup_ = null;
  }

  private void Popup_PopupDetached(object sender, EventArgs e) {
    var popup = (DraggablePopup)sender;
    detachPopup_?.Invoke(popup);

    if (popup == PreviewPopup) {
      previewPopup_ = null; // Prevent automatic closing.
    }
  }
}

public class ToolTipHoverPreview : HoverPreview {
  private Func<Point, Point, string> createPopup_;

  public ToolTipHoverPreview(UIElement control, Func<Point, Point, string> createPopup) :
    base(control, TimeSpan.MaxValue) {
    createPopup_ = createPopup;
  }

  private ToolTip PreviewPopup => (ToolTip)previewPopup_;

  protected override void OnHidePopup() {
    PreviewPopup.IsOpen = false;
  }

  protected override void OnShowPopup(Point mousePoint, Point position) {
    string text = createPopup_(mousePoint, position);

    if (!string.IsNullOrEmpty(text)) {
      previewPopup_ = new ToolTip {
        Content = text,
        IsOpen = true,
        FontFamily = new FontFamily("Consolas")
      };
    }
  }

  protected override bool OnHoverStopped(Point mousePosition) {
    return true;
  }
}
