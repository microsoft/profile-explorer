// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Core.IR;

public interface IRVisitor {
  void Visit(IRElement value);
  void Visit(OperandIR value);
  void Visit(TupleIR value);
  void Visit(BlockLabelIR value);
  void Visit(InstructionIR value);
  void Visit(BlockIR value);
  void Visit(FunctionIR value);
}