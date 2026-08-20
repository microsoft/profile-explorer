// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

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

      return new BinaryFileDescriptor {
        ImageName = string.IsNullOrEmpty(filePath_) ? "" : Path.GetFileName(filePath_),
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
        MinorVersion = reader_.PEHeaders.PEHeader.MinorImageVersion
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