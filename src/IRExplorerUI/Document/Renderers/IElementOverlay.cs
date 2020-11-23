// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Media;
using IRExplorerCore.IR;

namespace IRExplorerUI.Document {
    public interface IElementOverlay {
        string ToolTip { get; }
        HorizontalAlignment AlignmentX { get; }
        VerticalAlignment AlignmentY { get; }
        double MarginX { get; }
        double MarginY { get; }
        Size Size { get; }
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
        public Size Size => new Size(Width, Height);
        public HorizontalAlignment AlignmentX { get; set; }
        public VerticalAlignment AlignmentY { get; set; }

        public abstract void Draw(Rect elementRect, IRElement element, 
                                  DrawingContext drawingContext);

        protected double ComputePositionX(Rect rect) {
            if (AlignmentX == HorizontalAlignment.Left) {
                return rect.Left - Width - MarginX;
            }
            else if(AlignmentX == HorizontalAlignment.Right) {
                return rect.Right + MarginX;
            }
            else {
                return rect.Left + (rect.Width - Width) / 2;
            }
        }

        protected double ComputePositionY(Rect rect) {
            if (AlignmentY == VerticalAlignment.Top) {
                return rect.Top - Height - MarginY;
            }
            else if(AlignmentY == VerticalAlignment.Bottom) {
                return rect.Right + MarginX;
            }
            else {
                return rect.Top + (rect.Height - Height) / 2;
            }
        }
    }
}
