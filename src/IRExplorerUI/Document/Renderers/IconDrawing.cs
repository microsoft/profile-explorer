// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Media;

namespace IRExplorerUI {
    public class IconDrawing {
        public IconDrawing(ImageSource icon, double proportion) {
            Icon = icon;
            Proportion = proportion;
        }

        public static IconDrawing FromIconResource(string name) {
            if(!Application.Current.Resources.Contains(name)) {
                return null;
            }

            var icon = Application.Current.Resources[name] as ImageSource;
            return new IconDrawing(icon, icon.Width / icon.Height);
        }

        public ImageSource Icon { get; set; }
        public double Proportion { get; set; }

        public void Draw(double x, double y, double size, double availableSize,
                         DrawingContext drawingContext) {
            double height = size;
            double width = height * Proportion;

            // Center icon in the available space.
            var rect = Utils.SnapRectToPixels(x + (availableSize - width) / 2, y,
                                              width, height);
            drawingContext.DrawImage(Icon, rect);
        }

        public void Draw(double x, double y, double size, double availableSize,
                         double opacity, DrawingContext drawingContext) {
            drawingContext.PushOpacity(opacity);
            Draw(x, y, size, availableSize, drawingContext);
            drawingContext.Pop();
        }
    }
}
