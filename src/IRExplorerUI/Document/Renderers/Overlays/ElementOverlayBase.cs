// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerCore.IR;
using Microsoft.Diagnostics.Tracing;
using ProtoBuf;

namespace IRExplorerUI.Document;

[ProtoContract(SkipConstructor = true)]
[ProtoInclude(200, typeof(IconElementOverlay))]
public abstract class ElementOverlayBase : IElementOverlay {
  private static readonly Typeface DefaultFont = new Typeface("Consolas");
  private Rect labelBounds_;
  [ProtoMember(1)]
  private IRElementReference elementRef_;

  protected ElementOverlayBase() {
    // Used for deserialization.
  }

  protected ElementOverlayBase(double width, double height,
                               double marginX, double marginY,
                               HorizontalAlignment alignmentX,
                               VerticalAlignment alignmentY,
                               string label, string tooltip) {
    Label = label;
    ToolTip = tooltip;
    Width = width;
    Height = height;
    MarginX = marginX;
    MarginY = marginY;
    AlignmentX = alignmentX;
    AlignmentY = alignmentY;
    DefaultOpacity = 1;
    SaveStateToFile = false;
  }

  public event MouseEventHandler OnClick;
  public event KeyEventHandler OnKeyPress;
  public event MouseEventHandler OnHover;
  public event MouseEventHandler OnHoverEnd;
  [ProtoMember(2)]
  public string Label { get; set; }
  [ProtoMember(3)]
  public double Width { get; set; }
  [ProtoMember(4)]
  public double Height { get; set; }
  [ProtoMember(10)]
  public bool ShowBackgroundOnMouseOverOnly { get; set; }
  [ProtoMember(11)]
  public bool ShowBorderOnMouseOverOnly { get; set; }
  [ProtoMember(12)]
  public bool ShowLabelOnMouseOverOnly { get; set; }
  [ProtoMember(13)]
  public bool UseLabelBackground { get; set; }
  [ProtoMember(14)]
  public bool AllowLabelEditing { get; set; }
  [ProtoMember(15)]
  public bool IsLabelPinned { get; set; }
  [ProtoMember(17)]
  public double DefaultOpacity { get; set; }
  [ProtoMember(18)]
  public double MouseOverOpacity { get; set; }
  public bool HasLabel => !string.IsNullOrEmpty(Label);
  public bool HasToolTip => !string.IsNullOrEmpty(ToolTip);
  [ProtoMember(19)]
  public Brush Background { get; set; }
  [ProtoMember(20)]
  public Brush SelectedBackground { get; set; }
  [ProtoMember(21)]
  public Pen Border { get; set; }
  [ProtoMember(22)]
  public Brush TextColor { get; set; }
  [ProtoMember(23)]
  public Brush SelectedTextColor { get; set; }
  [ProtoMember(24)]
  public int TextSize { get; set; }
  [ProtoMember(25)]
  public FontWeight TextWeight { get; set; }
  [ProtoMember(26)]
  public double VirtualColumn { get; set; }
  [ProtoMember(27)]
  public string ToolTip { get; set; }

  public IRElement Element {
    get => elementRef_;
    set => elementRef_ = value;
  }

  [ProtoMember(5)]
  public double MarginX { get; set; }
  [ProtoMember(6)]
  public double MarginY { get; set; }
  [ProtoMember(7)]
  public double Padding { get; set; }
  public Size Size => new Size(ActualWidth, ActualHeight);
  [ProtoMember(8)]
  public HorizontalAlignment AlignmentX { get; set; }
  [ProtoMember(9)]
  public VerticalAlignment AlignmentY { get; set; }
  [ProtoMember(16)]
  public bool ShowOnMarkerBar { get; set; }
  public bool IsMouseOver { get; set; }
  public bool IsSelected { get; set; }
  public Rect Bounds { get; set; }
  [ProtoMember(28)]
  public bool SaveStateToFile { get; set; }
  protected virtual bool ShowLabel => !string.IsNullOrEmpty(Label) &&
                                      (!ShowLabelOnMouseOverOnly ||
                                       IsLabelPinned || IsMouseOver || IsSelected);
  protected virtual bool ShowBackground => Width > 0 && Height > 0 &&
                                           (!ShowBackgroundOnMouseOverOnly || IsMouseOver || IsSelected);
  protected virtual bool ShowBorder => !ShowBorderOnMouseOverOnly || IsMouseOver || IsSelected;
  protected virtual Brush CurrentBackgroundBrush => IsSelected && SelectedBackground != null ?
    SelectedBackground : Background;
  protected virtual Pen CurrentBorder => ShowBorder ? Border : null;
  protected virtual Brush CurrentLabelBackgroundBrush => Background == null ||
                                                         IsSelected && SelectedBackground != null ?
    SelectedBackground : Background;
  protected virtual Brush ActiveTextBrush => IsSelected && SelectedTextColor != null ?
    SelectedTextColor : TextColor ?? Brushes.Black;
  protected virtual double ActiveOpacity => IsMouseOver || IsSelected ? MouseOverOpacity > 0 ? MouseOverOpacity : 1.0 :
    DefaultOpacity > 0 ? DefaultOpacity : 1.0;
  protected double ActualWidth => Width + 2 * Padding;
  protected double ActualHeight => Height + 2 * Padding;

  public abstract void Draw(Rect elementRect, IRElement element,
                            IElementOverlay previousOverlay, DrawingContext drawingContext);

  public virtual bool CheckIsMouseOver(Point point) {
    IsMouseOver = Bounds.Contains(point);

    if (!IsMouseOver && ShowLabel) {
      IsMouseOver = labelBounds_.Contains(point);
    }

    return IsMouseOver;
  }

  public virtual bool Hovered(MouseEventArgs e) {
    OnHover?.Invoke(this, e);
    return true;
  }

  public virtual bool MouseClicked(MouseEventArgs e) {
    OnClick?.Invoke(this, e);
    return e.Handled;
  }

  public virtual bool KeyPressed(KeyEventArgs e) {
    OnKeyPress?.Invoke(this, e);

    if (!IsSelected) {
      return e.Handled;
    }

    if (AllowLabelEditing) {
      var keyInfo = Utils.KeyToChar(e.Key);

      if (keyInfo.IsLetter && Keyboard.Modifiers == ModifierKeys.None) {
        // Append a new letter.
        string keyString = keyInfo.Letter.ToString();

        if (string.IsNullOrEmpty(Label)) {
          Label = keyString;
        }
        else {
          Label += keyString;
        }

        return true;
      }
      else if (e.Key == Key.Back) {
        // Remove last letter.
        if (!string.IsNullOrEmpty(Label)) {
          Label = Label.Substring(0, Label.Length - 1);
          return true;
        }
      }
      else if (e.Key == Key.Delete) {
        Label = null; // Delete all text.
        return true;
      }
      else if (e.Key == Key.Enter) {
        IsLabelPinned = true;
        return true;
      }
      else if (e.Key == Key.Escape) {
        IsLabelPinned = false;
        return true;
      }
    }
    
    if(e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) {
      // Copy the label to the clipboard.
      if (HasToolTip) {
        Clipboard.Clear();
        Clipboard.SetText($"{Label}\n{ToolTip}", TextDataFormat.UnicodeText);
      }
      else {
        Clipboard.Clear();
        Clipboard.SetText(Label, TextDataFormat.UnicodeText);
      }
      return true;
    }

    return e.Handled;
  }

  protected void DrawBackground(Rect elementRect, double opacity, DrawingContext drawingContext) {
    if ((ShowBackground || ShowBorder) && !(ShowLabel && UseLabelBackground)) {
      drawingContext.PushOpacity(opacity);
      drawingContext.DrawRectangle(CurrentBackgroundBrush, CurrentBorder, elementRect);
      drawingContext.Pop();
    }
  }

  protected Rect DrawLabel(Rect elementRect, double opacity, DrawingContext drawingContext) {
    var host = Application.Current.MainWindow; // Used to get DPI.
    double fontSize = TextSize != 0 ? TextSize : App.Settings.DocumentSettings.FontSize;

    var text = DocumentUtils.CreateFormattedText(host, Label, DefaultFont, fontSize,
                                                 ActiveTextBrush, TextWeight);
    string[] lines = Label.Split(new[] {"\r\n", "\r", "\n"}, StringSplitOptions.None);
    double extraHeight = lines.Length > 1 ? elementRect.Height * (lines.Length - 1) : 0;

    double height = elementRect.Height + extraHeight;
    double width = elementRect.Width + text.WidthIncludingTrailingWhitespace + 2 * Padding;
    double textX = elementRect.Left + elementRect.Width + Padding;
    double textY = elementRect.Top + height / 2 - text.Height / 2;
    labelBounds_ = Utils.SnapRectToPixels(elementRect.X, elementRect.Y, width, height);
    drawingContext.PushOpacity(opacity);

    if (UseLabelBackground) {
      // Draw a rectangle covering both the icon and label.
      drawingContext.DrawRectangle(CurrentLabelBackgroundBrush, CurrentBorder, labelBounds_);
    }

    drawingContext.DrawText(text, Utils.SnapPointToPixels(textX, textY));
    drawingContext.Pop();
    return labelBounds_;
  }

  protected double ComputePositionX(Rect rect, IElementOverlay previousOveraly) {
    if (AlignmentX == HorizontalAlignment.Left) {
      double leftEdgeX = rect.Left;

      if (previousOveraly != null) {
        // Align to the right of the previous overlay.
        leftEdgeX = Math.Max(rect.Left, previousOveraly.Bounds.Right);
      }

      return Utils.SnapToPixels(leftEdgeX + MarginX);
    }

    if (AlignmentX == HorizontalAlignment.Right) {
      double rightEdgeX = rect.Right;

      if (previousOveraly != null) {
        // Align to the right of the previous overlay.
        rightEdgeX = Math.Max(rect.Right, previousOveraly.Bounds.Right);
      }

      return Utils.SnapToPixels(Math.Max(VirtualColumn, rightEdgeX + MarginX));
    }

    return Utils.SnapToPixels(rect.Left + (rect.Width - ActualWidth) / 2);
  }

  protected double ComputeHeight(Rect rect) {
    if (Height > 0) {
      return Math.Min(ActualHeight, rect.Height);
    }

    return rect.Height;
  }

  protected double ComputePositionY(Rect rect, IElementOverlay previousOveraly) {
    if (AlignmentY == VerticalAlignment.Top) {
      return Utils.SnapToPixels(rect.Top - ComputeHeight(rect) - MarginY);
    }

    if (AlignmentY == VerticalAlignment.Bottom) {
      return Utils.SnapToPixels(rect.Bottom + MarginY);
    }

    return Utils.SnapToPixels(rect.Top + (rect.Height - ComputeHeight(rect)) / 2);
  }

  public bool HoveredEnded(MouseEventArgs e) {
    OnHoverEnd?.Invoke(this, e);
    return true;
  }
}
