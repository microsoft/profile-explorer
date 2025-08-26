// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Parser;

namespace ProfileExplorer.Core.Compilers.ASM;

public sealed class ASMCompilerIRInfo : ICompilerIRInfo {
  public ASMCompilerIRInfo(IRMode mode) {
    Mode = mode;
  }

  public IRMode Mode { get; set; }
  public InstructionOffsetData InstructionOffsetData => Mode switch {
    IRMode.ARM64 => InstructionOffsetData.ConstantSize(4),
    _            => InstructionOffsetData.VariableSize(1, 16)
  };

  public IRParsingErrorHandler CreateParsingErrorHandler() {
    return new ParsingErrorHandler();
  }

  public IReachableReferenceFilter CreateReferenceFilter(FunctionIR function) {
    return new CFGReachabilityReferenceFilter(function);
  }

  public IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler, long functionSize) {
    return new ASMIRSectionParser(functionSize, this, errorHandler);
  }

  public IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders = true) {
    return new ASMIRSectionReader(filePath, expectSectionHeaders);
  }

  public IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders = true) {
    return new ASMIRSectionReader(textData, expectSectionHeaders);
  }

  public OperandIR GetCallTarget(InstructionIR instr) {
    if (IsCallInstruction(instr) && instr.Sources.Count > 0) {
      return instr.Sources[0];
    }

    return null;
  }

  public OperandIR GetBranchTarget(InstructionIR instr) {
    if (!(instr.IsBranch || instr.IsGoto) ||
        instr.Sources.Count == 0) {
      return null;
    }

    switch (Mode) {
      case IRMode.x86_64: {
        return instr.Sources[0];
      }
      case IRMode.ARM64: {
        if (!(instr.Opcode is ARM64Opcode)) {
          return null;
        }

        switch (instr.OpcodeAs<ARM64Opcode>()) {
          case ARM64Opcode.CBZ:
          case ARM64Opcode.CBNZ: {
            if (instr.Sources.Count == 2) {
              return instr.Sources[1];
            }

            break;
          }
          case ARM64Opcode.TBZ:
          case ARM64Opcode.TBNZ: {
            if (instr.Sources.Count == 3) {
              return instr.Sources[2];
            }

            break;
          }
          default: {
            return instr.Sources[0];
          }
        }

        break;
      }
    }

    return null;
  }

  public BlockIR GetIncomingPhiOperandBlock(InstructionIR phiInstr, int opIndex) {
    return null;
  }

  public bool IsCallInstruction(InstructionIR instr) {
    return instr.IsCall ||
           instr.IsGoto && instr.Sources.Count > 0 &&
           !instr.Sources[0].IsLabelAddress;
  }

  public bool IsCopyInstruction(InstructionIR instr) {
    return false;
  }

  public bool IsIntrinsicCallInstruction(InstructionIR instr) {
    return false;
  }

  public bool IsLoadInstruction(InstructionIR instr) {
    switch (Mode) {
      case IRMode.x86_64: {
        return instr.Sources.Find(op => op.IsIndirection) != null;
      }
      case IRMode.ARM64: {
        //? TODO: Use opcodes like LDP
        return instr.Sources.Find(op => op.IsIndirection) != null;
      }
    }

    return false;
  }

  public bool IsPhiInstruction(InstructionIR instr) {
    return false;
  }

  public bool IsStoreInstruction(InstructionIR instr) {
    switch (Mode) {
      case IRMode.x86_64: {
        return instr.Destinations.Count > 0 &&
               instr.Destinations[0].IsIndirection;
      }
      case IRMode.ARM64: {
        //? TODO
        return instr.Destinations.Count > 0 &&
               instr.Destinations[0].IsIndirection;
      }
    }

    return false;
  }

  public bool IsNOP(InstructionIR instr) {
    if (instr.Opcode == null) {
      return false;
    }

    switch (Mode) {
      case IRMode.x86_64: {
        return instr.OpcodeAs<x86Opcode>() == x86Opcode.NOP;
      }
      case IRMode.ARM64: {
        return instr.OpcodeAs<ARM64Opcode>() == ARM64Opcode.NOP;
      }
    }

    return false;
  }

  public bool OperandsReferenceSameSymbol(OperandIR opA, OperandIR opB, bool exactCheck) {
    //? TODO: This should do a register check too, ARM64 not implemented
    return opA.Kind == opB.Kind &&
           opA.HasName && opB.HasName &&
           opA.NameValue.Span.Equals(opB.NameValue.Span, StringComparison.Ordinal);
  }

  public IRElement SkipCopyInstruction(InstructionIR instr) {
    return instr;
  }

  public InstructionIR GetTransferInstruction(BlockIR block) {
    return (block.Tuples.Count > 0 ? block.Tuples[^1] : null) as InstructionIR;
  }
}