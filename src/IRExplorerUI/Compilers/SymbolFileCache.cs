// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using IRExplorerCore.FileFormat;
using ProtoBuf;

namespace IRExplorerUI.Compilers;

[ProtoContract]
public class SymbolFileCache {
  [ProtoMember(1)]
  public SymbolFileDescriptor SymbolFile { get; set; }
  [ProtoMember(2)]
  public List<FunctionDebugInfo> FunctionList { get; set; }
  public static string DefaultCacheDirectoryPath => Path.Combine(Path.GetTempPath(), "irexplorer", "symcache");

  public static async Task<bool> SerializeAsync(SymbolFileCache symCache, string directoryPath) {
    try {
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
