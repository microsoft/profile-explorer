// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Diff;

namespace IRExplorerUI.Compilers.ASM;

public class ASMDiffInputFilter : IDiffInputFilter {
    public char[] AcceptedLetters => new char[] {
        'A', 'B', 'C', 'D', 'E', 'F',
        'a', 'b', 'c', 'd', 'e', 'f'
    };

    private Dictionary<string, int> addressMap_;
    private int nextAddressId_;

    public void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo) {
        addressMap_ = new Dictionary<string, int>();
        nextAddressId_ = 1;
    }

    public FilteredDiffInput FilterInputText(string text) {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var result = new FilteredDiffInput(lines.Length);

        var builder = new StringBuilder(text.Length);
        var linePrefixes = new List<string>(lines.Length);

        foreach (var line in lines) {
            var (newLine, replacements) = FilterInputLineImpl(line);
            builder.AppendLine(newLine);
            result.LineReplacements.Add(replacements);
        }

        result.Text = builder.ToString();
        return result;
    }

    public string FilterInputLine(string line) {
        var (result, _) = FilterInputLineImpl(line);
        return result;
    }

    public (string, List<FilteredDiffInput.Replacement>) FilterInputLineImpl(string line) {
        var newLine = line;
        int index = line.IndexOf(':');

        if (index == -1) {
            return (newLine, FilteredDiffInput.NoReplacements);
        }

        var (address, _) = ParseAddress(line, 0);

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
            var letter = line[index];
            if (!(Char.IsWhiteSpace(letter) || Char.IsDigit(letter) ||
                  Array.IndexOf(AcceptedLetters, letter) != -1)) {
                break;
            }
        }

        // Move back before the opcode starts.
        int afterIndex = index;

        while (index > startIndex && !Char.IsWhiteSpace(line[index - 1])) {
            index--;
        }

        // Check if there is any address found after the opcode
        // and replace it with the canonical form.
        while (afterIndex < line.Length && !Char.IsWhiteSpace(line[afterIndex])) {
            afterIndex++;
        }

        //? TODO: There could be multiple addresses as operands?
        if (afterIndex < line.Length) {
            var (afterAddress, offset) = ParseAddress(line, afterIndex);

            if (afterAddress != null && afterAddress.Length >= 4) {
                int id = addressMap_.GetOrAddValue(afterAddress, nextAddressId_);
                if (id == nextAddressId_) {
                    nextAddressId_++;
                }

                var replacementAddress = id.ToString().PadLeft(afterAddress.Length, 'A');
                newLine = newLine.Replace(afterAddress, replacementAddress); //? TODO: In-place replace?
                replacements.Add(new FilteredDiffInput.Replacement(offset, replacementAddress, afterAddress));
            }
        }

        var newLinePrefix = newLine.Substring(0, index);
        var newLinePrefixReplacement = new string(' ', index);
        newLine = newLinePrefixReplacement + newLine.Substring(index);  //? TODO: In-place replace?
        replacements.Add(new FilteredDiffInput.Replacement(0, newLinePrefixReplacement, newLinePrefix));
        return (newLine, replacements);
    }
        
    private (string, int) ParseAddress(string line, int startOffset) {
        int addressStartOffset = startOffset;
        int offset;

        for (offset = startOffset; offset < line.Length; offset++) {
            var letter = line[offset];

            if (Char.IsWhiteSpace(letter)) {
                addressStartOffset = offset + 1;
                continue;
            }

            if (!(Char.IsDigit(letter) || Array.IndexOf(AcceptedLetters, letter) != -1)) {
                break;
            }
        }

        if (offset > addressStartOffset) {
            return (line.Substring(addressStartOffset, offset - addressStartOffset), addressStartOffset);
        }

        return (null, 0);
    }
}