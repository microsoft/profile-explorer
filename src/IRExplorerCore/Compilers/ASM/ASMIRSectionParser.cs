// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.IR;

namespace IRExplorerCore.ASM {

    public sealed class ASMIRSectionParser : IRSectionParser {
        private ICompilerIRInfo irInfo_;
        private IRParsingErrorHandler errorHandler_;
        private long functionSize_;
        
        public ASMIRSectionParser(long functionSize, ICompilerIRInfo irInfo, IRParsingErrorHandler errorHandler) {
            functionSize_ = functionSize;
            irInfo_ = irInfo;
            errorHandler_ = errorHandler;
        }

        public FunctionIR ParseSection(IRTextSection section, string sectionText) => 
            new ASMParser(irInfo_, errorHandler_,
                          RegisterTables.SelectRegisterTable(irInfo_.Mode),
                          sectionText, section, functionSize_).Parse();

        public FunctionIR ParseSection(IRTextSection section, ReadOnlyMemory<char> sectionText) => 
            new ASMParser(irInfo_, errorHandler_,
                          RegisterTables.SelectRegisterTable(irInfo_.Mode),
                          sectionText, section, functionSize_).Parse();

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
