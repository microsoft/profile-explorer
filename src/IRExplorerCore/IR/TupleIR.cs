// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore.Utilities;
using System.Collections.Generic;

namespace IRExplorerCore.IR {
    public enum TupleKind {
        Instruction,
        Label,
        Exception,
        Metadata,
        Other
    }

    public class TupleIR : IRElement {
        public TupleIR(IRElementId elementId, TupleKind kind, BlockIR parent) : base(
            elementId.NextTuple()) {
            Kind = kind;
            Parent = parent;
        }

        public TupleKind Kind { get; set; }
        public BlockIR Parent { get; set; }
        public int BlockIndex { get; set; }

        public bool IsInstruction => Kind == TupleKind.Instruction;
        public bool IsLabel => Kind == TupleKind.Label;
        public bool IsException => Kind == TupleKind.Exception;
        public bool IsMetadata => Kind == TupleKind.Metadata;

        public override void Accept(IRVisitor visitor) {
            visitor.Visit(this);
        }

        public override bool Equals(object obj) {
            return obj is TupleIR tuple &&
                   base.Equals(obj) &&
                   Kind == tuple.Kind &&
                   EqualityComparer<BlockIR>.Default.Equals(Parent, tuple.Parent);
        }

        public override string ToString() {
            return $"tuple kind: {Kind}, id: {Id}";
        }
    }
}
