// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace IRExplorer {
    public static class Pens {
        private static readonly double BoldPenThickness = 1.5;
        private static readonly Dictionary<Tuple<Color, double>, Pen> pens_;
        private static readonly Dictionary<Tuple<Color, double, DashStyle>, Pen> dashedPens_;

        static Pens() {
            pens_ = new Dictionary<Tuple<Color, double>, Pen>();
            dashedPens_ = new Dictionary<Tuple<Color, double, DashStyle>, Pen>();
        }

        public static Pen GetPen(Color color, double thickness = 1.0) {
            var pair = new Tuple<Color, double>(color, thickness);

            if (pens_.TryGetValue(pair, out var pen)) {
                return pen;
            }

            pen = CreatePen(color, thickness);
            pens_.Add(pair, pen);
            return pen;
        }

        public static Pen GetPen(Brush brush, double thickness = 1.0) {
            if (brush is SolidColorBrush colorBrush) {
                return GetPen(colorBrush.Color, thickness);
            }

            return null;
        }

        public static Pen GetPen(string color, double thickness = 1.0) {
            return GetPen(Utils.ColorFromString(color), thickness);
        }

        public static Pen GetBoldPen(Color color) {
            return GetPen(color, BoldPenThickness);
        }

        public static Pen GetBoldPen(string color) {
            return GetPen(Utils.ColorFromString(color), BoldPenThickness);
        }

        public static Pen GetDashedPen(Color color, DashStyle dashStyle, double thickness = 1.0) {
            var pair = new Tuple<Color, double, DashStyle>(color, thickness, dashStyle);

            if (dashedPens_.TryGetValue(pair, out var pen)) {
                return pen;
            }

            pen = CreateDashedPen(color, dashStyle, thickness);
            dashedPens_.Add(pair, pen);
            return pen;
        }

        private static Pen CreatePen(Color color, double thickness) {
            Pen pen;
            var brush = ColorBrushes.GetBrush(color);
            pen = new Pen(brush, thickness);

            if (brush.CanFreeze) {
                brush.Freeze();
            }

            if (pen.CanFreeze) {
                pen.Freeze();
            }

            return pen;
        }

        private static Pen CreateDashedPen(Color color, DashStyle dashStyle, double thickness) {
            Pen pen;
            var brush = ColorBrushes.GetBrush(color);

            pen = new Pen(brush, thickness) {
                DashStyle = dashStyle
            };

            if (brush.CanFreeze) {
                brush.Freeze();
            }

            if (pen.CanFreeze) {
                pen.Freeze();
            }

            return pen;
        }
    }
}
