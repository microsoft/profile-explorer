// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.UTC;

public sealed class UTCCompilerIRInfo : ICompilerIRInfo {
  public IRMode Mode { get; set; }
  public InstrOffsetData InstructionOffsetData => InstrOffsetData.PointsToNextInstr();

  public IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders) {
    return new UTCSectionReader(filePath, expectSectionHeaders);
  }

  public IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders) {
    return new UTCSectionReader(textData, expectSectionHeaders);
  }

  public IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler, long functionSize) {
    return new UTCSectionParser(this, (ParsingErrorHandler)errorHandler);
  }

  public IRParsingErrorHandler CreateParsingErrorHandler() {
    return new ParsingErrorHandler();
  }

  public IReachableReferenceFilter CreateReferenceFilter(FunctionIR function) {
    return new UTCReferenceFilter(function);
  }

  public bool IsCopyInstruction(InstructionIR instr) {
    return SkipCopyInstruction(instr) != null;
  }

  public bool IsLoadInstruction(InstructionIR instr) {
    if (instr.OpcodeIs(UTCOpcode.OPLOAD)) {
      return true;
    }

    if (instr.OpcodeIs(UTCOpcode.OPASSIGN) || instr.OpcodeIs(UTCOpcode.OPMBASSIGN)) {
      return instr.Sources.Count > 0 && instr.Sources[0].IsIndirection;
    }

    return false;
  }

  public bool IsStoreInstruction(InstructionIR instr) {
    if (instr.OpcodeIs(UTCOpcode.OPASSIGN) || instr.OpcodeIs(UTCOpcode.OPMBASSIGN)) {
      return instr.Destinations.Count > 0 && instr.Destinations[0].IsIndirection;
    }

    return false;
  }

  public bool IsCallInstruction(InstructionIR instr) {
    return instr.OpcodeIs(UTCOpcode.OPCALL);
  }

  public OperandIR GetCallTarget(InstructionIR instr) {
    if (!instr.OpcodeIs(UTCOpcode.OPCALL)) {
      return null;
    }

    return instr.Sources.Count > 0 ? instr.Sources[0] : null;
  }

  public OperandIR GetBranchTarget(InstructionIR instr) {
    if (!(instr.IsBranch || instr.IsGoto) ||
        instr.Sources.Count == 0) {
      return null;
    }

    return instr.Sources[0];
  }

  public bool IsIntrinsicCallInstruction(InstructionIR instr) {
    return instr.OpcodeIs(UTCOpcode.OPINTRINSIC);
  }

  public bool IsPhiInstruction(InstructionIR instr) {
    return instr.OpcodeIs(UTCOpcode.OPPHI);
  }

  public BlockIR GetIncomingPhiOperandBlock(InstructionIR phiInstr, int opIndex) {
    if (!IsPhiInstruction(phiInstr)) {
      return null;
    }

    var block = phiInstr.ParentBlock;
    return opIndex < block.Predecessors.Count ? block.Predecessors[opIndex] : null;
  }

  public bool IsNOP(InstructionIR instr) {
    throw new NotImplementedException();
  }

  public IRElement SkipCopyInstruction(InstructionIR instr) {
    if (!instr.OpcodeIs(UTCOpcode.OPASSIGN)) {
      return null;
    }

    var sourceOp = instr.Sources[0];
    var defOp = ReferenceFinder.GetSSADefinition(sourceOp);

    if (defOp is OperandIR destOp && Equals(sourceOp.Type, destOp.Type)) {
      return defOp;
    }

    return null;
  }

  public bool OperandsReferenceSameSymbol(OperandIR opA, OperandIR opB, bool exactCheck) {
    if (exactCheck) {
      long offsetA = FindSymbolOffset(opA);
      long offsetB = FindSymbolOffset(opB);

      //? TODO: Option for overlap check?
      if (offsetA != offsetB) {
        return false;
      }
    }

    if (opA.HasName && opB.HasName &&
        opA.NameValue.Span.Equals(opB.NameValue.Span, StringComparison.Ordinal)) {
      return true;
    }

    // Accept t100/tv100/hv100 as the same symbol.
    return opA.IsTemporary && opB.IsTemporary &&
           UTCParser.IsTemporary(opA.NameValue.Span, out int opAId) &&
           UTCParser.IsTemporary(opB.NameValue.Span, out int opBId) &&
           opAId == opBId;
  }

  public InstructionIR GetTransferInstruction(BlockIR block) {
    foreach (var tuple in block.TuplesBack) {
      if (tuple is InstructionIR instr &&
          (instr.IsBranch || instr.IsSwitch || instr.IsReturn)) {
        return instr;
      }
    }

    return null;
  }

  private long FindSymbolOffset(OperandIR op) {
    var offsetTag = op.GetTag<SymbolOffsetTag>();
    return offsetTag?.Offset ?? 0;
  }
}
