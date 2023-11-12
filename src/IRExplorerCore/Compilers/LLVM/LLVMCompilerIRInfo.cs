// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.LLVM;

public class LLVMCompilerIRInfo : ICompilerIRInfo {
  public IRMode Mode { get; set; }
  public InstrOffsetData InstructionOffsetData => InstrOffsetData.VariableSize(1, 16);

  public IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders) {
    return new LLVMSectionReader(filePath, expectSectionHeaders);
  }

  public IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders) {
    return new LLVMSectionReader(textData, expectSectionHeaders);
  }

  public IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler, long functionSize) {
    return null;
  }

  public IRParsingErrorHandler CreateParsingErrorHandler() {
    return new ParsingErrorHandler();
  }

  public IReachableReferenceFilter CreateReferenceFilter(FunctionIR function) {
    return null;
  }

  public bool IsCopyInstruction(InstructionIR instr) {
    return SkipCopyInstruction(instr) != null;
  }

  public bool IsLoadInstruction(InstructionIR instr) {
    return false;
  }

  public bool IsStoreInstruction(InstructionIR instr) {
    return false;
  }

  public bool IsCallInstruction(InstructionIR instr) {
    return false;
  }

  public OperandIR GetCallTarget(InstructionIR instr) {
    return null;
  }

  public OperandIR GetBranchTarget(InstructionIR instr) {
    return null;
  }

  public bool IsIntrinsicCallInstruction(InstructionIR instr) {
    return false;
  }

  public bool IsPhiInstruction(InstructionIR instr) {
    return false;
  }

  public bool IsNOP(InstructionIR instr) {
    throw new NotImplementedException();
  }

  public BlockIR GetIncomingPhiOperandBlock(InstructionIR phiInstr, int opIndex) {
    return null;
  }

  public IRElement SkipCopyInstruction(InstructionIR instr) {
    return null;
  }

  public bool OperandsReferenceSameSymbol(OperandIR opA, OperandIR opB, bool exactCheck) {
    return false;
  }

  public InstructionIR GetTransferInstruction(BlockIR block) {
    return null;
  }
}
