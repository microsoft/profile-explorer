using System.Diagnostics;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.UTC {
    public class UTCCompilerIRInfo : ICompilerIRInfo {
        public IRSectionReader CreateSectionReader(string filePath, bool expectSectionHeaders) {
            return new UTCSectionReader(filePath, expectSectionHeaders);
        }

        public IRSectionReader CreateSectionReader(byte[] textData, bool expectSectionHeaders) {
            return new UTCSectionReader(textData, expectSectionHeaders);
        }

        public IRSectionParser CreateSectionParser(IRParsingErrorHandler errorHandler) {
            return new UTCSectionParser((ParsingErrorHandler)errorHandler);
        }

        public IRParsingErrorHandler CreateParsingErrorHandler() {
            return new ParsingErrorHandler();
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

        public bool IsIntrinsicCallInstruction(InstructionIR instr) {
            return instr.OpcodeIs(UTCOpcode.OPINTRINSIC);
        }

        public bool IsPhiInstruction(InstructionIR instr) {
            return instr.OpcodeIs(UTCOpcode.OPPHI);
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
    }
}
