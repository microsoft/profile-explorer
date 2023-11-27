// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
namespace IRExplorerCore.IR;

public class IRElementId {
  public ushort BlockId;
  public ushort OperandId;
  public uint TupleId;

  public static implicit operator ulong(IRElementId elementId) {
    return elementId.ToLong();
  }

  public static IRElementId NewFunctionId() {
    return new IRElementId {
      BlockId = 0xFFFF,
      TupleId = 0xFFFFFFFF,
      OperandId = 0xFFFF
    };
  }

  public static IRElementId FromLong(ulong value) {
    return new IRElementId {
      OperandId = (ushort)((value & 0xFFFF) >> 0),
      TupleId = (uint)((value & 0xFFFFFFFF0000) >> 32),
      BlockId = (ushort)((value & 0xFFFF000000000000) >> 48)
    };
  }

  public static ulong ToLong(ushort blockId, uint tupleId = 0, ushort operandId = 0) {
    return (ulong)blockId << 48 | (ulong)tupleId << 32 | operandId;
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

  public ulong ToLong() {
    return ToLong(BlockId, TupleId, OperandId);
  }

  private IRElementId NewBlock(ushort blockId) {
    BlockId = blockId;
    TupleId = 0;
    OperandId = 0;
    return this;
  }
}
