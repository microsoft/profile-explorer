using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.IR;

namespace IRExplorerCore.ASM {

    public sealed class ASMIRSectionParser : IRSectionParser {
        private readonly ICompilerIRInfo irInfo_;
        private readonly IRParsingErrorHandler errorHandler_;
        private Dictionary<long, string> funcAddressMap_;
        
        public ASMIRSectionParser(ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler) {
            irInfo_ = irInfo;
            errorHandler_ = errorHandler;
        }

        public FunctionIR ParseSection(IRTextSection section, string sectionText) => new ASMParser(
                irInfo_, errorHandler_,
                RegisterTables.SelectRegisterTable(irInfo_.Mode),
                sectionText, funcAddressMap_).Parse();

        public FunctionIR ParseSection(IRTextSection section, ReadOnlyMemory<char> sectionText) => new ASMParser(
                irInfo_, errorHandler_,
                RegisterTables.SelectRegisterTable(irInfo_.Mode),
                sectionText, funcAddressMap_).Parse();

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
