using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.LLVM;

public class MLIRGraphPrinterNameProvider : GraphPrinterNameProvider {
    public override string GetBlockNodeLabel(BlockIR block) {
        return $"^bb{block.Number}";
    }

    public override string GetInstructionNodeLabel(InstructionIR instr, bool appendVarNames, bool appendSSANumber) {
        string label = instr.OpcodeText.ToString();

        if (appendVarNames && instr.Destinations.Count > 0) {
            var destOp = instr.Destinations[0];
            var variableName = GetOperandNodeLabel(destOp, appendSSANumber);

            if (!string.IsNullOrEmpty(variableName)) {
                if (string.IsNullOrEmpty(label)) {
                    if (instr.Kind == InstructionKind.BlockArgumentsMerge) {
                        return $"{variableName} = blockarg";
                    }
                    else {
                        return variableName;
                    }
                }

                return $"{variableName} = {label}";
            }
        }

        return label;

        return base.GetInstructionNodeLabel(instr, appendVarNames, appendSSANumber);
    }

    public override string GetOperandNodeLabel(OperandIR operand, bool appendSSANumber) {
        return operand.HasName ? operand.Name : "";
    }
}