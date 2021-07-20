// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Media;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI.Document {
    [ProtoContract(SkipConstructor = true)]
    public class IconElementOverlay : ElementOverlayBase {
        public IconElementOverlay() : base() {
            // Used by deserialization.
            return;
        }

        public IconElementOverlay(IconDrawing icon, double width, double height, string toolTip,
                               HorizontalAlignment alignmentX, VerticalAlignment alignmentY,
                               double marginX, double marginY) :
            base(width, height, marginX, marginY, alignmentX, alignmentY, toolTip) {
            Icon = icon;
        }

        public static IconElementOverlay
        CreateDefault(IconDrawing icon, double width, double height,
                      Brush backColor, Brush selectedBackColor, Pen border,
                      string toolTip = "", 
                      HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                      VerticalAlignment alignmentY = VerticalAlignment.Center,
                      double marginX = 4, double marginY = 4) {
            return new IconElementOverlay(icon, width, height, toolTip, alignmentX, alignmentY,
                                          marginX, marginY) {
                Background = backColor,
                SelectedBackground = selectedBackColor,
                Border = border,
                ShowBackgroundOnMouseOverOnly = true,
                ShowBorderOnMouseOverOnly = true,
                ShowToolTipOnMouseOverOnly = true,
                UseToolTipBackground = true,
                Padding = 2,
                AllowToolTipEditing = true,
            };
        }

        [ProtoMember(1)]
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

            if (Icon != null) {
                DrawBackground(Bounds, opacity, drawingContext);
                Icon.Draw(x + Padding, y + Padding, Height, Width, opacity, drawingContext);
            }
        }
    }
}
