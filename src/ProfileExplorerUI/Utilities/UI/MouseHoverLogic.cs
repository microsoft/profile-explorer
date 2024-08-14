// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ProfileExplorer.UI.Utilities.UI;

/// <summary>
///   Encapsulates and adds MouseHover support to UIElements.
/// </summary>
public class MouseHoverLogic : IDisposable {
  private UIElement target;
  private TimeSpan hoverDuration;
  private DispatcherTimer mouseHoverTimer;
  private Point mouseHoverStartPoint;
  private MouseEventArgs mouseHoverLastEventArgs;
  private bool mouseHovering;
  private bool disposed;

  /// <summary>
  ///   Creates a new instance and attaches itself to the <paramref name="target" /> UIElement.
  /// </summary>
  public MouseHoverLogic(UIElement target) {
    if (target == null)
      throw new ArgumentNullException("target");
    this.target = target;
    this.target.MouseLeave += MouseHoverLogicMouseLeave;
    this.target.MouseMove += MouseHoverLogicMouseMove;
    this.target.MouseEnter += MouseHoverLogicMouseEnter;
    hoverDuration = SystemParameters.MouseHoverTime;
  }

  public MouseHoverLogic(UIElement target, TimeSpan hoverDuration) : this(target) {
    // Override hover duration.
    if (hoverDuration != TimeSpan.MaxValue) {
      this.hoverDuration = hoverDuration;
    }
  }

  /// <summary>
  ///   Occurs when the mouse starts hovering over a certain location.
  /// </summary>
  public event EventHandler<MouseEventArgs> MouseHover;
  /// <summary>
  ///   Occurs when the mouse stops hovering over a certain location.
  /// </summary>
  public event EventHandler<MouseEventArgs> MouseHoverStopped;

  /// <summary>
  ///   Removes the MouseHover support from the target UIElement.
  /// </summary>
  public void Dispose() {
    if (!disposed) {
      target.MouseLeave -= MouseHoverLogicMouseLeave;
      target.MouseMove -= MouseHoverLogicMouseMove;
      target.MouseEnter -= MouseHoverLogicMouseEnter;
    }

    disposed = true;
  }

  /// <summary>
  ///   Raises the <see cref="MouseHover" /> event.
  /// </summary>
  protected virtual void OnMouseHover(MouseEventArgs e) {
    if (MouseHover != null) {
      MouseHover(this, e);
    }
  }

  /// <summary>
  ///   Raises the <see cref="MouseHoverStopped" /> event.
  /// </summary>
  protected virtual void OnMouseHoverStopped(MouseEventArgs e) {
    if (MouseHoverStopped != null) {
      MouseHoverStopped(this, e);
    }
  }

  private void MouseHoverLogicMouseMove(object sender, MouseEventArgs e) {
    var mouseMovement = mouseHoverStartPoint - e.GetPosition(target);

    if (Math.Abs(mouseMovement.X) > SystemParameters.MouseHoverWidth
        || Math.Abs(mouseMovement.Y) > SystemParameters.MouseHoverHeight) {
      StartHovering(e);
    }
    // do not set e.Handled - allow others to also handle MouseMove
  }

  private void MouseHoverLogicMouseEnter(object sender, MouseEventArgs e) {
    StartHovering(e);
    // do not set e.Handled - allow others to also handle MouseEnter
  }

  private void StartHovering(MouseEventArgs e) {
    StopHovering();
    mouseHoverStartPoint = e.GetPosition(target);
    mouseHoverLastEventArgs = e;
    mouseHoverTimer = new DispatcherTimer(hoverDuration, DispatcherPriority.Background, OnMouseHoverTimerElapsed,
                                          target.Dispatcher);
    mouseHoverTimer.Start();
  }

  private void MouseHoverLogicMouseLeave(object sender, MouseEventArgs e) {
    StopHovering();
    // do not set e.Handled - allow others to also handle MouseLeave
  }

  private void StopHovering() {
    if (mouseHoverTimer != null) {
      mouseHoverTimer.Stop();
      mouseHoverTimer = null;
    }

    if (mouseHovering) {
      mouseHovering = false;
      OnMouseHoverStopped(mouseHoverLastEventArgs);
    }
  }

  private void OnMouseHoverTimerElapsed(object sender, EventArgs e) {
    mouseHoverTimer.Stop();
    mouseHoverTimer = null;

    mouseHovering = true;
    OnMouseHover(mouseHoverLastEventArgs);
  }
}