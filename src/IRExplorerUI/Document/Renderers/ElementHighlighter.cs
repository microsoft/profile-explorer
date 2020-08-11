// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract]
    public class ElementGroupState {
        [ProtoMember(1)]
        public List<IRElementReference> Elements;
        [ProtoMember(2)]
        public HighlightingStyle Style;

        public ElementGroupState() {
            Elements = new List<IRElementReference>();
        }
    }

    [ProtoContract]
    public class ElementHighlighterState {
        [ProtoMember(1)]
        public List<ElementGroupState> Groups;

        public ElementHighlighterState() {
            Groups = new List<ElementGroupState>();
        }

        public ElementHighlighterState(List<ElementGroupState> groups) {
            Groups = groups;
        }

        public bool HasAnnotations => Groups.Count > 0;
    }

    public sealed class ElementHighlighter : IBackgroundRenderer {
        //? TODO: Change to Set to have faster Remove (search panel)
        private List<HighlightedSegmentGroup> groups_;

        public ElementHighlighter(HighlighingType type) {
            Type = type;
            groups_ = new List<HighlightedSegmentGroup>(64);
            Version = 1;
        }

        public HighlighingType Type { get; set; }
        public int Version { get; set; }

        public List<HighlightedSegmentGroup> Groups {
            get => groups_;
            set => groups_ = value;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext) {
            if (textView.Document == null) {
                return;
            }

            if (textView.Document.TextLength == 0) {
                return;
            }

            // Find start/end index of visible lines.
            textView.EnsureVisualLines();
            var visualLines = textView.VisualLines;

            if (visualLines.Count == 0) {
                return;
            }

            int viewStart = visualLines[0].FirstDocumentLine.Offset;
            int viewEnd = visualLines[^1].LastDocumentLine.EndOffset;

            // Query and draw visible segments from each group.
            foreach (var group in groups_) {
                DrawGroup(group, textView, drawingContext, viewStart, viewEnd);
            }
        }

        public void Add(HighlightedGroup group, bool saveToFile = true) {
            groups_.Add(new HighlightedSegmentGroup(group, saveToFile));
            Version++;
        }

        public void AddFront(HighlightedGroup group, bool saveToFile = true) {
            groups_.Insert(0, new HighlightedSegmentGroup(group, saveToFile));
            Version++;
        }

        public void CopyFrom(ElementHighlighter other) {
            foreach (var item in other.groups_) {
                groups_.Add(new HighlightedSegmentGroup(item.Group));
            }

            Version++;
        }

        public void Remove(HighlightedGroup group) {
            for (int i = 0; i < groups_.Count; i++) {
                if (groups_[i].Group == group) {
                    groups_.RemoveAt(i);
                    break;
                }
            }

            Version++;
        }

        public void Remove(IRElement element) {
            for (int i = 0; i < groups_.Count; i++) {
                groups_[i].Group.Remove(element);

                if (groups_[i].Group.IsEmpty()) {
                    groups_.RemoveAt(i);
                    i--;
                }
            }

            Version++;
        }

        public void Clear() {
            groups_.Clear();
            Version++;
        }

        public void ForEachElement(Action<IRElement> action) {
            groups_.ForEach(group => { group.Group.Elements.ForEach(action); });
        }

        public void ForEachStyledElement(Action<IRElement, HighlightingStyle> action) {
            groups_.ForEach(group => {
                group.Group.Elements.ForEach(element => { action(element, group.Group.Style); });
            });
        }

        public ElementHighlighterState SaveState(FunctionIR function) {
            return new ElementHighlighterState(StateSerializer.SaveElementGroupState(groups_));
        }

        public void LoadState(ElementHighlighterState state, FunctionIR function) {
            groups_ = StateSerializer.LoadElementGroupState(state.Groups);
            Version++;
        }

        private void DrawGroup(HighlightedSegmentGroup group, TextView textView,
                               DrawingContext drawingContext, int viewStart, int viewEnd) {
            var geoBuilder = new BackgroundGeometryBuilder();

            //geoBuilder.AlignToWholePixels = true;
            geoBuilder.BorderThickness = 0;
            geoBuilder.CornerRadius = 0;

            foreach (var segment in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
                    var actualRect = Utils.SnapToPixels(rect, -1, 0, 2, 0);
                    geoBuilder.AddRectangle(textView, actualRect);
                }
            }

            var geometry = geoBuilder.CreateGeometry();

            if (geometry != null) {
                drawingContext.DrawGeometry(group.BackColor, group.Border, geometry);
            }
        }
    }
}