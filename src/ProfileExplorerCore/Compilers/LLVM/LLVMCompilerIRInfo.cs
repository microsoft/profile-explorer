// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.Core.LLVM;

public class LLVMCompilerIRInfo : ICompilerIRInfo {
  public IRMode Mode { get; set; }
  public InstructionOffsetData InstructionOffsetData => InstructionOffsetData.VariableSize(1, 16);

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