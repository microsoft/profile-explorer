// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProfileExplorerCore2.IR;
using ProfileExplorerCore2.Lexer;

namespace ProfileExplorerCore2.Parser;

public interface IRSectionParser {
  void SkipCurrentToken();
  void SkipToLineEnd();
  void SkipToLineStart();
  void SkipToNextBlock();
  void SkipToFunctionEnd();
  FunctionIR ParseSection(IRTextSection section, string sectionText);
  FunctionIR ParseSection(IRTextSection section, ReadOnlyMemory<char> sectionText);
}

public interface IRParsingErrorHandler {
  IRSectionParser Parser { get; set; }
  bool HadParsingErrors { get; set; }
  List<IRParsingError> ParsingErrors { get; }

  bool HandleError(TextLocation location, TokenKind expectedToken, Token actualToken,
                   string message = "");
}

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