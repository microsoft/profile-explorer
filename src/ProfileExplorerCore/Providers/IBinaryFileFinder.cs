// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.Core.Providers;

public interface IBinaryFileFinder {
  Task<BinaryFileSearchResult> FindBinaryFileAsync(BinaryFileDescriptor binaryFile, SymbolFileSourceSettings settings = null);
}
