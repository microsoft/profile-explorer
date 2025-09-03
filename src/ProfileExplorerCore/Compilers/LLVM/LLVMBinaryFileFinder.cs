// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.Core.Compilers.LLVM;

public class LLVMBinaryFileFinder : IBinaryFileFinder {
  public async Task<BinaryFileSearchResult> FindBinaryFileAsync(BinaryFileDescriptor binaryFile, SymbolFileSourceSettings settings = null) {
    // LLVM implementation doesn't support binary file searching yet
    return BinaryFileSearchResult.None;
  }
}
