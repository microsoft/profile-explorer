// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Compilers.ASM;

public class ASMDebugInfoProviderFactory : IDebugInfoProviderFactory {
  // Use FilePath as key instead of DebugFileSearchResult object reference.
  // Different threads may create different DebugFileSearchResult objects for the same PDB,
  // and without Equals/GetHashCode overrides, reference equality would miss cache hits.
  private static readonly Dictionary<string, IDebugInfoProvider> loadedDebugInfo_ = new();
  private static readonly object loadedDebugInfoLock_ = new();

  public IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile) {
    if (!debugFile.Found || string.IsNullOrEmpty(debugFile.FilePath)) {
      return null;
    }

    // Use static lock since the dictionary is static (shared across all factory instances)
    lock (loadedDebugInfoLock_) {
      if (loadedDebugInfo_.TryGetValue(debugFile.FilePath, out var provider)) {
        return provider;
      }

      var newProvider = new PDBDebugInfoProvider(CoreSettingsProvider.SymbolSettings);

      if (newProvider.LoadDebugInfo(debugFile, null)) {
        loadedDebugInfo_[debugFile.FilePath] = newProvider;
        return newProvider;
      }

      return null;
    }
  }

  public IDebugInfoProvider GetOrCreateDebugInfoProvider(IRTextFunction function, ILoadedDocument loadedDoc) {
    lock (loadedDoc) {
      if (loadedDoc.DebugInfo != null) {
        return loadedDoc.DebugInfo;
      }
    }

    if (!loadedDoc.DebugInfoFileExists) {
      return null;
    }

    if (loadedDoc.DebugInfoFileExists) {
      var debugInfo = CreateDebugInfoProvider(loadedDoc.DebugInfoFile);

      if (debugInfo != null) {
        lock (loadedDoc) {
          loadedDoc.DebugInfo = debugInfo;
          return debugInfo;
        }
      }
    }

    return null;
  }
}
