// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Media;
using IRExplorerCore.IR;

namespace IRExplorerUI.Document {
    public class IconElementOverlay : ElementOverlayBase {
        public IconElementOverlay(IconDrawing icon, double width, double height,
                                  double marginX = 4, double marginY = 2, 
                                  HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                                  VerticalAlignment alignmentY = VerticalAlignment.Center,
                                  string toolTip = "") :
            base(width, height, marginX, marginY, alignmentX, alignmentY, toolTip) {
            Icon = icon;
        }

        public static IconElementOverlay FromIconResource(string name, double width, double height) {
            return new IconElementOverlay(IconDrawing.FromIconResource(name), width, height);
        }

        public IconDrawing Icon { get; set; }

        public override void Draw(Rect elementRect, IRElement element,
                                  DrawingContext drawingContext) {
            double x = ComputePositionX(elementRect);
            double y = ComputePositionY(elementRect);
            double opacity = ActiveOpacity;
            Bounds = Utils.SnapRectToPixels(x, y, ActualWidth, 
                                            Math.Max(ActualHeight, elementRect.Height));
            if (ShowToolTip) {
                DrawToolTip(Bounds, opacity, drawingContext);
            }
            else if(ShowBackground) {
                DrawBackground(Bounds, opacity, drawingContext);
            }

            Icon.Draw(x + Padding, y + Padding, Height, ActualWidth, opacity, drawingContext);
        }
    }
}
