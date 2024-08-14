// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;
using ProfileExplorer.Core.Lexer;

namespace ProfileExplorer.Core;

public class ParsingErrorHandler : IRParsingErrorHandler {
  public ParsingErrorHandler() {
    ParsingErrors = new List<IRParsingError>();
  }

  public bool ThrowOnError { get; set; }
  public IRSectionParser Parser { get; set; }
  public bool HadParsingErrors { get; set; }
  public List<IRParsingError> ParsingErrors { get; set; }

  public bool HandleError(TextLocation location, TokenKind expectedToken,
                          Token actualToken, string message = "") {
    var builder = new StringBuilder();

    if (!string.IsNullOrEmpty(message)) {
      builder.AppendLine(message);
    }

    builder.AppendLine($"Location: {location}");
    builder.AppendLine($"Expected token: {expectedToken}");
    builder.Append($"Actual token: {actualToken}");

    if (ThrowOnError) {
      throw new InvalidOperationException($"IR parsing error:\n{builder}");
    }

    ParsingErrors.Add(new IRParsingError(location, builder.ToString()));
    HadParsingErrors = true;
    return true; // Always continue parsing.
  }
}