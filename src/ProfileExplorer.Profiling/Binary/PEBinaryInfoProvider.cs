// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// Contains version information extracted from a PE file's version resource.
/// </summary>
public sealed class PEVersionInfo {
  public string CompanyName { get; init; }
  public string ProductName { get; init; }
  public string FileDescription { get; init; }
  public string LegalCopyright { get; init; }
  public string OriginalFilename { get; init; }

  /// <summary>
  /// Checks if any of the version info fields contain the specified text (case-insensitive).
  /// </summary>
  public bool ContainsText(string text) {
    if (string.IsNullOrEmpty(text)) {
      return false;
    }

    return ContainsTextInternal(CompanyName, text) ||
           ContainsTextInternal(ProductName, text) ||
           ContainsTextInternal(FileDescription, text) ||
           ContainsTextInternal(LegalCopyright, text);
  }

  /// <summary>
  /// Checks if any of the version info fields contain any of the specified texts (case-insensitive).
  /// </summary>
  public bool ContainsAnyText(IEnumerable<string> texts) {
    foreach (var text in texts) {
      if (ContainsText(text)) {
        return true;
      }
    }

    return false;
  }

  private static bool ContainsTextInternal(string field, string text) {
    return !string.IsNullOrEmpty(field) &&
           field.Contains(text, StringComparison.OrdinalIgnoreCase);
  }

  public override string ToString() {
    return $"Company: {CompanyName ?? "N/A"}, Product: {ProductName ?? "N/A"}, Description: {FileDescription ?? "N/A"}";
  }
}

public sealed class PEBinaryInfoProvider : IBinaryInfoProvider, IDisposable {
  private static ConcurrentDictionary<string, PEVersionInfo> versionInfoCache_ = new();

  /// <summary>
  /// Clears the static version info cache. Call between trace loads to ensure a clean state.
  /// </summary>
  public static void ClearVersionInfoCache() {
    versionInfoCache_.Clear();
  }

  private string filePath_;
  private PEReader reader_;

  public PEBinaryInfoProvider(string filePath) {
    filePath_ = filePath;
  }

  public List<SectionHeader> CodeSectionHeaders {
    get {
      var list = new List<SectionHeader>();

      if (reader_.PEHeaders.PEHeader == null) {
        return list;
      }

      foreach (var section in reader_.PEHeaders.SectionHeaders) {
        if (section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) ||
            section.SectionCharacteristics.HasFlag(SectionCharacteristics.ContainsCode)) {
          list.Add(section);
        }
      }

      return list;
    }
  }

  public SymbolFileDescriptor SymbolFileInfo {
    get {
      foreach (var entry in reader_.ReadDebugDirectory()) {
        if (entry.Type == DebugDirectoryEntryType.CodeView) {
          try {
            var dir = reader_.ReadCodeViewDebugDirectoryData(entry);
            return new SymbolFileDescriptor(dir.Path, dir.Guid, dir.Age);
          }
          catch (BadImageFormatException) {
            // PE reader has problems with some old binaries.
          }

          break;
        }
      }

      return null;
    }
  }

  public BinaryFileDescriptor BinaryFileInfo {
    get {
      if (reader_.PEHeaders.PEHeader == null) {
        return null;
      }

      var fileKind = BinaryFileKind.Native;

      if (reader_.HasMetadata && reader_.PEHeaders.CorHeader != null) {
        if (reader_.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.ILOnly)) {
          fileKind = BinaryFileKind.DotNet;
        }
        else if (reader_.PEHeaders.CorHeader.Flags.HasFlag(CorFlags.ILLibrary)) {
          fileKind = BinaryFileKind.DotNetR2R;
        }
      }

      // For AMR64 EC binaries, they may show up as AMD64, but have the hybrid metadata table set,
      // consider them ARM64 binaries instead so that disassembly works as expected.
      var architecture = reader_.PEHeaders.CoffHeader.Machine;

      if (architecture == Machine.Amd64 && IsARM64ECBinary()) {
        architecture = Machine.Arm64;
      }

      // Original filename from the version resource (e.g. "ntkrnlmp.exe" for ntoskrnl.exe) --
      // symbol servers can index the raw binary under this name instead of the on-disk name.
      // Only set when it's a real, distinct value; a self-referential match (OriginalFileName ==
      // ImageName, the common case) doesn't need to be carried around as a separate fallback key.
      string originalFileName = null;
      string imageName = string.IsNullOrEmpty(filePath_) ? "" : Path.GetFileName(filePath_);
      var versionInfo = GetVersionInfo(filePath_);

      if (!string.IsNullOrEmpty(versionInfo?.OriginalFilename) &&
          !string.Equals(versionInfo.OriginalFilename, imageName, StringComparison.OrdinalIgnoreCase)) {
        originalFileName = versionInfo.OriginalFilename;
      }

      return new BinaryFileDescriptor {
        ImageName = imageName,
        ImagePath = filePath_,
        Architecture = architecture,
        FileKind = fileKind,
        Checksum = reader_.PEHeaders.PEHeader.CheckSum,
        TimeStamp = reader_.PEHeaders.CoffHeader.TimeDateStamp,
        ImageSize = reader_.PEHeaders.PEHeader.SizeOfImage,
        CodeSize = reader_.PEHeaders.PEHeader.SizeOfCode,
        ImageBase = (long)reader_.PEHeaders.PEHeader.ImageBase,
        BaseOfCode = reader_.PEHeaders.PEHeader.BaseOfCode,
        MajorVersion = reader_.PEHeaders.PEHeader.MajorImageVersion,
        MinorVersion = reader_.PEHeaders.PEHeader.MinorImageVersion,
        OriginalFileName = originalFileName
      };
    }
  }

  public void Dispose() {
    reader_?.Dispose();
  }

  public static BinaryFileDescriptor GetBinaryFileInfo(string filePath) {
    using var binaryInfo = new PEBinaryInfoProvider(filePath);

    if (binaryInfo.Initialize()) {
      return binaryInfo.BinaryFileInfo;
    }

    return null;
  }

  public static SymbolFileDescriptor GetSymbolFileInfo(string filePath) {
    using var binaryInfo = new PEBinaryInfoProvider(filePath);

    if (binaryInfo.Initialize()) {
      return binaryInfo.SymbolFileInfo;
    }

    return null;
  }

  /// <summary>
  /// Gets version information (Company, Product, Description, Copyright) from a PE file.
  /// Uses FileVersionInfo which reads the version resource from the PE file.
  /// </summary>
  public static PEVersionInfo GetVersionInfo(string filePath) {
    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) {
      return null;
    }

    // Check cache first
    if (versionInfoCache_.TryGetValue(filePath, out var cached)) {
      return cached;
    }

    try {
      var fileVersionInfo = FileVersionInfo.GetVersionInfo(filePath);
      var versionInfo = new PEVersionInfo {
        CompanyName = fileVersionInfo.CompanyName,
        ProductName = fileVersionInfo.ProductName,
        FileDescription = fileVersionInfo.FileDescription,
        LegalCopyright = fileVersionInfo.LegalCopyright,
        OriginalFilename = fileVersionInfo.OriginalFilename
      };

      versionInfoCache_.TryAdd(filePath, versionInfo);
      return versionInfo;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to read version info from {filePath}: {ex.Message}");
      return null;
    }
  }

  /// <summary>
  /// Checks if a PE file's version info matches any of the specified company filter strings.
  /// Returns true if any filter string is found in Company, Product, Description, or Copyright fields.
  /// Returns true if no filters are specified (empty/null list).
  /// Returns true if the file doesn't exist or version info cannot be read (fail-open for safety).
  /// </summary>
  public static bool MatchesCompanyFilter(string filePath, IReadOnlyList<string> companyFilters) {
    // No filter specified - accept all
    if (companyFilters == null || companyFilters.Count == 0) {
      return true;
    }

    var versionInfo = GetVersionInfo(filePath);

    // If we can't read version info, accept the file (fail-open)
    if (versionInfo == null) {
      return true;
    }

    return versionInfo.ContainsAnyText(companyFilters);
  }

  public bool Initialize() {
    if (!File.Exists(filePath_)) {
      return false;
    }

    try {
      var stream = File.OpenRead(filePath_);
      reader_ = new PEReader(stream);
      return reader_.PEHeaders != null; // Throws BadImageFormatException on invalid file.
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to read PE binary file: {filePath_}");
      return false;
    }
  }

  public ReadOnlyMemory<byte> GetSectionData(SectionHeader header) {
    var data = reader_.GetSectionData(header.VirtualAddress);
    var array = data.GetContent();
    return array.AsMemory();
  }

  /// <summary>
  /// Raw bytes of the PE exception directory (IMAGE_DIRECTORY_ENTRY_EXCEPTION) — the x64/ARM64
  /// RUNTIME_FUNCTION table used by <see cref="RuntimeFunctionTable"/> to resolve function bounds
  /// when no PDB/DIA symbols are available. Empty when the directory is absent (e.g. x86 images,
  /// which don't use table-based unwinding).
  /// </summary>
  public ReadOnlyMemory<byte> GetExceptionDirectoryData() {
    var peHeader = reader_.PEHeaders.PEHeader;

    if (peHeader == null || peHeader.ExceptionTableDirectory.Size <= 0 ||
        !reader_.PEHeaders.TryGetDirectoryOffset(peHeader.ExceptionTableDirectory, out int offset)) {
      return ReadOnlyMemory<byte>.Empty;
    }

    var imageData = reader_.GetEntireImage();
    var content = imageData.GetContent(offset, peHeader.ExceptionTableDirectory.Size);
    return content.AsMemory();
  }

  /// <summary>
  /// Read <paramref name="length"/> bytes starting at RVA <paramref name="rva"/>, wherever in the
  /// image that RVA falls (not just code sections) — a single bounded read used to pull the
  /// .xdata unwind-info header word for ARM64 unpacked .pdata entries. Never scans; only reads
  /// this exact byte range. Returns false when the range doesn't fit entirely within one section.
  /// </summary>
  public bool TryReadRvaData(long rva, int length, out ReadOnlyMemory<byte> data) {
    if (reader_.PEHeaders.PEHeader != null) {
      foreach (var section in reader_.PEHeaders.SectionHeaders) {
        long sectionSize = Math.Max(section.VirtualSize, section.SizeOfRawData);

        if (rva >= section.VirtualAddress && rva + length <= section.VirtualAddress + sectionSize) {
          var sectionData = reader_.GetSectionData(section.VirtualAddress).GetContent();
          int offsetInSection = (int)(rva - section.VirtualAddress);

          if (offsetInSection >= 0 && offsetInSection + length <= sectionData.Length) {
            data = sectionData.AsMemory().Slice(offsetInSection, length);
            return true;
          }

          break; // RVA is within the section's virtual range but past its raw data -> unreadable.
        }
      }
    }

    data = ReadOnlyMemory<byte>.Empty;
    return false;
  }

  /// <summary>
  /// Read a null-terminated ASCII string starting at RVA <paramref name="rva"/>, bounded to at most
  /// <paramref name="maxLength"/> bytes and to the containing section's data -- never scans past
  /// the section or past <paramref name="maxLength"/>. Used for PE import/export directory names
  /// (module names, function names) which are always plain ASCII per the PE spec.
  /// </summary>
  public bool TryReadNullTerminatedAsciiString(long rva, int maxLength, out string? value) {
    if (!TryFindContainingSection(rva, out var sectionData, out long sectionStartRva)) {
      value = null;
      return false;
    }

    int offset = (int)(rva - sectionStartRva);

    if (offset < 0 || offset >= sectionData.Length) {
      value = null;
      return false;
    }

    var span = sectionData.Span;
    int end = offset;
    int limit = Math.Min(sectionData.Length, offset + maxLength);

    while (end < limit && span[end] != 0) {
      end++;
    }

    value = Encoding.ASCII.GetString(span.Slice(offset, end - offset));
    return true;
  }

  /// <summary>
  /// Read a null-terminated UTF-16LE ("wide") string starting at RVA <paramref name="rva"/>,
  /// bounded to at most <paramref name="maxLength"/> UTF-16 code units and to the containing
  /// section's data. Windows code commonly stores wide string literals (L"...") this way; used by
  /// <see cref="ReferenceResolver"/> to classify a data reference as a string.
  /// </summary>
  public bool TryReadNullTerminatedUtf16String(long rva, int maxLength, out string? value) {
    if (!TryFindContainingSection(rva, out var sectionData, out long sectionStartRva)) {
      value = null;
      return false;
    }

    int offset = (int)(rva - sectionStartRva);

    if (offset < 0 || offset + 1 >= sectionData.Length) {
      // Need at least 2 bytes available for a single UTF-16 code unit.
      value = null;
      return false;
    }

    var span = sectionData.Span;
    int end = offset;
    int limitBytes = Math.Min(sectionData.Length - 1, offset + maxLength * 2);

    while (end + 1 <= limitBytes) {
      int unit = span[end] | (span[end + 1] << 8);

      if (unit == 0) {
        break;
      }

      end += 2;
    }

    value = Encoding.Unicode.GetString(span.Slice(offset, end - offset));
    return true;
  }

  /// <summary>
  /// Locates the section containing <paramref name="rva"/> and returns its full data plus its
  /// start RVA, so callers that don't know the length to read up-front (null-terminated strings)
  /// can bound their own scan within the section instead of guessing a length for
  /// <see cref="TryReadRvaData"/>.
  /// </summary>
  private bool TryFindContainingSection(long rva, out ReadOnlyMemory<byte> sectionData, out long sectionStartRva) {
    if (reader_.PEHeaders.PEHeader != null) {
      foreach (var section in reader_.PEHeaders.SectionHeaders) {
        long sectionSize = Math.Max(section.VirtualSize, section.SizeOfRawData);

        if (rva >= section.VirtualAddress && rva < section.VirtualAddress + sectionSize) {
          sectionData = reader_.GetSectionData(section.VirtualAddress).GetContent().AsMemory();
          sectionStartRva = section.VirtualAddress;
          return true;
        }
      }
    }

    sectionData = ReadOnlyMemory<byte>.Empty;
    sectionStartRva = 0;
    return false;
  }

  /// <summary>
  /// Parses the PE Import Directory Table (IMAGE_DIRECTORY_ENTRY_IMPORT) into one
  /// <see cref="ImportedFunctionReference"/> per imported function/ordinal, including the exact
  /// RVA of its IAT slot. Returns an empty list (never throws) when the directory is absent or a
  /// bounded read anywhere in the table fails -- a malformed/truncated table yields a partial or
  /// empty result rather than a guess. Bounded by hard iteration caps so a corrupt/cyclic table
  /// cannot loop unbounded.
  /// </summary>
  public List<ImportedFunctionReference> GetImportedFunctions() {
    var result = new List<ImportedFunctionReference>();
    var peHeader = reader_.PEHeaders.PEHeader;

    if (peHeader == null || peHeader.ImportTableDirectory.Size <= 0) {
      return result;
    }

    bool isPe32Plus = peHeader.Magic == PEMagic.PE32Plus;
    int thunkSize = isPe32Plus ? 8 : 4;
    long ordinalFlag = isPe32Plus ? unchecked((long)0x8000000000000000) : 0x80000000L;

    const int descriptorSize = 20; // sizeof(IMAGE_IMPORT_DESCRIPTOR)
    long descriptorRva = peHeader.ImportTableDirectory.RelativeVirtualAddress;
    int descriptorCount = 0;

    while (descriptorCount++ < 4096 && TryReadRvaData(descriptorRva, descriptorSize, out var descBytes)) {
      var span = descBytes.Span;
      uint originalFirstThunk = BitConverter.ToUInt32(span[..4]);
      uint nameRva = BitConverter.ToUInt32(span.Slice(12, 4));
      uint firstThunk = BitConverter.ToUInt32(span.Slice(16, 4));

      if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0) {
        break; // Null terminator descriptor -- end of the table.
      }

      if (!TryReadNullTerminatedAsciiString(nameRva, 260, out string? moduleName) || string.IsNullOrEmpty(moduleName)) {
        moduleName = "<unknown-module>";
      }

      // Prefer the Import Lookup Table (OriginalFirstThunk) for names/ordinals -- it's never
      // overwritten by the loader. FirstThunk (the IAT) is what compiled code actually references
      // at runtime, so that's always the reported IatRva regardless of which table we read names from.
      long thunkArrayRva = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
      long iatArrayRva = firstThunk;
      int index = 0;

      while (index < 65536) {
        long entryRva = thunkArrayRva + index * thunkSize;

        if (!TryReadRvaData(entryRva, thunkSize, out var entryBytes)) {
          break;
        }

        long entryValue = thunkSize == 8
          ? BitConverter.ToInt64(entryBytes.Span)
          : BitConverter.ToUInt32(entryBytes.Span);

        if (entryValue == 0) {
          break; // End of this module's thunk array.
        }

        long iatSlotRva = iatArrayRva + (long)index * thunkSize;

        if ((entryValue & ordinalFlag) != 0) {
          result.Add(new ImportedFunctionReference(moduleName, null, entryValue & 0xFFFF, iatSlotRva));
        }
        else {
          // Non-ordinal entries are RVAs to IMAGE_IMPORT_BY_NAME: uint16 Hint followed by the
          // null-terminated ASCII function name.
          string? functionName = null;
          TryReadNullTerminatedAsciiString(entryValue + 2, 512, out functionName);
          result.Add(new ImportedFunctionReference(moduleName, functionName, null, iatSlotRva));
        }

        index++;
      }

      descriptorRva += descriptorSize;
    }

    return result;
  }

  /// <summary>
  /// Parses this binary's own Export Directory Table (IMAGE_DIRECTORY_ENTRY_EXPORT). Useful when
  /// a selected function is itself an export (common for driver dispatch routines) or to identify
  /// forwarder exports (<see cref="ExportedFunctionReference.IsForwarder"/>). Returns an empty
  /// list (never throws) when the directory is absent or malformed.
  /// </summary>
  public List<ExportedFunctionReference> GetExportedFunctions() {
    var result = new List<ExportedFunctionReference>();
    var peHeader = reader_.PEHeaders.PEHeader;

    if (peHeader == null || peHeader.ExportTableDirectory.Size <= 0 ||
        !TryReadRvaData(peHeader.ExportTableDirectory.RelativeVirtualAddress, 40, out var dirBytes)) {
      return result;
    }

    var span = dirBytes.Span;
    uint baseOrdinal = BitConverter.ToUInt32(span.Slice(16, 4));
    uint numberOfFunctions = Math.Min(BitConverter.ToUInt32(span.Slice(20, 4)), 65536);
    uint numberOfNames = Math.Min(BitConverter.ToUInt32(span.Slice(24, 4)), 65536);
    uint addressOfFunctions = BitConverter.ToUInt32(span.Slice(28, 4));
    uint addressOfNames = BitConverter.ToUInt32(span.Slice(32, 4));
    uint addressOfNameOrdinals = BitConverter.ToUInt32(span.Slice(36, 4));

    // Map ordinal-table-index -> exported name via the parallel AddressOfNames/AddressOfNameOrdinals arrays.
    var namesByOrdinalIndex = new Dictionary<uint, string>((int)numberOfNames);

    for (uint i = 0; i < numberOfNames; i++) {
      if (!TryReadRvaData(addressOfNames + i * 4, 4, out var nameRvaBytes) ||
          !TryReadRvaData(addressOfNameOrdinals + i * 2, 2, out var ordIdxBytes)) {
        break;
      }

      uint nameRva = BitConverter.ToUInt32(nameRvaBytes.Span);
      ushort ordinalIndex = BitConverter.ToUInt16(ordIdxBytes.Span);

      if (TryReadNullTerminatedAsciiString(nameRva, 512, out string? name) && !string.IsNullOrEmpty(name)) {
        namesByOrdinalIndex[ordinalIndex] = name;
      }
    }

    long exportDirRva = peHeader.ExportTableDirectory.RelativeVirtualAddress;
    long exportDirEndRva = exportDirRva + peHeader.ExportTableDirectory.Size;

    for (uint i = 0; i < numberOfFunctions; i++) {
      if (!TryReadRvaData(addressOfFunctions + i * 4, 4, out var funcRvaBytes)) {
        break;
      }

      uint functionRva = BitConverter.ToUInt32(funcRvaBytes.Span);

      if (functionRva == 0) {
        continue; // Gap in the ordinal range -- no export at this ordinal.
      }

      // A function RVA that falls *inside* the export directory itself is a forwarder: its "RVA"
      // is really the RVA of an ASCII "OtherModule.OtherFunction" string, not real code.
      bool isForwarder = functionRva >= exportDirRva && functionRva < exportDirEndRva;
      string? forwarderTarget = null;

      if (isForwarder) {
        TryReadNullTerminatedAsciiString(functionRva, 512, out forwarderTarget);
      }

      namesByOrdinalIndex.TryGetValue(i, out string? functionName);
      result.Add(new ExportedFunctionReference(functionName, isForwarder ? 0 : functionRva,
                                               baseOrdinal + i, forwarderTarget));
    }

    return result;
  }

  private bool IsARM64ECBinary() {
    if (reader_.PEHeaders.PEHeader == null ||
        reader_.PEHeaders.PEHeader.LoadConfigTableDirectory.Size <= 0 ||
        !reader_.PEHeaders.TryGetDirectoryOffset(reader_.PEHeaders.PEHeader.LoadConfigTableDirectory, out int offset)) {
      return false;
    }

    var imageData = reader_.GetEntireImage();
    var configTableData = imageData.GetContent(offset, reader_.PEHeaders.PEHeader.LoadConfigTableDirectory.Size);
    var span = MemoryMarshal.Cast<byte, IMAGE_LOAD_CONFIG_DIRECTORY64>(configTableData.AsSpan());
    return span.Length > 0 && span[0].CHPEMetadataPointer != 0;
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
  public struct IMAGE_LOAD_CONFIG_DIRECTORY64 {
    public uint Size;
    public uint TimeDateStamp;
    public ushort MajorVersion;
    public ushort MinorVersion;
    public uint GlobalFlagsClear;
    public uint GlobalFlagsSet;
    public uint CriticalSectionDefaultTimeout;
    public ulong DeCommitFreeBlockThreshold;
    public ulong DeCommitTotalFreeThreshold;
    public ulong LockPrefixTable;
    public ulong MaximumAllocationSize;
    public ulong VirtualMemoryThreshold;
    public ulong ProcessAffinityMask;
    public uint ProcessHeapFlags;
    public ushort CSDVersion;
    public ushort DependentLoadFlags;
    public ulong EditList;
    public ulong SecurityCookie;
    public ulong SEHandlerTable;
    public ulong SEHandlerCount;
    public ulong GuardCFCheckFunctionPointer;
    public ulong GuardCFDispatchFunctionPointer;
    public ulong GuardCFFunctionTable;
    public ulong GuardCFFunctionCount;
    public uint GuardFlags;
    public IMAGE_LOAD_CONFIG_CODE_INTEGRITY CodeIntegrity;
    public ulong GuardAddressTakenIatEntryTable;
    public ulong GuardAddressTakenIatEntryCount;
    public ulong GuardLongJumpTargetTable;
    public ulong GuardLongJumpTargetCount;
    public ulong DynamicValueRelocTable;
    public ulong CHPEMetadataPointer;
    public ulong GuardRFFailureRoutine;
    public ulong GuardRFFailureRoutineFunctionPointer;
    public uint DynamicValueRelocTableOffset;
    public ushort DynamicValueRelocTableSection;
    public ushort Reserved2;
    public ulong GuardRFVerifyStackPointerFunctionPointer;
    public uint HotPatchTableOffset;
    public uint Reserved3;
    public ulong EnclaveConfigurationPointer;
    public ulong VolatileMetadataPointer;
    public ulong GuardEHContinuationTable;
    public ulong GuardEHContinuationCount;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct IMAGE_LOAD_CONFIG_CODE_INTEGRITY {
    public ushort Flags;
    public ushort Catalog;
    public uint CatalogOffset;
    public uint Reserved;
  }
}