﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorerCore.Analysis;
using ProfileExplorerCore.Compilers.Architecture;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.Parser;

namespace ProfileExplorerCore;

public interface ICompilerIRInfo {
  IRMode Mode { get; set; }
  InstructionOffsetData InstructionOffsetData { get; }
  IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders = true);
  IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders = true);
  IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler, long functionSize = 0);
  IRParsingErrorHandler CreateParsingErrorHandler();
  IReachableReferenceFilter CreateReferenceFilter(FunctionIR function);
  bool IsCopyInstruction(InstructionIR instr);
  bool IsLoadInstruction(InstructionIR instr);
  bool IsStoreInstruction(InstructionIR instr);
  bool IsCallInstruction(InstructionIR instr);
  bool IsIntrinsicCallInstruction(InstructionIR instr);
  bool IsPhiInstruction(InstructionIR instr);
  bool IsNOP(InstructionIR instr);
  BlockIR GetIncomingPhiOperandBlock(InstructionIR phiInstr, int opIndex);
  IRElement SkipCopyInstruction(InstructionIR instr);
  OperandIR GetCallTarget(InstructionIR instr);
  OperandIR GetBranchTarget(InstructionIR instr);
  InstructionIR GetTransferInstruction(BlockIR block);
  bool OperandsReferenceSameSymbol(OperandIR opA, OperandIR opB, bool exactCheck);
}

public class InstructionOffsetData {
  public int OffsetAdjustIncrement { get; set; }
  public int MaxOffsetAdjust { get; set; }
  public int InitialMultiplier { get; set; }

  public static InstructionOffsetData ConstantSize(int size) {
    return new InstructionOffsetData {
      OffsetAdjustIncrement = size,
      MaxOffsetAdjust = size,
      InitialMultiplier = 1
    };
  }

  public static InstructionOffsetData VariableSize(int minSize, int maxSize) {
    return new InstructionOffsetData {
      OffsetAdjustIncrement = minSize,
      MaxOffsetAdjust = maxSize,
      InitialMultiplier = 1
    };
  }
}