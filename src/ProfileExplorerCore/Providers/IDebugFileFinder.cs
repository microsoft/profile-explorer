// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.Core.Providers;

public interface IDebugFileFinder {
  Task<DebugFileSearchResult> FindDebugInfoFileAsync(string imagePath, SymbolFileSourceSettings settings = null);
  Task<DebugFileSearchResult> FindDebugInfoFileAsync(SymbolFileDescriptor symbolFile, SymbolFileSourceSettings settings = null);
}
