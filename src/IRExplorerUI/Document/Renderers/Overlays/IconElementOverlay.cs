// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Media;
using IRExplorerCore.IR;

namespace IRExplorerUI.Document {
    public class IconElementOverlay : ElementOverlayBase {
        public IconElementOverlay(IconDrawing icon, double width, double height,
                                  double marginX = 2, double marginY = 2, 
                                  HorizontalAlignment alignmentX = HorizontalAlignment.Left,
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
            Icon.Draw(ComputePositionX(elementRect), ComputePositionY(elementRect),
                      Height, Width, drawingContext);
        }
    }
}
