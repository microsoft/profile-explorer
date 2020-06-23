// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows.Media;

namespace Client {
    public static class HighlightingStyles {
        public static HighlightingStyleCollection StyleSet;
        public static HighlightingStyleCollection LightStyleSet;

        static HighlightingStyles() {
            StyleSet = new HighlightingStyleCollection();

            StyleSet.Styles.Add(
                new HighlightingStyle(Color.FromRgb(254, 202, 165), Pens.GetPen(Colors.Gray)));

            StyleSet.Styles.Add(
                new HighlightingStyle(Color.FromRgb(232, 254, 165), Pens.GetPen(Colors.Gray)));

            StyleSet.Styles.Add(
                new HighlightingStyle(Color.FromRgb(173, 254, 165), Pens.GetPen(Colors.Gray)));

            StyleSet.Styles.Add(
                new HighlightingStyle(Color.FromRgb(165, 180, 254), Pens.GetPen(Colors.Gray)));

            StyleSet.Styles.Add(
                new HighlightingStyle(Color.FromRgb(254, 165, 187), Pens.GetPen(Colors.Gray)));

            LightStyleSet = new HighlightingStyleCollection();
            LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(253, 231, 216)));
            LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(244, 253, 216)));
            LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(220, 253, 216)));
            LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(216, 223, 253)));
            LightStyleSet.Styles.Add(new HighlightingStyle(Color.FromRgb(253, 216, 226)));
        }

        public static HighlightingStyleCollection GetStyleSetWithBorder(
            HighlightingStyleCollection baseStyleSet, Pen pen) {
            var newStyle = new HighlightingStyleCollection();

            foreach (var style in baseStyleSet.Styles) {
                newStyle.Styles.Add(new HighlightingStyle(style.BackColor, pen));
            }

            return newStyle;
        }
    }
}
