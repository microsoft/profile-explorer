// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using ProtoBuf;

namespace IRExplorer {
    [ProtoContract]
    public class IRDocumentState {
        [ProtoMember(5)]
        public byte[] bookmarks_;
        [ProtoMember(7)]
        public int caretOffset_;
        [ProtoMember(1)]
        public ElementHighlighterState hoverHighlighter_;

        [ProtoMember(4)]
        public DocumentMarginState margin_;
        [ProtoMember(3)]
        public ElementHighlighterState markedHighlighter_;

        [ProtoMember(6)]
        public List<IRElementReference> selectedElements_;
        [ProtoMember(2)]
        public ElementHighlighterState selectedHighlighter_;

        public bool HasAnnotations => markedHighlighter_.HasAnnotations || margin_.HasAnnotations;
    }
}
