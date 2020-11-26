// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerCore.IR;
using IRExplorerUI.Utilities;

namespace IRExplorerUI.Document {
    public interface IElementOverlay {
        HorizontalAlignment AlignmentX { get; }
        VerticalAlignment AlignmentY { get; }
        double MarginX { get; }
        double MarginY { get; }
        double Padding { get; }
        Size Size { get; }
        bool IsMouseOver { get; set; }
        bool IsSelected { get; set; }
        void Draw(Rect elementRect, IRElement element, DrawingContext drawingContext);

        bool CheckIsMouseOver(Point point);
        bool MouseClicked(MouseEventArgs e);
        bool KeyPressed(KeyEventArgs e);

        // event EventHandler OnHover;
        event MouseEventHandler OnClick;
        event KeyEventHandler OnKeyPress;
    }

    public abstract class ElementOverlayBase : IElementOverlay {
        protected ElementOverlayBase(double width, double height,
                                     double marginX, double marginY, 
                                     HorizontalAlignment alignmentX,
                                     VerticalAlignment alignmentY,
                                     string toolTip) {
            ToolTip = toolTip;
            Width = width;
            Height = height;
            MarginX = marginX;
            MarginY = marginY;
            AlignmentX = alignmentX;
            AlignmentY = alignmentY;
        }

        public string ToolTip { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double MarginX { get; set; }
        public double MarginY { get; set;  }
        public double Padding { get; set; }
        public Size Size => new Size(ActualWidth, ActualHeight);
        public HorizontalAlignment AlignmentX { get; set; }
        public VerticalAlignment AlignmentY { get; set; }
        public bool ShowBackgroundOnMouseOverOnly { get; set; }
        public bool ShowToolTipOnMouseOverOnly { get; set; }
        public bool UseToolTipBackground { get; set; }
        public bool AllowToolTipEditing { get; set; }
        public bool IsToolTipPinned { get; set; }
        public double DefaultOpacity { get; set; }
        public double MouseOverOpacity { get; set; }
        public bool IsMouseOver { get; set; }
        public bool IsSelected { get; set; }
        public Rect Bounds { get; set; }
        public Brush Background { get; set; }
        public Brush SelectedBackground { get; set; }
        public Pen Border { get; set; }
        public Brush TextColor { get; set; }
        public Brush SelectedTextColor { get; set; }
        public int TextSize { get; set; }
        public FontWeight TextWeight { get; set; }
        protected double ActualWidth => Width + 2 * Padding;
        protected double ActualHeight => Height + 2 * Padding;
        protected virtual bool ShowToolTip => !string.IsNullOrEmpty(ToolTip) &&
                                             (!ShowToolTipOnMouseOverOnly || 
                                              IsToolTipPinned || IsMouseOver || IsSelected);
        protected virtual bool ShowBackground => !ShowBackgroundOnMouseOverOnly || IsMouseOver || IsSelected;

        public abstract void Draw(Rect elementRect, IRElement element, DrawingContext drawingContext);

        protected void DrawBackground(Rect elementRect, double opacity, 
                                      DrawingContext drawingContext) {
            if (Background != null || Border != null) {
                drawingContext.PushOpacity(opacity);
                drawingContext.DrawRectangle(CurrentBackgroundBrush, Border, elementRect);
                drawingContext.Pop();
            }
        }

        protected virtual Brush CurrentBackgroundBrush => IsSelected && SelectedBackground != null ?
                   SelectedBackground : Background;

        protected virtual Brush ActiveTextBrush => IsSelected && SelectedTextColor != null ?
                   SelectedTextColor : (TextColor ?? Brushes.Black);

        protected virtual double ActiveOpacity => (IsMouseOver || IsSelected) ?
            (MouseOverOpacity > 0 ? MouseOverOpacity : 1.0) :
            (DefaultOpacity > 0 ? DefaultOpacity : 1.0);

        private static readonly Typeface DefaultFont = new Typeface("Consolas");

        public event MouseEventHandler OnClick;
        public event KeyEventHandler OnKeyPress;

        protected void DrawToolTip(Rect elementRect, double opacity, DrawingContext drawingContext) {
            var host = App.Current.MainWindow; // Used to get DPI.
            double fontSize = TextSize != 0 ? TextSize : App.Settings.DocumentSettings.FontSize;
            
            var text = DocumentUtils.CreateFormattedText(host, ToolTip, DefaultFont, fontSize,
                                                         ActiveTextBrush, TextWeight);
            double textX = elementRect.Right + MarginX;
            double textY = (elementRect.Top + elementRect.Height / 2) - text.Height / 2;
            drawingContext.PushOpacity(opacity);

            if (UseToolTipBackground) {
                // Draw a rectangle covering both the icon and tooltip.
                var rect = Utils.SnapRectToPixels(elementRect, 0, 0, text.Width + 2 * MarginX, 0);
                drawingContext.DrawRectangle(CurrentBackgroundBrush, Border, rect);
            }

            drawingContext.DrawText(text, Utils.SnapPointToPixels(textX, textY));
            drawingContext.Pop();
        }

        protected double ComputePositionX(Rect rect) {
            if (AlignmentX == HorizontalAlignment.Left) {
                return Utils.SnapToPixels(rect.Left - ActualWidth - MarginX);
            }
            else if(AlignmentX == HorizontalAlignment.Right) {
                return Utils.SnapToPixels(rect.Right + MarginX);
            }
            else {
                return Utils.SnapToPixels(rect.Left + (rect.Width - ActualWidth) / 2);
            }
        }

        protected double ComputePositionY(Rect rect) {
            if (AlignmentY == VerticalAlignment.Top) {
                return Utils.SnapToPixels(rect.Top - ActualHeight - MarginY);
            }
            else if(AlignmentY == VerticalAlignment.Bottom) {
                return Utils.SnapToPixels(rect.Right + MarginX);
            }
            else {
                return Utils.SnapToPixels(rect.Top + (rect.Height - ActualHeight) / 2);
            }
        }

        public bool CheckIsMouseOver(Point point) {
            IsMouseOver = Bounds.Contains(point);
            return IsMouseOver;
        }

        public virtual bool MouseClicked(MouseEventArgs e) {
            OnClick?.Invoke(this, e);
            return e.Handled;
        }

        public virtual bool KeyPressed(KeyEventArgs e) {
            OnKeyPress?.Invoke(this, e);

            if (!AllowToolTipEditing || !IsSelected) {
                return e.Handled;
            }

            var keyInfo = Utils.KeyToChar(e.Key);

            if (keyInfo.IsLetter) {
                // Append a new letter.
                string keyString = keyInfo.Letter.ToString();

                if (string.IsNullOrEmpty(ToolTip)) {
                    ToolTip = keyString;
                }
                else {
                    ToolTip += keyString;
                }

                return true;
            }
            else if (e.Key == Key.Back) {
                // Remove last letter.
                if (!string.IsNullOrEmpty(ToolTip)) {
                    ToolTip = ToolTip.Substring(0, ToolTip.Length - 1);
                    return true;
                }
            }
            else if (e.Key == Key.Delete) {
                ToolTip = null; // Delete all text.
                return true;
            }
            else if(e.Key == Key.Enter) {
                IsToolTipPinned = true;
                return true;
            }
            else if (e.Key == Key.Escape) {
                IsToolTipPinned = false;
                return true;
            }

            return e.Handled;
        }
    }
}
