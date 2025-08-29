// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Binary;

namespace ProfileExplorer.Core.Providers;

public interface IDebugInfoProviderFactory {
  IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile);
}
