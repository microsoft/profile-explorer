// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Providers;

public class DebugInfoProviderFactory : IDebugInfoProviderFactory {
  private static readonly Dictionary<DebugFileSearchResult, IDebugInfoProvider> loadedDebugInfo_ = new();

  public IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile) {
    if (!debugFile.Found) {
      return null;
    }

    lock (this) {
      if (loadedDebugInfo_.TryGetValue(debugFile, out var provider)) {
        return provider;
      }

      var newProvider = new PDBDebugInfoProvider(CoreSettingsProvider.SymbolSettings);

      if (newProvider.LoadDebugInfo(debugFile, provider)) {
        loadedDebugInfo_[debugFile] = newProvider;
        provider?.Dispose();
        return newProvider;
      }

      return null;
    }
  }
}
