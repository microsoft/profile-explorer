// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorerCore2.Providers;

namespace ProfileExplorerCore2.Session;

public interface ILoadedDocumentState {
  Guid Id { get; set; }
  string ModuleName { get; set; }
  string FilePath { get; set; }
  BinaryFileSearchResult BinaryFile { get; set; }
  DebugFileSearchResult DebugInfoFile { get; set; }
  byte[] DocumentText { get; set; }
  List<Tuple<int, byte[]>> SectionStates { get; set; }
  List<string> FunctionNames { get; set; }
}