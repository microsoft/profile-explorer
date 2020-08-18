// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace IRExplorerCore.IR {
    public class IRElementId {
        public ushort BlockId;
        public ushort OperandId;
        public uint TupleId;

        public static IRElementId NewFunctionId() {
            return new IRElementId {
                BlockId = 0xFFFF,
                TupleId = 0xFFFFFFFF,
                OperandId = 0xFFFF
            };
        }

        public IRElementId NewBlock(ushort blockId) {
            BlockId = blockId;
            TupleId = 0;
            OperandId = 0;
            return this;
        }

        public IRElementId NewBlock(int blockId) {
            return NewBlock((ushort)blockId);
        }

        public IRElementId NextTuple() {
            TupleId++;
            OperandId = 0;
            return this;
        }

        public IRElementId NextOperand() {
            OperandId++;
            return this;
        }

        public static implicit operator ulong(IRElementId elementId) {
            return elementId.ToLong();
        }

        public static IRElementId FromLong(ulong value) {
            return new IRElementId {
                OperandId = (ushort)((value & 0xFFFF) >> 0),
                TupleId = (uint)((value & 0xFFFFFFFF0000) >> 32),
                BlockId = (ushort)((value & 0xFFFF000000000000) >> 48)
            };
        }

        public ulong ToLong() {
            return ToLong(blockId: BlockId, tupleId: TupleId, operandId: OperandId);
        }

        public static ulong ToLong(ushort blockId = 0, uint tupleId = 0, ushort operandId = 0) {
            return ((ulong)blockId << 48) | ((ulong)tupleId << 32) | operandId;
        }
    }

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
