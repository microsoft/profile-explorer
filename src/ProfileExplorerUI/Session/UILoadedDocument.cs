// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ProfileExplorerCore2;
using ProfileExplorer.UI.Profile;
using ProtoBuf;
using ProfileExplorerCore2.Providers;
using ProfileExplorerCore2.Binary;
using ProfileExplorerCore2.Profile.Utils;
using ProfileExplorerCore2.Session;
using ProfileExplorerUI.Session;

namespace ProfileExplorer.UI;

[ProtoContract]
public class UILoadedDocumentState : LoadedDocumentState, IUILoadedDocumentState {
  // Inherited properties from LoadedDocumentState (ProtoMember 1–7, 9)
  // Add new property for panel states:
  [ProtoMember(8)]
  public List<Tuple<int, PanelObjectPairState>> PanelStates { get; set; }

  public UILoadedDocumentState() : base() {
    PanelStates = new List<Tuple<int, PanelObjectPairState>>();
  }

  public UILoadedDocumentState(Guid id) : this() {
    Id = id;
  }
}

public class UILoadedDocument : LoadedDocument, IUILoadedDocument {
  public Dictionary<IRTextSection, List<PanelObjectPair>> PanelStates;
  private FileSystemWatcher documentWatcher_;
  private IRTextSummary summary_;
  private bool disposed_;

  public UILoadedDocument(string filePath, string modulePath, Guid id) : base(filePath, modulePath, id) {
    PanelStates = new Dictionary<IRTextSection, List<PanelObjectPair>>();
  }

  new public static UILoadedDocument CreateDummyDocument(string name) {
    return CreateDummyDocument(name, Guid.NewGuid());
  }

  new public static UILoadedDocument CreateDummyDocument(string name, Guid id) {
    var doc = new UILoadedDocument(name, name, id);
    doc.Summary = new IRTextSummary(name);
    doc.Loader = new DummySectionLoader(); // Placeholder used to prevent null pointers.
    return doc;
  }

  public event EventHandler DocumentChanged;

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

  public override IUILoadedDocumentState SerializeDocument() {
    var state = new UILoadedDocumentState(Id) {
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

    // Used by profiling to represent missing binaries.
    foreach (var func in summary_.Functions) {
      state.FunctionNames.Add(func.Name);
    }

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

    DocumentChanged?.Invoke(this, EventArgs.Empty);
  }

  ~UILoadedDocument() {
    Dispose(false);
  }
}