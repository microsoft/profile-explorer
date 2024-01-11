// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore;

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