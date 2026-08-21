// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Linq;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// What kind of thing a resolved RVA turned out to be.
/// </summary>
public enum ReferenceKind {
  /// <summary>Nothing matched -- not an import, export, or a plausible string. Still a valid,
  /// meaningful result (e.g. a raw data constant or an unrecognized reference), not a failure.</summary>
  Unknown,
  ImportedFunction,
  ExportedFunction,
  AsciiString,
  Utf16String,
}

/// <summary>
/// The result of resolving a single RVA (typically a call's resolved memory operand, or a direct
/// branch/call target) to whatever it actually refers to: an imported function (with
/// <see cref="ApiFact"/> attached when the API is documented in <see cref="KnownApiFacts"/>), an
/// export of the same binary, a string literal, or -- explicitly, not silently -- unknown.
/// </summary>
public sealed record ResolvedReference(ReferenceKind Kind, long Rva, string? Name, string? Text, KnownApiFact? ApiFact);

/// <summary>
/// Resolves an arbitrary module RVA (an IAT slot, an export's entry point, or a data reference) to
/// a <see cref="ResolvedReference"/> using only static PE evidence (imports/exports/strings) --
/// never dynamic tracing, and never a PDB requirement (imports/exports are always present
/// regardless of whether symbols are available, which is exactly the third-party/no-PDB case the
/// reverse-engineering enhancement plan is built around). This is what turns an x64
/// "call qword ptr [rip+N]" (see <see cref="SemanticInstruction.MemoryReferenceRva"/>) into a real
/// API identity instead of an opaque address.
/// </summary>
public static class ReferenceResolver {
  public static ResolvedReference Resolve(PEBinaryInfoProvider peInfo, long rva,
                                          IReadOnlyList<ImportedFunctionReference>? imports = null,
                                          IReadOnlyList<ExportedFunctionReference>? exports = null) {
    imports ??= peInfo.GetImportedFunctions();
    exports ??= peInfo.GetExportedFunctions();

    var import = imports.FirstOrDefault(i => i.IatRva == rva);

    if (import != null) {
      KnownApiFacts.TryGetFact(import.FunctionName, out var fact);
      return new ResolvedReference(ReferenceKind.ImportedFunction, rva, import.QualifiedName, null, fact);
    }

    var export = exports.FirstOrDefault(e => !e.IsForwarder && e.Rva == rva);

    if (export != null) {
      return new ResolvedReference(ReferenceKind.ExportedFunction, rva, export.FunctionName, null, null);
    }

    // Prefer UTF-16 (Windows' native wide-string form) over ASCII when both happen to look
    // plausible, since a real ASCII byte sequence can also decode as valid (if nonsensical) UTF-16.
    if (peInfo.TryReadNullTerminatedUtf16String(rva, 260, out string? wide) && IsLikelyDisplayString(wide)) {
      return new ResolvedReference(ReferenceKind.Utf16String, rva, null, wide, null);
    }

    if (peInfo.TryReadNullTerminatedAsciiString(rva, 260, out string? ascii) && IsLikelyDisplayString(ascii)) {
      return new ResolvedReference(ReferenceKind.AsciiString, rva, null, ascii, null);
    }

    return new ResolvedReference(ReferenceKind.Unknown, rva, null, null, null);
  }

  /// <summary>
  /// Conservative printable-text heuristic: every character must be printable ASCII (0x20-0x7E) or
  /// tab, and the string must be at least 3 characters -- avoids misclassifying arbitrary binary
  /// data (e.g. a jump table or a struct) as a string merely because it happened to decode without
  /// error.
  /// </summary>
  private static bool IsLikelyDisplayString(string? value) {
    if (string.IsNullOrEmpty(value) || value.Length < 3) {
      return false;
    }

    foreach (char c in value) {
      if (c != '\t' && (c < 0x20 || c > 0x7E)) {
        return false;
      }
    }

    return true;
  }
}
