// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace IRExplorerUI {
    public static class ColorBrushes {
        private static readonly Dictionary<Color, SolidColorBrush> brushes_;

        static ColorBrushes() {
            brushes_ = new Dictionary<Color, SolidColorBrush>();
        }

        public static SolidColorBrush GetBrush(string colorName) {
            return GetBrush(Utils.ColorFromString(colorName));
        }

        public static SolidColorBrush GetBrush(Color color) {
            if (brushes_.TryGetValue(color, out var brush)) {
                return brush;
            }

            brush = new SolidColorBrush(color);
            brush.Freeze();
            brushes_.Add(color, brush);
            return brush;
        }

        public static SolidColorBrush GetTransparentBrush(Color baseColor, byte alpha) {
            return GetBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        }

        public static SolidColorBrush GetTransparentBrush(Color baseColor, double opacity) {
            byte alpha = Math.Clamp((byte)(opacity * 255), (byte)0, (byte)255);
            return GetBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        }
    }
}
