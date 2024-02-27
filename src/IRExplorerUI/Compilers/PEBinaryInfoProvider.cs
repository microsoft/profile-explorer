// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Symbols;

namespace IRExplorerUI.Compilers;

public sealed class PEBinaryInfoProvider : IBinaryInfoProvider, IDisposable {
  private static ConcurrentDictionary<BinaryFileDescriptor, BinaryFileSearchResult> resolvedBinariesCache_ =
    new ConcurrentDictionary<BinaryFileDescriptor, BinaryFileSearchResult>();
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
    // Check if the binary was requested before.
    if (resolvedBinariesCache_.TryGetValue(binaryFile, out var searchResult)) {
      return searchResult;
    }

    // Quick check if trace was recorded on local machine.
    string result = FindExactLocalBinaryFile(binaryFile);

    if (result != null) {
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
      using var authHandler = new BasicAuthenticationHandler(settings);
      using var symbolReader = new SymbolReader(logWriter, userSearchPath, authHandler);
      symbolReader.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.

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
        result = FindMatchingLocalBinaryFile(binaryFile, settings);
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed FindExecutableFilePath: {ex.Message}");
    }
#if DEBUG
    Trace.WriteLine($">> TraceEvent FindExecutableFilePath for {binaryFile.ImageName}");
    Trace.WriteLine(logWriter.ToString());
    Trace.WriteLine("<< TraceEvent");
#endif

    if (!string.IsNullOrEmpty(result) && File.Exists(result)) {
      // Read the binary info from the local file to fill in all fields.
      binaryFile = GetBinaryFileInfo(result);
      searchResult = BinaryFileSearchResult.Success(binaryFile, result, logWriter.ToString());
    }
    else {
      searchResult = BinaryFileSearchResult.Failure(binaryFile, logWriter.ToString());
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

  public byte[] GetSectionData(SectionHeader header) {
    var data = reader_.GetSectionData(header.VirtualAddress);
    var array = data.GetContent();
    byte[] copy = new byte[array.Length];
    array.CopyTo(copy);
    return copy;
  }

  public void Dispose() {
    reader_?.Dispose();
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