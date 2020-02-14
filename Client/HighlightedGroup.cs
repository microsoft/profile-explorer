// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using Core.IR;

namespace Client {
    public sealed class HighlightedGroup {
        public List<IRElement> Elements { get; set; }
        public HighlightingStyle Style { get; set; }

        public HighlightedGroup(HighlightingStyle style) {
            Elements = new List<IRElement>();
            Style = style;
        }

        public HighlightedGroup(IRElement element, HighlightingStyle style) : this(style) {
            Add(element);
        }

        public bool IsEmpty() {
            return Elements.Count == 0;
        }

        public void Add(IRElement element) {
            Elements.Add(element);
        }

        public void AddFront(IRElement element) {
            Elements.Insert(0, element);
        }

        public bool Remove(IRElement element) {
            return Elements.Remove(element);
        }
    }
}
