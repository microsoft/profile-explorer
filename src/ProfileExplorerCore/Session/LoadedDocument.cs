// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile.Utils;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Utilities;
using ProtoBuf;

namespace ProfileExplorer.Core.Session;

[ProtoContract]
public class LoadedDocumentState : ILoadedDocumentState {
  [ProtoMember(1)]
  public Guid Id { get; set; }
  [ProtoMember(2)]
  public string ModuleName { get; set; }
  [ProtoMember(3)]
  public string FilePath { get; set; }
  [ProtoMember(4)]
  public BinaryFileSearchResult BinaryFile { get; set; }
  [ProtoMember(5)]
  public DebugFileSearchResult DebugInfoFile { get; set; }
  [ProtoMember(6)]
  public byte[] DocumentText { get; set; }
  [ProtoMember(7)]
  public List<Tuple<int, byte[]>> SectionStates { get; set; }
  [ProtoMember(8)]
  public List<string> FunctionNames { get; set; }

  public LoadedDocumentState() {
    SectionStates = new List<Tuple<int, byte[]>>();
    FunctionNames = new List<string>();
  }

  public LoadedDocumentState(Guid id) : this() {
    Id = id;
  }
}

public class LoadedDocument : ILoadedDocument {
  public Dictionary<IRTextSection, object> SectionStates;
  private FileSystemWatcher documentWatcher_;
  private IRTextSummary summary_;
  private bool disposed_;

  public LoadedDocument(string filePath, string modulePath, Guid id) {
    FilePath = filePath;
    ModuleName = Utils.TryGetFileName(modulePath ?? filePath);
    Id = id;
    SectionStates = new Dictionary<IRTextSection, object>();
  }

  public Guid Id { get; set; }
  public string ModuleName { get; set; }
  public string FilePath { get; set; }
  public BinaryFileSearchResult BinaryFile { get; set; }
  public DebugFileSearchResult DebugInfoFile { get; set; }
  public SymbolFileDescriptor SymbolFileInfo { get; set; }
  public IRTextSectionLoader Loader { get; set; }

  public IRTextSummary Summary {
    get => summary_;
    set {
      summary_ = value;
      if (summary_ != null) {
        summary_.Id = Id;
        summary_.SetModuleName(ModuleName);
      }
    }
  }

  public IDebugInfoProvider DebugInfo { get; set; } // Used for managed binaries.
  public bool IsDummyDocument => Loader is DummySectionLoader;
  public bool DebugInfoFileExists => DebugInfoFile is {Found: true};
  public bool BinaryFileExists => BinaryFile is {Found: true};
  public bool HasSymbolFileInfo => SymbolFileInfo != null;
  public string FileName => Utils.TryGetFileName(FilePath);
  public EnsureBinaryLoadedDelegate EnsureBinaryLoaded { get; set; }

  public event EventHandler DocumentChanged;

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  public static LoadedDocument CreateDummyDocument(string name) {
    return CreateDummyDocument(name, Guid.NewGuid());
  }

  public static LoadedDocument CreateDummyDocument(string name, Guid id) {
    var doc = new LoadedDocument(name, name, id);
    doc.Summary = new IRTextSummary(name);
    doc.Loader = new DummySectionLoader(); // Placeholder used to prevent null pointers.
    return doc;
  }

  public IRTextFunction AddDummyFunction(string name) {
    var func = new IRTextFunction(name);
    func.ParentSummary = summary_;
    var section = new IRTextSection(func, func.Name, IRPassOutput.Empty);
    func.AddSection(section);
    summary_.AddFunction(func);
    summary_.AddSection(section);
    return func;
  }

  public void AddDummyFunctions(List<string> funcNames) {
    foreach (string name in funcNames) {
      if (summary_.FindFunction(name) == null) {
        AddDummyFunction(name);
      }
    }
  }

  public void SaveSectionState(object stateObject, IRTextSection section) {
    SectionStates[section] = stateObject;
  }

  public object LoadSectionState(IRTextSection section) {
    return SectionStates.TryGetValue(section, out object stateObject) ? stateObject : null;
  }

  public void SetupDocumentWatcher() {
    try {
      string fileDir = Path.GetDirectoryName(FilePath);
      string fileName = Path.GetFileName(FilePath);
      documentWatcher_ = new FileSystemWatcher(fileDir, fileName);
      documentWatcher_.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
      documentWatcher_.Changed += DocumentWatcher_Changed;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to setup document watcher for file {FilePath}: {ex.Message}");
      documentWatcher_ = null;
    }
  }

  public void ChangeDocumentWatcherState(bool enabled) {
    if (documentWatcher_ != null) {
      documentWatcher_.EnableRaisingEvents = enabled;
    }
  }

  public virtual ILoadedDocumentState SerializeDocument() {
    var state = new LoadedDocumentState(Id) {
      ModuleName = ModuleName, FilePath = FilePath, BinaryFile = BinaryFile,
      DebugInfoFile = DebugInfoFile,
      DocumentText = Loader.GetDocumentTextBytes()
    };

    foreach (var sectionState in SectionStates) {
      state.SectionStates.Add(new Tuple<int, byte[]>(sectionState.Key.Id,
                                                     sectionState.Value as byte[]));
    }

    // Used by profiling to represent missing binaries.
    foreach (var func in summary_.Functions) {
      state.FunctionNames.Add(func.Name);
    }

    return state;
  }

  private void DocumentWatcher_Changed(object sender, FileSystemEventArgs e) {
    if (e.ChangeType != WatcherChangeTypes.Changed) {
      return;
    }

    DocumentChanged?.Invoke(this, EventArgs.Empty);
  }

  protected virtual void Dispose(bool disposing) {
    if (!disposed_) {
      if (disposing) {
        documentWatcher_?.Dispose();
      }
      Loader?.Dispose();
      Loader = null;
      Summary = null;
      documentWatcher_ = null;
      disposed_ = true;
    }
  }

  ~LoadedDocument() {
    Dispose(false);
  }
}