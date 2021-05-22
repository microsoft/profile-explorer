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
            //? TODO: Use UTCReferenceFilter, make a DefaultReachabilityFilter out of it
            return null;
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
            if (IsCallInstruction(instr) && instr.Sources.Count > 0) {
                return instr.Sources[0];
            }

            return null;
        }

        public BlockIR GetIncomingPhiOperandBlock(InstructionIR phiInstr, int opIndex) {
            return null;
        }

        public bool IsCallInstruction(InstructionIR instr) {
            return instr.IsCall ||
                   instr.IsBranch && instr.Sources.Count > 0 && instr.Sources[0].IsAddress;
        }

        public bool IsCopyInstruction(InstructionIR instr) {
            return false;
        }

        public bool IsIntrinsicCallInstruction(InstructionIR instr) {
            return false;
        }

        public bool IsLoadInstruction(InstructionIR instr) {
            switch (IRMode) {
                case IRMode.x86: {
                    return instr.Sources.Find((op) => op.IsIndirection) != null;
                }
                case IRMode.ARM64: {
                    //? TODO:
                    return instr.Sources.Find((op) => op.IsIndirection) != null;
                }
            }

            return false;
        }

        public bool IsPhiInstruction(InstructionIR instr) {
            return false;
        }

        public bool IsStoreInstruction(InstructionIR instr) {
            switch (IRMode) {
                case IRMode.x86: {
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
            return false;
        }

        public bool OperandsReferenceSameSymbol(OperandIR opA, OperandIR opB, bool exactCheck) {
            return false;
        }

        public IRElement SkipCopyInstruction(InstructionIR instr) {
            return instr;
        }

        public InstructionIR GetTransferInstruction(BlockIR block) {
            return (block.Tuples.Count > 0 ? block.Tuples[^1] : null) as InstructionIR;
        }
    }
}
