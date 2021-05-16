// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore.IR;
using IRExplorerCore.Analysis;

namespace IRExplorerCore {
    public class InstrOffsetData {
        public int OffsetAdjustIncrement { get; private set; }
        public int MaxOffsetAdjust { get; private set; }

        public static InstrOffsetData PointsToNextInstr() => new InstrOffsetData() {
            OffsetAdjustIncrement = 0,
            MaxOffsetAdjust = 0
        };

        public static InstrOffsetData ConstantSize(int size) => new InstrOffsetData() {
            OffsetAdjustIncrement = size,
            MaxOffsetAdjust = size
        };

        public static InstrOffsetData VariableSize(int minSize, int maxSize) => new InstrOffsetData() {
            OffsetAdjustIncrement = minSize,
            MaxOffsetAdjust = maxSize
        };
    }

    public interface ICompilerIRInfo {
        IRMode IRMode { get; set; }
        IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders = true);
        IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders = true);
        IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler);
        IRParsingErrorHandler CreateParsingErrorHandler();
        IReachableReferenceFilter CreateReferenceFilter(FunctionIR function);
        InstrOffsetData InstrOffsetData { get; }
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
        InstructionIR GetTransferInstruction(BlockIR block);

        bool OperandsReferenceSameSymbol(OperandIR opA, OperandIR opB, bool exactCheck);
    }
}
