// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.IR;
using IRExplorerCore.Lexer;

namespace IRExplorerCore {
    public class IRParsingError {
        public IRParsingError(TextLocation location, string error) {
            Location = location;
            Error = error;
        }

        public TextLocation Location { get; set; }
        public string Error { get; set; }

        public override string ToString() {
            return Error;
        }
    }

    public interface IRParsingErrorHandler {
        IRSectionParser Parser { get; set; }
        bool HadParsingErrors { get; set; }
        List<IRParsingError> ParsingErrors { get; }

        bool HandleError(TextLocation location, TokenKind expectedToken, Token actualToken,
                         string message = "");
    }

    public interface IRSectionParser {
        FunctionIR ParseSection(IRTextSection section, string sectionText);
        FunctionIR ParseSection(IRTextSection section, ReadOnlyMemory<char> sectionText);
    }
}
