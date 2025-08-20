// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;
using ProfileExplorerCore2.Diff;
using ProfileExplorerCore2.Settings;
using ProfileExplorerCore2.Utilities;

namespace ProfileExplorerCore2.Compilers.ASM;

public class ASMDiffInputFilter : IDiffInputFilter {
  private Dictionary<string, int> addressMap_;
  private int nextAddressId_;
  public char[] AcceptedLetters => new[] {
    'A', 'B', 'C', 'D', 'E', 'F',
    'a', 'b', 'c', 'd', 'e', 'f'
  };

  public void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo) {
    addressMap_ = new Dictionary<string, int>();
    nextAddressId_ = 1;
  }

  public FilteredDiffInput FilterInputText(string text) {
    string[] lines = text.SplitLines();
    var result = new FilteredDiffInput(lines.Length);
    var builder = new StringBuilder(text.Length);

    foreach (string line in lines) {
      (string newLine, var replacements) = FilterInputLineImpl(line);
      builder.AppendLine(newLine);
      result.LineReplacements.Add(replacements);
    }

    result.Text = builder.ToString();
    return result;
  }

  public string FilterInputLine(string line) {
    (string result, _) = FilterInputLineImpl(line);
    return result;
  }

  public (string, List<FilteredDiffInput.Replacement>) FilterInputLineImpl(string line) {
    string newLine = line;
    int index = line.IndexOf(':');

    if (index == -1) {
      return (newLine, FilteredDiffInput.NoReplacements);
    }

    (string address, _) = ParseAddress(line, 0);

    if (address == null) {
      return (newLine, FilteredDiffInput.NoReplacements);
    }

    var replacements = new List<FilteredDiffInput.Replacement>(1);

    // Create a canonical form for the address so that diffing considers
    // the addresses equal if the target blocks are the same in two functs.
    // where the function start is not the same anymore.
    if (addressMap_.GetOrAddValue(address, nextAddressId_) == nextAddressId_) {
      nextAddressId_++;
    }

    // Skip over the bytecodes found before the opcode.
    int startIndex = index;

    for (index = index + 1; index < line.Length; index++) {
      char letter = line[index];

      if (!(char.IsWhiteSpace(letter) || char.IsDigit(letter) ||
            Array.IndexOf(AcceptedLetters, letter) != -1)) {
        break;
      }
    }

    // Move back before the opcode starts.
    int afterIndex = index;

    while (index > startIndex && !char.IsWhiteSpace(line[index - 1])) {
      index--;
    }

    // Check if there is any address found after the opcode
    // and replace it with the canonical form.
    while (afterIndex < line.Length && !char.IsWhiteSpace(line[afterIndex])) {
      afterIndex++;
    }

    //? TODO: There could be multiple addresses as operands?
    if (afterIndex < line.Length) {
      (string afterAddress, int offset) = ParseAddress(line, afterIndex);

      if (afterAddress != null && afterAddress.Length >= 4) {
        int id = addressMap_.GetOrAddValue(afterAddress, nextAddressId_);

        if (id == nextAddressId_) {
          nextAddressId_++;
        }

        string replacementAddress = id.ToString().PadLeft(afterAddress.Length, 'A');
        newLine = newLine.Replace(afterAddress, replacementAddress);
        replacements.Add(new FilteredDiffInput.Replacement(offset, replacementAddress, afterAddress));
      }
    }

    string newLinePrefix = newLine.Substring(0, index);
    string newLinePrefixReplacement = new(' ', index);
    newLine = newLinePrefixReplacement + newLine.Substring(index);
    replacements.Add(new FilteredDiffInput.Replacement(0, newLinePrefixReplacement, newLinePrefix));
    return (newLine, replacements);
  }

  private (string, int) ParseAddress(string line, int startOffset) {
    int addressStartOffset = startOffset;
    int offset;

    for (offset = startOffset; offset < line.Length; offset++) {
      char letter = line[offset];

      if (char.IsWhiteSpace(letter)) {
        addressStartOffset = offset + 1;
        continue;
      }

      if (!(char.IsDigit(letter) || Array.IndexOf(AcceptedLetters, letter) != -1)) {
        break;
      }
    }

    if (offset > addressStartOffset) {
      return (line.Substring(addressStartOffset, offset - addressStartOffset), addressStartOffset);
    }

    return (null, 0);
  }
}