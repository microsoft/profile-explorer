// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorer {
    [ProtoContract]
    public sealed class HighlightingStyle {
        public HighlightingStyle() { }

        public HighlightingStyle(Color color) : this(color, 1.0) { }

        public HighlightingStyle(Color color, Pen border = null) : this(color, 1.0, border) { }

        public HighlightingStyle(string color, Pen border = null) : this(
            Utils.ColorFromString(color), 1.0, border) { }

        public HighlightingStyle(Color color, double opacity = 1.0, Pen border = null) {
            Brush colorBrush;

            if (Math.Abs(opacity - 1.0) < double.Epsilon) {
                colorBrush = ColorBrushes.GetBrush(color);
            }
            else {
                colorBrush = new SolidColorBrush(color);
                colorBrush.Opacity = opacity;

                if (colorBrush.CanFreeze) {
                    colorBrush.Freeze();
                }
            }

            BackColor = colorBrush;
            Border = border;
        }

        public HighlightingStyle(Brush backColor, Pen border = null) {
            BackColor = backColor;
            Border = border;
        }

        [ProtoMember(1)] public Brush BackColor { get; set; }

        [ProtoMember(2)] public Pen Border { get; set; }
    }

    public sealed class PairHighlightingStyle {
        public PairHighlightingStyle() {
            ParentStyle = new HighlightingStyle();
            ChildStyle = new HighlightingStyle();
        }

        public HighlightingStyle ParentStyle { get; set; }
        public HighlightingStyle ChildStyle { get; set; }
    }

    public class HighlightingStyleCollection {
        public HighlightingStyleCollection(List<HighlightingStyle> styles = null) {
            if (styles == null) {
                Styles = new List<HighlightingStyle>();
            }
            else {
                Styles = styles;
            }
        }

        public List<HighlightingStyle> Styles { get; set; }

        public HighlightingStyle ForIndex(int index) {
            return Styles[index % Styles.Count];
        }
    }

    public class HighlightingStyleCyclingCollection : HighlightingStyleCollection {
        private int counter_;

        public HighlightingStyleCyclingCollection(List<HighlightingStyle> styles = null) : base(styles) { }

        public HighlightingStyleCyclingCollection(HighlightingStyleCollection styleSet) : base(
            styleSet.Styles) { }

        public HighlightingStyle GetNext() {
            var style = ForIndex(counter_);
            counter_ = (counter_ + 1) % Styles.Count;
            return style;
        }
    }
}
