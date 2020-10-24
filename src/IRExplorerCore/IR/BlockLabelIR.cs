// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore.Utilities;
using System;

namespace IRExplorerCore.IR {
    public sealed class BlockLabelIR : TupleIR {
        private ReadOnlyMemory<char> label_;

        public BlockLabelIR(IRElementId elementId, ReadOnlyMemory<char> name,
                            BlockIR parent = null) : base(elementId, TupleKind.Label, parent) {
            label_ = name;
        }

        public override bool HasName => true;
        public override ReadOnlyMemory<char> NameValue => label_;

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
            return $"label name: {Name}";
        }
    }
}
