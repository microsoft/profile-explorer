// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Core.IR {
    public sealed class SourceLocationTag : ITag {
        public string Name { get => "Source location"; }
        public IRElement Parent { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }

        public SourceLocationTag(int line, int column) {
            Line = line;
            Column = column;
        }

        public override bool Equals(object obj) {
            return obj is SourceLocationTag tag &&
                   Line == tag.Line &&
                   Column == tag.Column;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Line, Column);
        }

        public override string ToString() {
            return $"Source location: {Line};{Column}";
        }
    }
}
