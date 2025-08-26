// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorerCore.Binary;
using ProfileExplorerCore.Profile.Data;
using ProfileExplorerCore.Providers;
using ProfileExplorerCore.Settings;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorerCore.Session;

public interface ILoadedDocument : IDisposable {
  Guid Id { get; set; }
  string ModuleName { get; set; }
  string FilePath { get; set; }
  BinaryFileSearchResult BinaryFile { get; set; }
  DebugFileSearchResult DebugInfoFile { get; set; }
  SymbolFileDescriptor SymbolFileInfo { get; set; }
  IRTextSectionLoader Loader { get; set; }
  IRTextSummary Summary { get; set; }
  IDebugInfoProvider DebugInfo { get; set; }
  bool IsDummyDocument { get; }
  bool DebugInfoFileExists { get; }
  bool BinaryFileExists { get; }
  bool HasSymbolFileInfo { get; }
  string FileName { get; }

  public void AddDummyFunctions(List<string> funcNames);
  IRTextFunction AddDummyFunction(string name);
  void SaveSectionState(object stateObject, IRTextSection section);
  object LoadSectionState(IRTextSection section);
  ILoadedDocumentState SerializeDocument();
}