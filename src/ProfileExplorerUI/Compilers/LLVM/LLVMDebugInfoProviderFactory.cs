// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Providers;

namespace ProfileExplorer.UI.Compilers.LLVM;

public class LLVMDebugInfoProviderFactory : IDebugInfoProviderFactory {
  public IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile) {
    // LLVM implementation doesn't support debug info providers yet
    return null;
  }
}
