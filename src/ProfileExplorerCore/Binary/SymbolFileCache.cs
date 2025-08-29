// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ProfileExplorer.Core.FileFormat;
using ProfileExplorer.Core.Utilities;
using ProtoBuf;

namespace ProfileExplorer.Core.Binary;

[ProtoContract]
public class SymbolFileCache {
  private static int CurrentFileVersion = 2;
  private static int MinSupportedFileVersion = 2;
  [ProtoMember(1)]
  public int Version { get; set; }
  [ProtoMember(2)]
  public SymbolFileDescriptor SymbolFile { get; set; }
  [ProtoMember(3)]
  public List<FunctionDebugInfo> FunctionList { get; set; }
  public static string DefaultCacheDirectoryPath => Path.Combine(Path.GetTempPath(), "ProfileExplorer", "symcache");

  public static async Task<bool> SerializeAsync(SymbolFileCache symCache, string directoryPath) {
    try {
      symCache.Version = CurrentFileVersion;
      var outStream = new MemoryStream();
      Serializer.Serialize(outStream, symCache);
      outStream.Position = 0;

      if (!Directory.Exists(directoryPath)) {
        Directory.CreateDirectory(directoryPath);
      }

      string cacheFile = MakeCacheFilePath(symCache.SymbolFile);
      string cachePath = Path.Combine(directoryPath, cacheFile);

      //? TODO: Convert everything to RunSync or add the support in the FileArchive
      return await FileArchive.CreateFromStreamAsync(outStream, cacheFile, cachePath).ConfigureAwait(false);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to save symbol file cache: {ex.Message}");
      return false;
    }
  }

  public static async Task<SymbolFileCache> DeserializeAsync(SymbolFileDescriptor symbolFile, string directoryPath) {
    try {
      string cacheFile = MakeCacheFilePath(symbolFile);
      string cachePath = Path.Combine(directoryPath, cacheFile);

      if (!File.Exists(cachePath)) {
        return null;
      }

      using var archive = await FileArchive.LoadAsync(cachePath).ConfigureAwait(false);

      if (archive != null) {
        using var stream = await archive.ExtractFileToMemoryAsync(cacheFile);

        if (stream != null) {
          var symCache = Serializer.Deserialize<SymbolFileCache>(stream);

          if (symCache.Version < MinSupportedFileVersion) {
            Trace.WriteLine($"File version mismatch in deserialized symbol file cache");
            Trace.WriteLine($"  actual: {symCache.Version} vs min supported {MinSupportedFileVersion}");
            return null;
          }

          // Ensure it's a cache for the same symbol file.
          if (symCache.SymbolFile.Equals(symbolFile)) {
            return symCache;
          }

          Trace.WriteLine($"Symbol file mismatch in deserialized symbol file cache");
          Trace.WriteLine($"  actual: {symCache.SymbolFile} vs expected {symbolFile}");
        }
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to load symbol file cache: {ex.Message}");
    }

    return null;
  }

  private static string MakeCacheFilePath(SymbolFileDescriptor symbolFile) {
    return $"{Utils.TryGetFileName(symbolFile.FileName)}-{symbolFile.Id}-{symbolFile.Age}.cache";
  }
}