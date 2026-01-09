// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Symbols;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

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
  private static ConcurrentDictionary<BinaryFileDescriptor, BinaryFileSearchResult> resolvedBinariesCache_ = new();
  private static ConcurrentDictionary<string, PEVersionInfo> versionInfoCache_ = new();
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
        ImageName = Utils.TryGetFileName(filePath_),
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

  public static async Task<BinaryFileSearchResult> LocateBinaryFileAsync(BinaryFileDescriptor binaryFile,
                                                                         SymbolFileSourceSettings settings) {
    // Check if the binary was requested before.
    if (resolvedBinariesCache_.TryGetValue(binaryFile, out var searchResult)) {
      return searchResult;
    }

    return await Task.Run(() => {
      return LocateBinaryFile(binaryFile, settings);
    }).ConfigureAwait(false);
  }

  public static BinaryFileSearchResult LocateBinaryFile(BinaryFileDescriptor binaryFile,
                                                        SymbolFileSourceSettings settings) {
    var sw = Stopwatch.StartNew();
    
    // Check if the binary was requested before.
    if (resolvedBinariesCache_.TryGetValue(binaryFile, out var searchResult)) {
      DiagnosticLogger.LogDebug($"[BinarySearch] Cache hit for {binaryFile.ImageName}");
      return searchResult;
    }

    DiagnosticLogger.LogInfo($"[BinarySearch] Starting binary search for {binaryFile.ImageName} (Size: {binaryFile.ImageSize}, Timestamp: {binaryFile.TimeStamp})");

    // Check if this binary was previously rejected (failed lookup in a prior session).
    if (settings.IsRejectedBinaryFile(binaryFile)) {
      DiagnosticLogger.LogInfo($"[BinarySearch] SKIPPED - previously rejected: {binaryFile.ImageName}");
      searchResult = BinaryFileSearchResult.Failure(binaryFile, "Previously rejected");
      resolvedBinariesCache_.TryAdd(binaryFile, searchResult);
      return searchResult;
    }

    // Quick check if trace was recorded on local machine.
    string result = FindExactLocalBinaryFile(binaryFile);

    if (result != null) {
      DiagnosticLogger.LogInfo($"[BinarySearch] Found exact local binary for {binaryFile.ImageName} at {result} ({sw.ElapsedMilliseconds}ms)");
      binaryFile = GetBinaryFileInfo(result);
      searchResult = BinaryFileSearchResult.Success(binaryFile, result, "");
      resolvedBinariesCache_.TryAdd(binaryFile, searchResult);
      return searchResult;
    }

    using var logWriter = new StringWriter();

    try {
      // Try to use symbol server to download binary.
      if (File.Exists(binaryFile.ImagePath)) {
        settings = settings.WithSymbolPaths(binaryFile.ImagePath);
      }

      string userSearchPath = PDBDebugInfoProvider.ConstructSymbolSearchPath(settings);

      //? TODO: Making a new instance clears the "dead servers",
      //? have a way to share the list between multiple instances.
      using var symbolReader =
        new SymbolReader(logWriter, userSearchPath, PDBDebugInfoProvider.CreateAuthHandler(settings));
      symbolReader.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.

      // Set symbol server timeout from settings (default 10 seconds).
      // Use 10 seconds as fallback if setting is 0 (e.g., from old settings file without this property).
      int timeoutSeconds = settings.SymbolServerTimeoutSeconds > 0 ? settings.SymbolServerTimeoutSeconds : 10;
      symbolReader.ServerTimeout = TimeSpan.FromSeconds(timeoutSeconds);
      DiagnosticLogger.LogInfo($"[BinarySearch] ServerTimeout={timeoutSeconds}s, RejectPreviouslyFailedFiles={settings.RejectPreviouslyFailedFiles}, " +
                               $"RejectedBinaries={settings.RejectedBinaryFiles?.Count ?? 0} for {binaryFile.ImageName}");

      //? TODO: Workaround for cases where the ETL file doesn't have a timestamp
      //? and SymbolReader would reject the bin even on the same machine...
      //? Better way to handle this is to have SymReader accept a func to check if PDB is valid to use
      if (binaryFile.TimeStamp == 0 && File.Exists(binaryFile.ImagePath)) {
        var binInfo = GetBinaryFileInfo(binaryFile.ImagePath);
        binaryFile.TimeStamp = binInfo.TimeStamp;
      }

      //Trace.WriteLine($"Start download of {Utils.TryGetFileName(binaryFile.ImageName)}");
      result = symbolReader.FindExecutableFilePath(binaryFile.ImageName,
                                                   binaryFile.TimeStamp,
                                                   (int)binaryFile.ImageSize);

      if (result == null) {
        // Finally, try an approximate manual search.
        DiagnosticLogger.LogDebug($"[BinarySearch] Symbol server search failed, trying approximate local search for {binaryFile.ImageName}");
        result = FindMatchingLocalBinaryFile(binaryFile, settings);
      }
    }
    catch (Exception ex) {
      DiagnosticLogger.LogError($"[BinarySearch] Exception during binary search for {binaryFile.ImageName}: {ex.Message}", ex);
      Trace.TraceError($"Failed FindExecutableFilePath: {ex.Message}");
    }

    var searchDuration = sw.Elapsed;
    string searchLog = logWriter.ToString();
    
#if DEBUG
    Trace.WriteLine($">> TraceEvent FindExecutableFilePath for {binaryFile.ImageName}");
    Trace.WriteLine(searchLog);
    Trace.WriteLine("<< TraceEvent");
#endif

    if (!string.IsNullOrEmpty(result) && File.Exists(result)) {
      DiagnosticLogger.LogInfo($"[BinarySearch] Found binary for {binaryFile.ImageName} at {result} ({searchDuration.TotalMilliseconds:F0}ms)");
      // Read the binary info from the local file to fill in all fields.
      binaryFile = GetBinaryFileInfo(result);
      searchResult = BinaryFileSearchResult.Success(binaryFile, result, searchLog);
    }
    else {
      DiagnosticLogger.LogWarning($"[BinarySearch] Failed to find binary for {binaryFile.ImageName} ({searchDuration.TotalMilliseconds:F0}ms)");
      searchResult = BinaryFileSearchResult.Failure(binaryFile, searchLog);

      // Record failed lookup to avoid retrying in future sessions.
      settings.RejectBinaryFile(binaryFile);
    }

    resolvedBinariesCache_.TryAdd(binaryFile, searchResult);
    return searchResult;
  }

  private static string FindExactLocalBinaryFile(BinaryFileDescriptor binaryFile) {
    if (File.Exists(binaryFile.ImagePath)) {
      var fileInfo = GetBinaryFileInfo(binaryFile.ImagePath);

      if (fileInfo != null &&
          fileInfo.TimeStamp == binaryFile.TimeStamp &&
          fileInfo.ImageSize == binaryFile.ImageSize) {
        return binaryFile.ImagePath;
      }
    }

    return null;
  }

  private static string FindMatchingLocalBinaryFile(BinaryFileDescriptor binaryFile,
                                                    SymbolFileSourceSettings settings) {
    // Manually search in the provided directories.
    // This helps in cases where the original fine name doesn't match
    // the one on disk, like it seems to happen sometimes with the SPEC runner.
    string winPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    string sysPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string sysx86Path = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);

    // Don't search in the system dirs though, it's pointless
    // and takes a long time checking thousands of binaries.
    bool PathIsSubPath(string subPath, string basePath) {
      string rel = Path.GetRelativePath(basePath, subPath);
      return !rel.StartsWith('.') && !Path.IsPathRooted(rel);
    }

    foreach (string path in settings.SymbolPaths) {
      if (PathIsSubPath(path, winPath) ||
          PathIsSubPath(path, sysPath) ||
          PathIsSubPath(path, sysx86Path)) {
        continue;
      }

      try {
        string searchPath = Utils.TryGetDirectoryName(path);

        foreach (string file in Directory.EnumerateFiles(searchPath, "*.*", SearchOption.AllDirectories)) {
          if (!Utils.IsBinaryFile(file)) {
            continue;
          }

          var fileInfo = GetBinaryFileInfo(file);

          if (fileInfo != null &&
              fileInfo.TimeStamp == binaryFile.TimeStamp &&
              fileInfo.ImageSize == binaryFile.ImageSize) {
            return file;
          }
        }
      }
      catch (Exception ex) {
        Trace.TraceError($"Exception searching for binary {binaryFile.ImageName} in {path}: {ex.Message}");
      }
    }

    return null;
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