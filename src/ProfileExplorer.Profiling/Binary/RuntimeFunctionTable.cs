// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Buffers.Binary;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// How a <see cref="RuntimeFunctionRange"/> was decoded from the PE exception directory.
/// Diagnostic detail only — callers should treat any of these as "resolved from .pdata",
/// as opposed to <see cref="FunctionBoundaryProvenance.PdbSymbols"/>.
/// </summary>
public enum RuntimeFunctionKind {
  /// <summary>x64 RUNTIME_FUNCTION: BeginAddress/EndAddress are explicit in the record.</summary>
  Amd64,
  /// <summary>ARM64 packed .pdata record (Flag != 0): FunctionLength is encoded directly in the record.</summary>
  Arm64Packed,
  /// <summary>ARM64 unpacked .pdata record (Flag == 0): FunctionLength comes from the .xdata header word.</summary>
  Arm64Unwind
}

/// <summary>
/// A single function's start/end RVA as decoded from the PE exception directory
/// (IMAGE_DIRECTORY_ENTRY_EXCEPTION), independent of PDB/DIA symbols.
/// </summary>
public readonly record struct RuntimeFunctionRange(long StartRva, long EndRva, RuntimeFunctionKind Kind) {
  public bool Contains(long rva) => rva >= StartRva && rva < EndRva;
}

/// <summary>
/// Deterministic, bounded parser for the PE exception directory (.pdata) runtime-function tables
/// that x64 and ARM64 Windows binaries carry to describe function extents for stack unwinding.
/// This is the fallback source of trustworthy function boundaries when no PDB/DIA symbols are
/// available: entries are a fixed-size, densely packed, address-sorted array, so decoding them
/// touches only the exception-directory bytes themselves (and, for unpacked ARM64 entries, a
/// single bounded 4-byte .xdata header read) — never an unbounded scan of .text.
/// <para>
/// References: "x64 exception handling" and "ARM64 exception handling"
/// (https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64,
/// https://learn.microsoft.com/en-us/cpp/build/arm64-exception-handling).
/// </para>
/// </summary>
public static class RuntimeFunctionTable {
  // x64 RUNTIME_FUNCTION: { DWORD BeginAddress; DWORD EndAddress; DWORD UnwindInfoAddress; }.
  private const int Amd64EntrySize = 12;

  // ARM64 IMAGE_ARM64_RUNTIME_FUNCTION_ENTRY: { DWORD BeginAddress; DWORD UnwindData; }.
  private const int Arm64EntrySize = 8;

  /// <summary>
  /// Parse the x64 RUNTIME_FUNCTION array. Each entry directly carries Begin/End RVAs, so no
  /// auxiliary reads are required to determine bounds.
  /// </summary>
  public static List<RuntimeFunctionRange> ParseAmd64(ReadOnlySpan<byte> exceptionDirectory) {
    var result = new List<RuntimeFunctionRange>(exceptionDirectory.Length / Amd64EntrySize);
    int count = exceptionDirectory.Length / Amd64EntrySize;

    for (int i = 0; i < count; i++) {
      var entry = exceptionDirectory.Slice(i * Amd64EntrySize, Amd64EntrySize);
      uint begin = BinaryPrimitives.ReadUInt32LittleEndian(entry);
      uint end = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(4));

      // Skip zero-filled/sentinel rows and any row that isn't monotonically increasing —
      // reject rather than guess when the table looks corrupt.
      if (begin == 0 && end == 0) continue;
      if (end <= begin) continue;

      result.Add(new RuntimeFunctionRange(begin, end, RuntimeFunctionKind.Amd64));
    }

    result.Sort((a, b) => a.StartRva.CompareTo(b.StartRva));
    return result;
  }

  /// <summary>
  /// Parse the ARM64 IMAGE_ARM64_RUNTIME_FUNCTION_ENTRY array. Packed entries (low 2 bits of the
  /// second DWORD, "Flag", != 0) encode FunctionLength directly in bits [2:12] of that DWORD, in
  /// units of 4-byte instructions. Unpacked entries (Flag == 0) instead store the RVA of an
  /// .xdata unwind-info record whose first DWORD's low 18 bits are FunctionLength in the same
  /// units; <paramref name="readXdataHeaderDword"/> reads exactly that one DWORD (a bounded,
  /// single 4-byte read — never a scan) and returns null when it can't be read deterministically
  /// (e.g. the RVA falls outside any section).
  /// </summary>
  public static List<RuntimeFunctionRange> ParseArm64(ReadOnlySpan<byte> exceptionDirectory,
                                                      Func<long, uint?> readXdataHeaderDword) {
    ArgumentNullException.ThrowIfNull(readXdataHeaderDword);

    var result = new List<RuntimeFunctionRange>(exceptionDirectory.Length / Arm64EntrySize);
    int count = exceptionDirectory.Length / Arm64EntrySize;

    for (int i = 0; i < count; i++) {
      var entry = exceptionDirectory.Slice(i * Arm64EntrySize, Arm64EntrySize);
      uint begin = BinaryPrimitives.ReadUInt32LittleEndian(entry);
      uint unwindData = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(4));

      if (begin == 0 && unwindData == 0) continue;

      uint flag = unwindData & 0x3;

      if (flag != 0) {
        // Packed: Flag(2) | FunctionLength(11) | RegF(3) | RegI(4) | H(1) | CR(2) | FrameSize(9).
        // FunctionLength is in units of 4-byte instructions.
        uint functionLengthWords = (unwindData >> 2) & 0x7FF;
        long length = functionLengthWords * 4L;
        if (length <= 0) continue;

        result.Add(new RuntimeFunctionRange(begin, begin + length, RuntimeFunctionKind.Arm64Packed));
      }
      else {
        // Unpacked: the remaining 30 bits (low 2 bits implicitly 0) are the RVA of the .xdata
        // unwind-info record. Its header DWORD's low 18 bits are FunctionLength, again in units
        // of 4-byte instructions.
        uint xdataRva = unwindData & ~0x3u;
        uint? header = readXdataHeaderDword(xdataRva);
        if (header == null) continue; // Can't read .xdata deterministically -> skip, don't guess.

        uint functionLengthWords = header.Value & 0x3FFFF;
        long length = functionLengthWords * 4L;
        if (length <= 0) continue;

        result.Add(new RuntimeFunctionRange(begin, begin + length, RuntimeFunctionKind.Arm64Unwind));
      }
    }

    result.Sort((a, b) => a.StartRva.CompareTo(b.StartRva));
    return result;
  }

  /// <summary>
  /// Binary search for the range containing <paramref name="rva"/> in a list already sorted by
  /// <see cref="RuntimeFunctionRange.StartRva"/> (as returned by <see cref="ParseAmd64"/>/<see
  /// cref="ParseArm64"/>).
  /// </summary>
  public static RuntimeFunctionRange? Find(List<RuntimeFunctionRange> sortedRanges, long rva) {
    int low = 0;
    int high = sortedRanges.Count - 1;

    while (low <= high) {
      int mid = low + (high - low) / 2;
      var range = sortedRanges[mid];

      if (rva < range.StartRva) {
        high = mid - 1;
      }
      else if (rva >= range.EndRva) {
        low = mid + 1;
      }
      else {
        return range;
      }
    }

    return null;
  }
}
