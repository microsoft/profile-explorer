// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;
using IRExplorerCore.ASM;
using IRExplorerCore.IR;
using IRExplorerCore.Analysis;
using System;

namespace IRExplorerCore.ASM {
    public class ASMCompilerIRInfo : ICompilerIRInfo {
        public IRMode IRMode { get; set; }

        public InstrOffsetData InstrOffsetData => IRMode switch {
            IRMode.ARM64 => InstrOffsetData.ConstantSize(4),
            _ => InstrOffsetData.VariableSize(1, 16),
        };

        public IRParsingErrorHandler CreateParsingErrorHandler() => new ParsingErrorHandler();

        public IReachableReferenceFilter CreateReferenceFilter(FunctionIR function) {
            throw new NotImplementedException();
        }

        public IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler) {
            return new ASMIRSectionParser(IRMode, errorHandler);
        }

        public IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders = true) {
            return new ASMIRSectionReader(filePath, expectSectionHeaders);
        }

        public IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders = true) {
            return new ASMIRSectionReader(textData, expectSectionHeaders);
        }

        public OperandIR GetCallTarget(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public BlockIR GetIncomingPhiOperandBlock(InstructionIR phiInstr, int opIndex) {
            throw new NotImplementedException();
        }

        public bool IsCallInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsCopyInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsIntrinsicCallInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsLoadInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsPhiInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsStoreInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool IsNOP(InstructionIR instr) {
            throw new NotImplementedException();
        }

        public bool OperandsReferenceSameSymbol(OperandIR opA, OperandIR opB, bool exactCheck) {
            throw new NotImplementedException();
        }

        public IRElement SkipCopyInstruction(InstructionIR instr) {
            throw new NotImplementedException();
        }
    }
}
