// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using IRExplorerCore.IR;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract]
    public class Bookmark {
        [ProtoMember(1)]
        private IRElementReference elementRef_;
        public Bookmark() { }

        public Bookmark(int index, IRElement element, string text, HighlightingStyle style) {
            Index = index;
            Element = element;
            Text = text;
            Style = style;
        }

        public IRElement Element {
            get => elementRef_;
            set => elementRef_ = value;
        }

        [ProtoMember(2)] public int Index { get; set; }

        [ProtoMember(3)] public string Text { get; set; }

        [ProtoMember(4)] public bool IsSelected { get; set; }

        [ProtoMember(5)] public bool IsPinned { get; set; }

        [ProtoMember(6)] public HighlightingStyle Style { get; set; }

        public bool HasStyle => Style != null;
        public Brush StyleBackColor => Style?.BackColor;
        public int Line => Element.TextLocation.Line;
        public string Block => Utils.MakeBlockDescription(Element.ParentBlock);
    }
}
