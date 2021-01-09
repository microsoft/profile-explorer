// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.LLVM {
    public class LLVMCompilerIRInfo : ICompilerIRInfo {
        public IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders) {
            return new LLVMSectionReader(filePath, expectSectionHeaders);
        }

        public IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders) {
            return new LLVMSectionReader(textData, expectSectionHeaders);
        }

        public IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler) {
            return new LLVMSectionParser((ParsingErrorHandler)errorHandler);
        }

        public IRParsingErrorHandler CreateParsingErrorHandler() {
            return new ParsingErrorHandler();
        }

        public IReachableReferenceFilter CreateReferenceFilter(FunctionIR function) {
            return null;
        }

        public string GetBlockName(BlockIR block) {
            return block.HasLabel ? block.Label.Name : "ENTRY";
        }

        public string GetBlockLabelName(BlockIR block) {
            // return block.HasLabel ? block.Label.Name : "";
            return "";
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

        public bool IsIntrinsicCallInstruction(InstructionIR instr) {
            return false;
        }

        public bool IsPhiInstruction(InstructionIR instr) {
            return false;
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
    }
}
