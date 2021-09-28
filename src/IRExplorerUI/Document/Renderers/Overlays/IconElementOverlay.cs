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

        public IconElementOverlay(IconDrawing icon, double width, double height, 
                                  string label, string tooltip,
                                  HorizontalAlignment alignmentX, VerticalAlignment alignmentY,
                                  double marginX, double marginY) :
            base(width, height, marginX, marginY, alignmentX, alignmentY, label, tooltip) {
            Icon = icon;
        }

        public static IconElementOverlay
        CreateDefault(IconDrawing icon, double width, double height,
                      Brush backColor, Brush selectedBackColor, Pen border,
                      string label = "", string tooltip = "",
                      HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                      VerticalAlignment alignmentY = VerticalAlignment.Center,
                      double marginX = 4, double marginY = 4, double padding = 1) {
            return new IconElementOverlay(icon, width, height, 
                                          label, tooltip,
                                          alignmentX, alignmentY,
                                          marginX, marginY) {
                Background = backColor,
                SelectedBackground = selectedBackColor,
                Border = border,
                ShowBackgroundOnMouseOverOnly = true,
                ShowBorderOnMouseOverOnly = true,
                ShowLabelOnMouseOverOnly = true,
                UseLabelBackground = true,
                Padding = padding,
                AllowLabelEditing = true,
            };
        }

        [ProtoMember(1)]
        public IconDrawing Icon { get; set; }

        public override void Draw(Rect elementRect, IRElement element,
                                  IElementOverlay previousOverlay, DrawingContext drawingContext) {
            double x = ComputePositionX(elementRect, previousOverlay);
            double y = ComputePositionY(elementRect, previousOverlay);
            double opacity = ActiveOpacity;
            Bounds = Utils.SnapRectToPixels(x, y, ActualWidth, 
                                           Math.Max(ActualHeight, elementRect.Height));
            if (ShowLabel) {
                if (Icon == null) {
                    Bounds = Utils.SnapRectToPixels(Bounds.X + Bounds.Width, Bounds.Y, 0, Bounds.Height);
                }
                
                Bounds = DrawLabel(Bounds, opacity, drawingContext);
            }

            if (Icon != null) {
                DrawBackground(Bounds, opacity, drawingContext);
                Icon.Draw(x + 1, y - 1, Width, Width, ComputeHeight(Bounds), opacity, drawingContext);
            }
        }
    }
}
