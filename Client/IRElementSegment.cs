// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows.Media;
using Core.IR;
using ICSharpCode.AvalonEdit.Document;

namespace Client {
    public class IRSegment : TextSegment {
        public IRElement Element { get; set; }

        public IRSegment(IRElement element) {
            Element = element;
            if (element == null) return;

            StartOffset = element.TextLocation.Offset;
            Length = element.TextLength;
        }
    }

    public enum HighlighingType {
        Hovered,
        Selected,
        Marked
    }

    public sealed class HighlightedSegmentGroup {
        public HighlightedGroup Group { get; set; }
        public TextSegmentCollection<IRSegment> Segments { get; set; }
        public bool SavesStateToFile { get; set; }
        public Brush BackColor { get { return Group.Style.BackColor; } }
        public Pen Border { get { return Group.Style.Border; } }

        public HighlightedSegmentGroup(HighlightedGroup group, bool saveToFile = true) {
            Group = group;
            Segments = new TextSegmentCollection<IRSegment>();
            SavesStateToFile = saveToFile;

            foreach (var element in Group.Elements) {
                if (element == null) {
                    throw new NullReferenceException("Element is null");
                }

                Add(element);
            }
        }

        public void Add(IRElement element) {
            Segments.Add(new IRSegment(element));
        }
    }
}
