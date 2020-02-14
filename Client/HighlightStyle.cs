// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace Client {
    [ProtoContract]
    public sealed class HighlightingStyle {
        [ProtoMember(1)]
        public Brush BackColor { get; set; }
        [ProtoMember(2)]
        public Pen Border { get; set; }

        public HighlightingStyle() { }

        public HighlightingStyle(Color color) : this(color, 1.0, null) { }

        public HighlightingStyle(Color color, Pen border = null) : this(color, 1.0, border) { }

        public HighlightingStyle(string color, Pen border = null) :
            this(Utils.ColorFromString(color), 1.0, border) { }

        public HighlightingStyle(Color color, double opacity = 1.0, Pen border = null) {
            var colorBrush = new SolidColorBrush(color);
            colorBrush.Opacity = opacity;

            if (colorBrush.CanFreeze) {
                colorBrush.Freeze();
            }

            BackColor = colorBrush;
            Border = border;
        }

        public HighlightingStyle(Brush backColor, Pen border = null) {
            BackColor = backColor;
            Border = border;
        }
    }

    public sealed class PairHighlightingStyle {
        public HighlightingStyle ParentStyle { get; set; }
        public HighlightingStyle ChildStyle { get; set; }

        public PairHighlightingStyle() {
            ParentStyle = new HighlightingStyle();
            ChildStyle = new HighlightingStyle();
        }
    }

    public class HighlightingStyleCollection {
        public List<HighlightingStyle> Styles { get; set; }

        public HighlightingStyleCollection(List<HighlightingStyle> styles = null) {
            if (styles == null) {
                Styles = new List<HighlightingStyle>();
            }
            else {
                Styles = styles;
            }
        }

        public HighlightingStyle ForIndex(int index) {
            return Styles[index % Styles.Count];
        }
    }

    public class HighlightingStyleCyclingCollection : HighlightingStyleCollection {
        private int counter_;

        public HighlightingStyleCyclingCollection(List<HighlightingStyle> styles = null) : base(styles) { }

        public HighlightingStyleCyclingCollection(HighlightingStyleCollection styleSet) : base(styleSet.Styles) { }

        public HighlightingStyle GetNext() {
            var style = ForIndex(counter_);
            counter_ = (counter_ + 1) % Styles.Count;
            return style;
        }
    }
}
