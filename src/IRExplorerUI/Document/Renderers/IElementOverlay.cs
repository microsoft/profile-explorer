// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Media;
using IRExplorerCore.IR;
using IRExplorerUI.Utilities;

namespace IRExplorerUI.Document {
    public interface IElementOverlay {
        string ToolTip { get; }
        HorizontalAlignment AlignmentX { get; }
        VerticalAlignment AlignmentY { get; }
        double MarginX { get; }
        double MarginY { get; }
        double Padding { get; }
        Brush Background { get; }
        Pen Border { get; }
        Brush TextColor { get; }
        public int TextSize { get; }
        FontWeight TextWeight { get; }
        public bool ShowBackgroundOnMouseOverOnly { get; }
        public bool ShowToolTipOnMouseOverOnly { get; }
        public bool UseToolTipBackground { get; }
        Size Size { get; }
        bool IsMouseOver { get; set; }
        bool CheckIsMouseOver(Point point);
        void Draw(Rect elementRect, IRElement element, DrawingContext drawingContext);
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
        public bool IsMouseOver { get; set; }
        public Rect Bounds { get; set; }
        public Brush Background { get; set; }
        public Pen Border { get; set; }
        public Brush TextColor { get; set; }
        public int TextSize { get; set; }
        public FontWeight TextWeight { get; set; }
        protected double ActualWidth => Width + 2 * Padding;
        protected double ActualHeight => Height + 2 * Padding;
        protected bool ShowToolTip => !string.IsNullOrEmpty(ToolTip) &&
                                     (!ShowToolTipOnMouseOverOnly || IsMouseOver);

        public abstract void Draw(Rect elementRect, IRElement element, DrawingContext drawingContext);

        protected void DrawBackground(Rect elementRect, double opacity, 
                                      DrawingContext drawingContext) {
            if (Background != null || Border != null) {
                drawingContext.PushOpacity(opacity);
                drawingContext.DrawRectangle(Background, Border, elementRect);
                drawingContext.Pop();
            }
        }

        private static readonly Typeface DefaultFont = new Typeface("Consolas");

        protected void DrawToolTip(Rect elementRect, double opacity, DrawingContext drawingContext) {
            var host = App.Current.MainWindow; // Used to get DPI.
            var fontBrush = TextColor != null ? TextColor : Brushes.Black;
            double fontSize = TextSize != 0 ? TextSize : App.Settings.DocumentSettings.FontSize;
            var text = DocumentUtils.CreateFormattedText(host, ToolTip, DefaultFont, fontSize,
                                                         fontBrush, TextWeight);
            double textX = elementRect.Right + MarginX;
            double textY = (elementRect.Top + elementRect.Height / 2) - text.Height / 2;
            drawingContext.PushOpacity(opacity);

            if (UseToolTipBackground) {
                // Draw a rectangle covering both the icon and tooltip.
                var rect = Utils.SnapRectToPixels(elementRect, 0, 0, text.Width + 2 * MarginX, 0);
                drawingContext.DrawRectangle(Background, Border, rect);
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
    }
}
