// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;

namespace IRExplorerCore.UTC;

public sealed class UTCSectionReader : SectionReaderBase {
  private const string SectionStartLine = "*********************";
  private static readonly char[] WhitespaceChars = {' ', '\t'};
  private static readonly string[] SeparatorLines = {
    "# # # # # # # # # # # # # # # # # # # # # # # # # # # # # #",
    "***********************************************************"
  };

  public UTCSectionReader(string filePath, bool expectSectionHeaders = true) :
    base(filePath, expectSectionHeaders) {
  }

  public UTCSectionReader(byte[] textData, bool expectSectionHeaders = true) :
    base(textData, expectSectionHeaders) {
  }

  protected override bool IsSectionStart(string line) {
    return line.Equals(SectionStartLine, StringComparison.Ordinal);
  }

  protected override bool IsFunctionStart(string line) {
    return line.StartsWith("ENTRY", StringComparison.Ordinal);
  }

  protected override bool IsBlockStart(string line) {
    return line.StartsWith("BLOCK", StringComparison.Ordinal);
  }

  protected override bool IsFunctionEnd(string line) {
    return line.StartsWith("EXIT", StringComparison.Ordinal);
  }

  protected override string ExtractSectionName(string line) {
    string sectionName = PreviousLine(1);

    if (sectionName == null) {
      return string.Empty;
    }

    if (sectionName.StartsWith(SeparatorLines[0], StringComparison.Ordinal) ||
        sectionName.StartsWith(SeparatorLines[1], StringComparison.Ordinal)) {
      sectionName = PreviousLine(2);
    }

    return sectionName != null ? sectionName.Trim() : string.Empty;
  }

  protected override string ExtractFunctionName(string line) {
    string[] parts = line.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries);
    return parts.Length >= 2 ? parts[1].Trim() : string.Empty;
  }

  protected override string PreprocessLine(string line) {
    // Convert "DDD>actual line text" -> "actual line text"
    // Convert "DDD: actual line text" -> "actual line text"
    if (string.IsNullOrEmpty(line) || !char.IsDigit(line[0])) {
      return line;
    }

    for (int i = 1; i < line.Length; i++) {
      if (line[i] == '>' && i < line.Length - 1) {
        MarkPreprocessedLine(i);
        return line.Substring(i + 1);
      }

      if (line[i] == ':' && i < line.Length - 2) {
        MarkPreprocessedLine(i);
        return line.Substring(i + 2);
      }

      if (!char.IsDigit(line[i])) {
        break;
      }
    }

    return line;
  }

  protected override bool ShouldSkipOutputLine(string line) {
    return string.IsNullOrWhiteSpace(line) ||
           line.StartsWith(SectionStartLine, StringComparison.Ordinal);
  }

  protected override bool IsMetadataLine(string line) {
    return line.StartsWith("/// irx:", StringComparison.Ordinal);
  }

  protected override bool FunctionEndIsFunctionStart(string line) {
    return false;
  }

  protected override bool SectionStartIsFunctionStart(string line) {
    return false;
  }
}
