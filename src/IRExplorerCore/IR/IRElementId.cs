// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

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
}
