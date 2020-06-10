// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using Core.IR;
using Core.Lexer;

namespace Core {
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

        bool HandleError(TextLocation location,
                         TokenKind expectedToken, Token actualToken,
                         string message = "");
    }

    public interface IRSectionParser {
        void SkipCurrentToken();
        void SkipToLineEnd();
        void SkipToLineStart();
        void SkipToNextBlock();
        void SkipToFunctionEnd();

        FunctionIR ParseSection(IRTextSection section, string sectionText);
    }
}
