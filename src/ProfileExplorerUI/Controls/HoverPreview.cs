// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI;

namespace ProfileExplorer.UI;

public abstract class HoverPreview : IDisposable {
  public static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(200);
  public static readonly TimeSpan LongHoverDuration = TimeSpan.FromMilliseconds(1000);
  public static readonly TimeSpan ExtraLongHoverDuration = TimeSpan.FromMilliseconds(2500);
  private UIElement control_;
  private MouseHoverLogic hover_;
  protected UIElement previewPopup_;
  private DelayedAction removeHoveredAction_;

  public HoverPreview(UIElement control, TimeSpan hoverDuration) {
    hoverDuration = TimeSpan.FromTicks(Math.Max(hoverDuration.Ticks, HoverDuration.Ticks));
    control_ = control;
    hover_ = new MouseHoverLogic(control, hoverDuration);
    hover_.MouseHover += Hover_MouseHover;
    hover_.MouseHoverStopped += Hover_MouseHoverStopped;
    control_.MouseLeave += OnMouseLeave;
  }

  public void Dispose() {
    hover_?.Dispose();
  }

  public void Hide() {
    HidePreviewPopup(true);
  }

  public void HideDelayed() {
    HidePreviewPopupDelayed(HoverDuration);
  }

  public void Unregister() {
    control_.MouseLeave -= OnMouseLeave;
    hover_.MouseHover -= Hover_MouseHover;
    hover_.MouseHoverStopped -= Hover_MouseHoverStopped;
    hover_.Dispose();
    hover_ = null;
  }

  protected abstract void OnHidePopup();
  protected abstract void OnShowPopup(Point mousePoint, Point position);
  protected abstract bool OnHoverStopped(Point mousePosition);

  private void OnMouseLeave(object sender, MouseEventArgs e) {
    HidePreviewPopupDelayed(HoverDuration);
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

  private void HidePreviewPopupDelayed(TimeSpan duration) {
    removeHoveredAction_?.Cancel();
    removeHoveredAction_ = DelayedAction.StartNew(duration, () => {
      if (removeHoveredAction_ != null) {
        removeHoveredAction_ = null;
        HidePreviewPopup();
      }
    });
  }

  private void Hover_MouseHoverStopped(object sender, MouseEventArgs e) {
    if (OnHoverStopped(e.GetPosition(control_))) {
      HidePreviewPopupDelayed(HoverDuration);
    }
  }
}

public class PopupHoverPreview : HoverPreview {
  private Func<Point, Point, DraggablePopup> createPopup_;
  private Action<DraggablePopup> detachPopup_;
  private Func<Point, DraggablePopup, bool> hoverStopped_;

  public PopupHoverPreview(UIElement control, TimeSpan hoverDuration,
                           Func<Point, Point, DraggablePopup> createPopup,
                           Func<Point, DraggablePopup, bool> hoverStopped,
                           Action<DraggablePopup> detachPopup) :
    base(control, hoverDuration) {
    createPopup_ = createPopup;
    hoverStopped_ = hoverStopped;
    detachPopup_ = detachPopup;
  }

  public PopupHoverPreview(UIElement control,
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