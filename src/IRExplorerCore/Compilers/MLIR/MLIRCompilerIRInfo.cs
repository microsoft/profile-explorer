﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.MLIR {
    public class MLIRCompilerIRInfo : ICompilerIRInfo {
        public IRMode Mode { get; set; }

        public InstrOffsetData InstructionOffsetData => InstrOffsetData.VariableSize(1, 16);

        public IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders) {
            return new MLIRSectionReader(filePath, false);
        }

        public IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders) {
            return new MLIRSectionReader(textData, false);
        }

        public IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler, long functionSize) {
            return new MLIRSectionParser(this, errorHandler);
        }

        public IRParsingErrorHandler CreateParsingErrorHandler() {
            return new ParsingErrorHandler();
        }

        public IReachableReferenceFilter CreateReferenceFilter(FunctionIR function) {
            return null;
        }

        public bool IsCopyInstruction(InstructionIR instr) {
            return MemoryExtensions.Contains(instr.OpcodeText.Span, "copy", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsLoadInstruction(InstructionIR instr) {
            return MemoryExtensions.Contains(instr.OpcodeText.Span, "load", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsStoreInstruction(InstructionIR instr) {
            return MemoryExtensions.Contains(instr.OpcodeText.Span, "store", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCallInstruction(InstructionIR instr) {
            return MemoryExtensions.Contains(instr.OpcodeText.Span, "call", StringComparison.OrdinalIgnoreCase) ||
                   MemoryExtensions.Contains(instr.OpcodeText.Span, "launch", StringComparison.OrdinalIgnoreCase);
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
}