using IRExplorerCore.IR;

namespace IRExplorerCore {
    public interface ICompilerIRInfo {
        bool IsCopyInstruction(InstructionIR instr);
        bool IsLoadInstruction(InstructionIR instr);
        bool IsStoreInstruction(InstructionIR instr);
        bool IsCallInstruction(InstructionIR instr);
        bool IsIntrinsicCallInstruction(InstructionIR instr);
        bool IsPhiInstruction(InstructionIR instr);
        IRElement SkipCopyInstruction(InstructionIR instr);
    }
}
