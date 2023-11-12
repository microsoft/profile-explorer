// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
namespace IRExplorerCore.IR;

public interface IRVisitor {
  void Visit(IRElement value);
  void Visit(OperandIR value);
  void Visit(TupleIR value);
  void Visit(BlockLabelIR value);
  void Visit(InstructionIR value);
  void Visit(BlockIR value);
  void Visit(FunctionIR value);
}
