// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Providers;

public class ASMDebugFileFinder : IDebugFileFinder {
  public async Task<DebugFileSearchResult> FindDebugInfoFileAsync(string imagePath, SymbolFileSourceSettings settings = null) {
    using var info = new PEBinaryInfoProvider(imagePath);

    if (!info.Initialize()) {
      return Utils.LocateDebugInfoFile(imagePath, ".json");
    }

    switch (info.BinaryFileInfo.FileKind) {
      case BinaryFileKind.Native: {
        if (settings == null) {
          // Make sure the binary directory is also included in the symbol search.
          settings = CoreSettingsProvider.SymbolSettings.Clone();
          settings.InsertSymbolPath(imagePath);
        }

        return await FindDebugInfoFileAsync(info.SymbolFileInfo, settings).ConfigureAwait(false);
      }
    }

    return DebugFileSearchResult.None;
  }

  public async Task<DebugFileSearchResult> FindDebugInfoFileAsync(SymbolFileDescriptor symbolFile, SymbolFileSourceSettings settings = null) {
    if (settings == null) {
      settings = CoreSettingsProvider.SymbolSettings;
    }

    return await PDBDebugInfoProvider.LocateDebugInfoFileAsync(symbolFile, settings).ConfigureAwait(false);
  }
}
