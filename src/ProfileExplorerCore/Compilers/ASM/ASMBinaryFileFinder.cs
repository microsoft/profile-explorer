// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Compilers.ASM;

public class ASMBinaryFileFinder : IBinaryFileFinder {
  public async Task<BinaryFileSearchResult> FindBinaryFileAsync(BinaryFileDescriptor binaryFile, SymbolFileSourceSettings settings = null) {
    if (settings == null) {
      // Make sure the binary directory is also included in the symbol search.
      settings = CoreSettingsProvider.SymbolSettings.Clone();
    }

    return await PEBinaryInfoProvider.LocateBinaryFileAsync(binaryFile, settings).ConfigureAwait(false);
  }
}
