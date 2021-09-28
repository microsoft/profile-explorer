// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class IconDrawing {
        public IconDrawing() {
            // Used for deserialization.
        }

        public IconDrawing(ImageSource icon, double proportion) {
            Icon = icon;
            Proportion = proportion;
        }

        public static IconDrawing FromIconResource(string name) {
            if(!Application.Current.Resources.Contains(name)) {
                return null;
            }

            var icon = (ImageSource)Application.Current.Resources[name];
            return new IconDrawing(icon, icon.Width / icon.Height) {
                IconResourceName = name
            };
        }

        [ProtoMember(1)]
        public string IconResourceName { get; set; }
        [ProtoMember(2)]
        public double Proportion { get; set; }
        public ImageSource Icon { get; set; }

        [ProtoAfterDeserialization]
        private void AfterDeserialization() {
            if(!string.IsNullOrEmpty(IconResourceName)) {
                Icon = (ImageSource)Application.Current.Resources[IconResourceName];
            }
        }

        public void Draw(double x, double y, double size, double availableWidth, double availableHeight,
                         DrawingContext drawingContext) {
            // This assumes the icon should be centered inside a rectangle {availableWidth, availableHeight}
            // with {x, y} as top-left corner, scaling down if available space is not enough.
            double height;
            double width;

            if (availableHeight < availableWidth) {
                height = Math.Min(size, availableHeight);
                width = height * Proportion;
                y = y + (availableHeight - height) / 2;
            }
            else {
                height = Math.Min(size, availableWidth);
                width = height * Proportion;
                x = x + (availableHeight - width) / 2;
            }

            // Center icon in the available space.
            var rect = Utils.SnapRectToPixels(x, y, width, height);
            drawingContext.DrawImage(Icon, rect);
        }

        public void Draw(double x, double y, double size, double avaiableWidth, double availableHeight,
                       double opacity, DrawingContext drawingContext) {
            drawingContext.PushOpacity(opacity);
            Draw(x, y, size, avaiableWidth, availableHeight, drawingContext);
            drawingContext.Pop();
        }
    }
}
