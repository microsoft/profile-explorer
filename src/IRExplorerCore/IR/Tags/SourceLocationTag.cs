// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace IRExplorerCore.IR {
    public sealed class SourceLocationTag : ITag {
        public SourceLocationTag(int line, int column) {
            Line = line;
            Column = column;
        }

        public int Line { get; set; }
        public int Column { get; set; }
        public string Name => "Source location";
        public TaggedObject Owner { get; set; }

        public override bool Equals(object obj) {
            return obj is SourceLocationTag tag && Line == tag.Line && Column == tag.Column;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Line, Column);
        }

        public override string ToString() {
            return $"source location: {Line};{Column}";
        }
    }
}
