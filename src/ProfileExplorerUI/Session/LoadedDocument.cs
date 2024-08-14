// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ProfileExplorer.Core;
using ProfileExplorer.UI.Compilers;
using ProfileExplorer.UI.Profile;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract]
public class LoadedDocumentState {
  [ProtoMember(1)]
  public Guid Id;
  [ProtoMember(2)]
  public string ModuleName;
  [ProtoMember(3)]
  public string FilePath;
  [ProtoMember(4)]
  public BinaryFileSearchResult BinaryFile;
  [ProtoMember(5)]
  public DebugFileSearchResult DebugInfoFile;
  [ProtoMember(6)]
  public byte[] DocumentText;
  [ProtoMember(7)]
  public List<Tuple<int, byte[]>> SectionStates;
  [ProtoMember(8)]
  public List<Tuple<int, PanelObjectPairState>> PanelStates;
  [ProtoMember(9)]
  public List<string> FunctionNames;

  public LoadedDocumentState() {
    SectionStates = new List<Tuple<int, byte[]>>();
    PanelStates = new List<Tuple<int, PanelObjectPairState>>();
    FunctionNames = new List<string>();
  }

  public LoadedDocumentState(Guid id) : this() {
    Id = id;
  }
}

public class LoadedDocument : IDisposable {
  public Dictionary<IRTextSection, List<PanelObjectPair>> PanelStates;
  public Dictionary<IRTextSection, object> SectionStates;
  private FileSystemWatcher documentWatcher_;
  private IRTextSummary summary_;

  public LoadedDocument(string filePath, string modulePath, Guid id) {
    FilePath = filePath;
    ModuleName = Utils.TryGetFileName(modulePath ?? filePath);
    Id = id;

    PanelStates = new Dictionary<IRTextSection, List<PanelObjectPair>>();
    SectionStates = new Dictionary<IRTextSection, object>();
  }

  public event EventHandler DocumentChanged;
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

  public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section) {
    if (!PanelStates.TryGetValue(section, out var list)) {
      list = new List<PanelObjectPair>();
      PanelStates.Add(section, list);
    }

    var state = list.Find(item => item.Panel == panel);

    if (state != null) {
      state.StateObject = stateObject;
    }
    else {
      list.Add(new PanelObjectPair(panel, stateObject));
    }
  }

  public object LoadPanelState(IToolPanel panel, IRTextSection section) {
    if (PanelStates.TryGetValue(section, out var list)) {
      var state = list.Find(item => item.Panel == panel);
      return state?.StateObject;
    }

    return null;
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

  public LoadedDocumentState SerializeDocument() {
    var state = new LoadedDocumentState(Id) {
      ModuleName = ModuleName, FilePath = FilePath, BinaryFile = BinaryFile,
      DebugInfoFile = DebugInfoFile,
      DocumentText = Loader.GetDocumentTextBytes()
    };

    foreach (var sectionState in SectionStates) {
      state.SectionStates.Add(new Tuple<int, byte[]>(sectionState.Key.Id,
                                                     sectionState.Value as byte[]));
    }

    foreach (var panelState in PanelStates) {
      foreach (var panelStatePair in panelState.Value) {
        if (panelStatePair.Panel.SavesStateToFile) {
          state.PanelStates.Add(new Tuple<int, PanelObjectPairState>(
                                  panelState.Key.Id,
                                  new PanelObjectPairState(panelStatePair.Panel.PanelKind,
                                                           panelStatePair.StateObject)));
        }
      }
    }

    //if (IsDummyDocument) {
    // Used by profiling to represent missing binaries.
    foreach (var func in summary_.Functions) {
      state.FunctionNames.Add(func.Name);
    }
    //}

    return state;
  }

  public void ChangeDocumentWatcherState(bool enabled) {
    if (documentWatcher_ != null) {
      documentWatcher_.EnableRaisingEvents = enabled;
    }
  }

  private void DocumentWatcher_Changed(object sender, FileSystemEventArgs e) {
    if (e.ChangeType != WatcherChangeTypes.Changed) {
      return;
    }

    DocumentChanged?.Invoke(this, new EventArgs());
  }

        #region IDisposable Support

  private bool disposed_;

  protected virtual void Dispose(bool disposing) {
    if (!disposed_) {
      Loader?.Dispose();
      Loader = null;
      Summary = null;
      disposed_ = true;
    }
  }

  ~LoadedDocument() {
    Dispose(false);
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

        #endregion
}