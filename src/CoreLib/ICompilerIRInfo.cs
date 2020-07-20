using IRExplorerCore.IR;

namespace IRExplorerCore {
    public interface ICompilerIRInfo {
        IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders = true);
        IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders = true);
        IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler);
        IRParsingErrorHandler CreateParsingErrorHandler();

        bool IsCopyInstruction(InstructionIR instr);
        bool IsLoadInstruction(InstructionIR instr);
        bool IsStoreInstruction(InstructionIR instr);
        bool IsCallInstruction(InstructionIR instr);
        bool IsIntrinsicCallInstruction(InstructionIR instr);
        bool IsPhiInstruction(InstructionIR instr);
        IRElement SkipCopyInstruction(InstructionIR instr);
    }
}
