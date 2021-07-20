// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public class IRSegment : TextSegment {
        public IRSegment(IRElement element) {
            Element = element;

            if (element == null) {
                return;
            }

            StartOffset = element.TextLocation.Offset;
            Length = element.TextLength;
        }

        public IRElement Element { get; set; }
    }

    public enum HighlighingType {
        Hovered,
        Selected,
        Marked
    }

    public sealed class HighlightedSegmentGroup {
        public HighlightedSegmentGroup(HighlightedGroup group, bool saveToFile = true) {
            Group = group;
            Segments = new TextSegmentCollection<IRSegment>();
            SavesStateToFile = saveToFile;

            foreach (var element in Group.Elements) {
                Add(element);
            }
        }

        public HighlightedGroup Group { get; set; }
        public TextSegmentCollection<IRSegment> Segments { get; set; }
        public bool SavesStateToFile { get; set; }
        public Brush BackColor => Group.Style.BackColor;
        public Pen Border => Group.Style.Border;

        private void Add(IRElement element) {
            Segments.Add(new IRSegment(element));
        }
    }
}
