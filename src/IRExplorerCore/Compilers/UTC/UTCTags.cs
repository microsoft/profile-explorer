// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using IRExplorerCore.IR;

namespace IRExplorerCore.UTC {
    public enum SymbolAnnotationKind {
        Volatile,     // ^
        Writethrough, // ~
        CantMakeSDSU, // -
        Dead          // !
    }

    public class SymbolAnnotationTag {
        //? TODO
    }

    public class SymbolOffsetTag : ITag {
        public SymbolOffsetTag(long offset) {
            Offset = offset;
        }

        public long Offset { get;set; }
        public string Name => "Symbol offset";
        public TaggedObject Owner { get; set; }

        public override bool Equals(object obj) {
            return obj is SymbolOffsetTag tag &&
                   Offset == tag.Offset;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Offset);
        }

        public override string ToString() {
            return $"symbol offset: {Offset}";
        }
    }

}
