// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace IRExplorerCore.IR {
    public class IRElement : TaggedObject {
        public IRElement(IRElementId elementId) {
            Id = elementId;
            TextLocation = default;
            TextLength = 0;
            Tags = null;
        }

        public IRElement(TextLocation location, int length) {
            TextLocation = location;
            TextLength = length;
            Tags = null;
        }

        public ulong Id { get; set; }
        public TextLocation TextLocation { get; set; }
        public int TextLength { get; set; }

        public ulong BlockId => IRElementId.FromLong(Id).BlockId;
        public ulong TupleId => IRElementId.FromLong(Id).TupleId;
        public ulong OperandId => IRElementId.FromLong(Id).OperandId;

        public TupleIR ParentTuple {
            get {
                if (this is TupleIR) {
                    return this as TupleIR;
                }
                else if (this is OperandIR) {
                    return ((OperandIR)this).Parent;
                }

                return null;
            }
        }

        public InstructionIR ParentInstruction {
            get {
                if (this is InstructionIR) {
                    return this as InstructionIR;
                }
                else if (this is OperandIR) {
                    return ((OperandIR)this).Parent as InstructionIR;
                }

                return null;
            }
        }

        public BlockIR ParentBlock {
            get {
                if (this is BlockIR) {
                    return this as BlockIR;
                }

                return ParentTuple?.Parent;
            }
        }

        public FunctionIR ParentFunction {
            get {
                var block = ParentBlock;
                return block?.Parent;
            }
        }

        public virtual bool HasName => false;
        public virtual ReadOnlyMemory<char> NameValue => null;
        public virtual string Name => NameValue.ToString();

        public void SetTextRange(TextLocation location, int length) {
            TextLocation = location;
            TextLength = length;
        }

        public ReadOnlyMemory<char> GetText(string source) {
            return source.AsMemory(TextLocation.Offset, TextLength);
        }

        public virtual void Accept(IRVisitor visitor) {
            visitor.Visit(this);
        }

        public override bool Equals(object obj) {
            return obj is IRElement element && Id == element.Id;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Id);
        }
    }
}
