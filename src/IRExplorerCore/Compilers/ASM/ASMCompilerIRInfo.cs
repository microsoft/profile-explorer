// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;
using IRExplorerCore.ASM;
using IRExplorerCore.IR;
using IRExplorerCore.Analysis;
using System;

namespace IRExplorerCore.ASM {
    public sealed class ASMCompilerIRInfo : ICompilerIRInfo {
        public IRMode Mode { get; set; }

        public ASMCompilerIRInfo(IRMode mode) {
            Mode = mode;
        }

        public InstrOffsetData InstructionOffsetData => Mode switch {
            IRMode.ARM64 => InstrOffsetData.ConstantSize(4),
            _ => InstrOffsetData.VariableSize(1, 16),
        };

        public IRParsingErrorHandler CreateParsingErrorHandler() => new ParsingErrorHandler();

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
            if(!(instr.IsBranch || instr.IsGoto) || 
                 instr.Sources.Count == 0) {
                return null;
            }

            switch (Mode) {
                case IRMode.x86_64: {
                    return instr.Sources[0];
                }
                case IRMode.ARM64: {
                    if(!(instr.Opcode is ARMOpcode)) {
                        return null;
                    }

                    switch (instr.OpcodeAs<ARMOpcode>()) {
                        case ARMOpcode.CBZ:
                        case ARMOpcode.CBNZ: {
                            if (instr.Sources.Count == 2) {
                                return instr.Sources[1];
                            }
                            break;
                        }
                        case ARMOpcode.TBZ:
                        case ARMOpcode.TBNZ: {
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
                   (instr.IsGoto && instr.Sources.Count > 0 &&
                   !instr.Sources[0].IsLabelAddress &&
                    instr.Sources[0].HasName);
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
                    return instr.Sources.Find((op) => op.IsIndirection) != null;
                }
                case IRMode.ARM64: {
                    //? TODO: Use opcodes like LDP
                    return instr.Sources.Find((op) => op.IsIndirection) != null;
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
}
