// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Session;

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

  public event EventHandler DocumentChanged;
  public void SetupDocumentWatcher();
  public void ChangeDocumentWatcherState(bool enabled);

  public void AddDummyFunctions(List<string> funcNames);
  IRTextFunction AddDummyFunction(string name);
  void SaveSectionState(object stateObject, IRTextSection section);
  object LoadSectionState(IRTextSection section);
  ILoadedDocumentState SerializeDocument();
}