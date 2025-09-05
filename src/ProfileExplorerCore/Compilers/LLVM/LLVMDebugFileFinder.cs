// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Compilers.LLVM;

public class LLVMDebugFileFinder : IDebugFileFinder {
  public async Task<DebugFileSearchResult> FindDebugInfoFileAsync(string imagePath, SymbolFileSourceSettings settings = null) {
    // LLVM implementation uses a simple file location approach
    return Utils.LocateDebugInfoFile(imagePath, ".pdb");
  }

  public async Task<DebugFileSearchResult> FindDebugInfoFileAsync(SymbolFileDescriptor symbolFile, SymbolFileSourceSettings settings = null) {
    // LLVM implementation doesn't support symbol file descriptor searches yet
    return DebugFileSearchResult.None;
  }
}
