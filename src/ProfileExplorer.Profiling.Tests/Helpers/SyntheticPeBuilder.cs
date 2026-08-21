// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Linq;
using System.Text;

namespace ProfileExplorer.Profiling.Tests.Helpers;

/// <summary>
/// Hand-rolled minimal PE (COFF) image builder for tests that need deterministic, from-scratch
/// binaries -- no external toolchain, no dependency on real compiled fixtures. Used to cover the
/// PE exception-directory (.pdata) code paths (x64 and ARM64) end-to-end, including cases (like an
/// unpadded/uncovered code "gap", or a non-x64/ARM64 machine type) that would be impractical to
/// find or fabricate reliably in a real compiled binary.
/// <para>
/// Deliberately chooses SectionAlignment == FileAlignment == 0x200 so RVA arithmetic and file
/// offsets coincide, keeping the layout trivial to reason about and verify.
/// </para>
/// </summary>
internal static class SyntheticPeBuilder {
  public const int SectionAlignment = 0x200;
  public const int FileAlignment = 0x200;
  public const ulong ImageBase = 0x1_4000_0000;

  public readonly record struct Section(string Name, byte[] Data, bool IsExecutableCode);

  /// <summary>
  /// Build a minimal well-formed PE32+ image with the given COFF machine type and sections.
  /// When <paramref name="exceptionTableSectionIndex"/> is >= 0, the IMAGE_DIRECTORY_ENTRY_EXCEPTION
  /// data directory is pointed at that section (offset <paramref name="exceptionTableOffsetInSection"/>,
  /// size <paramref name="exceptionTableSize"/>). <paramref name="exportTableSectionIndex"/>/
  /// <paramref name="importTableSectionIndex"/> work the same way for
  /// IMAGE_DIRECTORY_ENTRY_EXPORT/IMPORT (directory indices 0/1), used by
  /// PEReferenceModelTests to exercise import/export parsing without needing a real compiled binary.
  /// </summary>
  public static byte[] Build(ushort machine, IReadOnlyList<Section> sections,
                             int exceptionTableSectionIndex = -1, uint exceptionTableOffsetInSection = 0,
                             uint exceptionTableSize = 0,
                             int exportTableSectionIndex = -1, uint exportTableOffsetInSection = 0,
                             uint exportTableSize = 0,
                             int importTableSectionIndex = -1, uint importTableOffsetInSection = 0,
                             uint importTableSize = 0) {
    int numberOfSections = sections.Count;
    const int dosHeaderSize = 64;
    const int peSignatureSize = 4;
    const int coffHeaderSize = 20;
    const int optionalHeaderSize = 112 + 16 * 8; // PE32+ fixed part + 16 data directories.
    const int sectionHeaderSize = 40;

    int headersSize = dosHeaderSize + peSignatureSize + coffHeaderSize + optionalHeaderSize +
                       numberOfSections * sectionHeaderSize;
    int sizeOfHeaders = RoundUp(headersSize, FileAlignment);

    var sectionSizes = sections.Select(s => s.Data.Length).ToArray();
    var rvas = ComputeSectionRvas(numberOfSections, sectionSizes);
    var fileOffsets = rvas; // SectionAlignment == FileAlignment -> RVA coincides with file offset.
    int cursor = sizeOfHeaders;

    for (int i = 0; i < numberOfSections; i++) {
      cursor += RoundUp(sectionSizes[i], FileAlignment);
    }

    int sizeOfImage = RoundUp(cursor, SectionAlignment);

    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);

    // --- DOS header (64 bytes): only e_magic ("MZ") and e_lfanew (at fixed offset 0x3C) matter. ---
    w.Write((ushort)0x5A4D);
    w.Write(new byte[58]);
    w.Write((uint)dosHeaderSize); // e_lfanew: PE header starts right after the 64-byte DOS header.

    // --- PE signature ---
    w.Write((uint)0x00004550); // "PE\0\0"

    // --- COFF header ---
    w.Write(machine);
    w.Write((ushort)numberOfSections);
    w.Write((uint)0); // TimeDateStamp
    w.Write((uint)0); // PointerToSymbolTable
    w.Write((uint)0); // NumberOfSymbols
    w.Write((ushort)optionalHeaderSize);
    w.Write((ushort)0x0022); // IMAGE_FILE_EXECUTABLE_IMAGE | IMAGE_FILE_LARGE_ADDRESS_AWARE.

    // --- Optional header (PE32+) ---
    w.Write((ushort)0x20B); // Magic: PE32+.
    w.Write((byte)0);       // MajorLinkerVersion
    w.Write((byte)0);       // MinorLinkerVersion
    w.Write((uint)0);       // SizeOfCode
    w.Write((uint)0);       // SizeOfInitializedData
    w.Write((uint)0);       // SizeOfUninitializedData
    w.Write((uint)(numberOfSections > 0 ? rvas[0] : 0)); // AddressOfEntryPoint
    w.Write((uint)(numberOfSections > 0 ? rvas[0] : 0)); // BaseOfCode
    w.Write(ImageBase);
    w.Write((uint)SectionAlignment);
    w.Write((uint)FileAlignment);
    w.Write((ushort)6); // MajorOperatingSystemVersion
    w.Write((ushort)0); // MinorOperatingSystemVersion
    w.Write((ushort)0); // MajorImageVersion
    w.Write((ushort)0); // MinorImageVersion
    w.Write((ushort)6); // MajorSubsystemVersion
    w.Write((ushort)0); // MinorSubsystemVersion
    w.Write((uint)0);   // Win32VersionValue
    w.Write((uint)sizeOfImage);
    w.Write((uint)sizeOfHeaders);
    w.Write((uint)0);   // CheckSum
    w.Write((ushort)3); // Subsystem: Console.
    w.Write((ushort)0); // DllCharacteristics
    w.Write((ulong)0x100000); // SizeOfStackReserve
    w.Write((ulong)0x1000);   // SizeOfStackCommit
    w.Write((ulong)0x100000); // SizeOfHeapReserve
    w.Write((ulong)0x1000);   // SizeOfHeapCommit
    w.Write((uint)0);  // LoaderFlags
    w.Write((uint)16); // NumberOfRvaAndSizes

    for (int i = 0; i < 16; i++) {
      if (i == 0 && exportTableSectionIndex >= 0) { // IMAGE_DIRECTORY_ENTRY_EXPORT.
        w.Write((uint)(rvas[exportTableSectionIndex] + exportTableOffsetInSection));
        w.Write(exportTableSize);
      }
      else if (i == 1 && importTableSectionIndex >= 0) { // IMAGE_DIRECTORY_ENTRY_IMPORT.
        w.Write((uint)(rvas[importTableSectionIndex] + importTableOffsetInSection));
        w.Write(importTableSize);
      }
      else if (i == 3 && exceptionTableSectionIndex >= 0) { // IMAGE_DIRECTORY_ENTRY_EXCEPTION.
        w.Write((uint)(rvas[exceptionTableSectionIndex] + exceptionTableOffsetInSection));
        w.Write(exceptionTableSize);
      }
      else {
        w.Write((uint)0);
        w.Write((uint)0);
      }
    }

    // --- Section headers ---
    for (int i = 0; i < numberOfSections; i++) {
      var nameField = new byte[8];
      var nameBytes = Encoding.ASCII.GetBytes(sections[i].Name);
      Array.Copy(nameBytes, nameField, Math.Min(nameBytes.Length, 8));
      w.Write(nameField);
      w.Write((uint)sections[i].Data.Length); // VirtualSize
      w.Write((uint)rvas[i]);
      w.Write((uint)RoundUp(sections[i].Data.Length, FileAlignment)); // SizeOfRawData
      w.Write((uint)fileOffsets[i]);
      w.Write((uint)0);   // PointerToRelocations
      w.Write((uint)0);   // PointerToLinenumbers
      w.Write((ushort)0); // NumberOfRelocations
      w.Write((ushort)0); // NumberOfLinenumbers

      uint characteristics = 0x40000000; // IMAGE_SCN_MEM_READ
      characteristics |= sections[i].IsExecutableCode
        ? 0x20000020u  // IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE
        : 0x00000040u; // IMAGE_SCN_CNT_INITIALIZED_DATA
      w.Write(characteristics);
    }

    long headerPad = sizeOfHeaders - ms.Length;
    if (headerPad > 0) w.Write(new byte[headerPad]);

    for (int i = 0; i < numberOfSections; i++) {
      w.Write(sections[i].Data);
      int padded = RoundUp(sections[i].Data.Length, FileAlignment) - sections[i].Data.Length;
      if (padded > 0) w.Write(new byte[padded]);
    }

    w.Flush();
    return ms.ToArray();
  }

  /// <summary>
  /// Computes each section's RVA (equivalently, file offset, since SectionAlignment == FileAlignment)
  /// given the section count and sizes, so callers can compute cross-references (e.g. a .pdata
  /// entry's BeginAddress pointing into .text) before the final image bytes are assembled.
  /// </summary>
  public static int[] ComputeSectionRvas(int numberOfSections, IReadOnlyList<int> sectionSizes) {
    const int dosHeaderSize = 64;
    const int peSignatureSize = 4;
    const int coffHeaderSize = 20;
    const int optionalHeaderSize = 112 + 16 * 8;
    const int sectionHeaderSize = 40;

    int headersSize = dosHeaderSize + peSignatureSize + coffHeaderSize + optionalHeaderSize +
                       numberOfSections * sectionHeaderSize;
    int sizeOfHeaders = RoundUp(headersSize, FileAlignment);

    var rvas = new int[numberOfSections];
    int cursor = sizeOfHeaders;

    for (int i = 0; i < numberOfSections; i++) {
      rvas[i] = cursor;
      cursor += RoundUp(sectionSizes[i], FileAlignment);
    }

    return rvas;
  }

  private static int RoundUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}

/// <summary>
/// Materializes a synthetic PE built by <see cref="SyntheticPeBuilder"/> to a file so it can be
/// opened by <see cref="ProfileExplorer.Core.Binary.PEBinaryInfoProvider"/>/<see cref="ProfileExplorer.Core.Binary.Disassembler"/>
/// (which require a file path, not in-memory bytes). Written under the test assembly's own output
/// directory (a build artifact directory, never the OS temp folder) and removed on <see cref="Dispose"/>.
/// </summary>
internal sealed class SyntheticPeFile : IDisposable {
  public string Path { get; }

  public SyntheticPeFile(byte[] bytes, string fileName) {
    string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "SyntheticFixtures");
    Directory.CreateDirectory(dir);
    Path = System.IO.Path.Combine(dir, fileName);
    File.WriteAllBytes(Path, bytes);
  }

  public void Dispose() {
    try { File.Delete(Path); } catch { /* best-effort cleanup */ }
  }
}
