// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;

namespace ProfileExplorer.Core.Compilers.LLVM;

public class LLVMDebugInfoProviderFactory : IDebugInfoProviderFactory {
  public IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile) {
    // LLVM implementation doesn't support debug info providers yet
    return null;
  }

  public IDebugInfoProvider GetOrCreateDebugInfoProvider(IRTextFunction function, ILoadedDocument loadedDoc) {
    return null;
  }
}
