// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace IRExplorerCore.IR {
    public sealed class BlockLabelIR : TupleIR {
        public BlockLabelIR(IRElementId elementId, ReadOnlyMemory<char> name,
                            BlockIR parent = null) : base(elementId, TupleKind.Label, parent) {
            Name = name;
        }

        public ReadOnlyMemory<char> Name { get; set; }

        public override bool HasName => true;
        public override ReadOnlyMemory<char> NameValue => Name;

        public override void Accept(IRVisitor visitor) {
            visitor.Visit(this);
        }

        public override bool Equals(object obj) {
            return obj is BlockLabelIR iR && base.Equals(obj) && Name.Equals(iR.Name);
        }

        public override int GetHashCode() {
            return HashCode.Combine(base.GetHashCode(), Name);
        }

        public override string ToString() {
            return $"  > label name: {Name}\n";
        }
    }
}
