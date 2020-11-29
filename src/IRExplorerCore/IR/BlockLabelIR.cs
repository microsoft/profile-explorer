// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

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
            return ReferenceEquals(this, obj) || obj is BlockLabelIR other && Equals(other);
        }

        private bool Equals(BlockLabelIR other) {
            return base.Equals(other) && label_.Equals(other.label_);
        }
        
        public override int GetHashCode() {
            return HashCode.Combine(base.GetHashCode(), label_);
        }

        public override string ToString() {
            return $"label name: {Name}";
        }
    }
}
