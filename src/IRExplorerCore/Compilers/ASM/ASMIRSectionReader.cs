// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
namespace IRExplorerCore.ASM;

public sealed class ASMIRSectionReader : SectionReaderBase {
  public ASMIRSectionReader(string filePath, bool expectSectionHeaders) :
    base(filePath, expectSectionHeaders) {
  }

  public ASMIRSectionReader(byte[] textData, bool expectSectionHeaders) :
    base(textData, expectSectionHeaders) {
  }

  protected override string ExtractFunctionName(string line) {
    return line.Substring(0, line.Length - 1);
  }

  protected override string ExtractSectionName(string line) {
    return ExtractFunctionName(line);
  }

  protected override bool IsBlockStart(string line) {
    return false;
  }

  protected override bool IsFunctionEnd(string line) {
    return string.IsNullOrEmpty(line) || IsFunctionStart(line);
  }

  protected override bool IsFunctionStart(string line) {
    // Search for name: with optional whitespace after :
    int index = line.LastIndexOf(':');

    if (index == -1) {
      return false;
    }

    if (index == line.Length - 1) {
      return true;
    }

    for (; index < line.Length; index++) {
      if (!char.IsWhiteSpace(line[index])) {
        return false;
      }
    }

    return true;
  }

  protected override bool IsMetadataLine(string line) {
    return false;
  }

  protected override bool IsSectionStart(string line) {
    return IsFunctionStart(line);
  }

  protected override string PreprocessLine(string line) {
    return line;
  }

  protected override bool ShouldSkipOutputLine(string line) {
    return string.IsNullOrEmpty(line);
  }

  protected override bool FunctionEndIsFunctionStart(string line) {
    return !string.IsNullOrEmpty(line);
  }

  protected override bool SectionStartIsFunctionStart(string line) {
    return true;
  }
}
