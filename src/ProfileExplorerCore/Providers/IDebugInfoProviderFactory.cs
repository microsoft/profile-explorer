// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Session;

namespace ProfileExplorer.Core.Providers;

public interface IDebugInfoProviderFactory {
  IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile);

  IDebugInfoProvider GetOrCreateDebugInfoProvider(IRTextFunction function, ILoadedDocument loadedDoc);
}
