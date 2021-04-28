using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.IR;

namespace IRExplorerCore.ASM {

    public sealed class ASMIRSectionParser : IRSectionParser {
        private readonly IRMode irMode_;
        private readonly IRParsingErrorHandler errorHandler_;

        public ASMIRSectionParser(IRMode irMode, IRParsingErrorHandler errorHandler) {
            irMode_ = irMode;
            errorHandler_ = errorHandler;
        }

        public FunctionIR ParseSection(IRTextSection section, string sectionText) => new ASMParser(
                errorHandler_,
                RegisterTables.SelectRegisterTable(irMode_),
                sectionText).Parse();

        public FunctionIR ParseSection(IRTextSection section, ReadOnlyMemory<char> sectionText) => new ASMParser(
                errorHandler_,
                RegisterTables.SelectRegisterTable(irMode_),
                sectionText).Parse();

        public void SkipCurrentToken() {
            throw new NotImplementedException();
        }

        public void SkipToFunctionEnd() {
            throw new NotImplementedException();
        }

        public void SkipToLineEnd() {
            throw new NotImplementedException();
        }

        public void SkipToLineStart() {
            throw new NotImplementedException();
        }

        public void SkipToNextBlock() {
            throw new NotImplementedException();
        }
    }
}
