// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AvalonDock.Layout;
using ProfileExplorer.Core;
using ProfileExplorer.UI.Profile;
using ProtoBuf;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.Processing;
using ProfileExplorer.Core.Providers;
using ProfileExplorerUI.Session;
using ProfileExplorer.Core.Session;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class SessionInfo {
  [ProtoMember(1)]
  public string FilePath;
  [ProtoMember(2)]
  public SessionKind Kind;
  [ProtoMember(3)]
  public string Notes;
  [ProtoMember(4)]
  public string IRName;
  [ProtoMember(5)]
  public IRMode IRMode;
  [ProtoMember(6)]
  public bool IsSaved;
  public SessionInfo() { }

  public SessionInfo(string filePath, SessionKind kind, string irName, IRMode irMode) {
    FilePath = filePath;
    Kind = kind;
    IRName = irName;
    IRMode = irMode;
  }

  public bool IsDebugSession => Kind == SessionKind.DebugSession;
  public bool IsFileSession => Kind == SessionKind.FileSession;
  public bool IsSavedFileSession => IsFileSession && IsSaved;
}

[ProtoContract(SkipConstructor = true)]
public class PanelObjectPairState {
  [ProtoMember(1)]
  public ToolPanelKind PanelKind;
  [ProtoMember(2)]
  public byte[] StateObject;

  public PanelObjectPairState(ToolPanelKind panelKind, object stateObject) {
    PanelKind = panelKind;
    StateObject = stateObject as byte[];
  }
}

[ProtoContract]
public class OpenSectionState {
  [ProtoMember(1)]
  public Guid DocumentId;
  [ProtoMember(2)]
  public int SectionId;
  public OpenSectionState() { }

  public OpenSectionState(Guid documentId, int sectionId) {
    DocumentId = documentId;
    SectionId = sectionId;
  }
}

[ProtoContract]
public class SessionState {
  [ProtoMember(1)]
  public List<ILoadedDocumentState> Documents;
  [ProtoMember(2)]
  public List<PanelObjectPairState> GlobalPanelStates;
  [ProtoMember(3)]
  public List<OpenSectionState> OpenSections;
  [ProtoMember(4)]
  public SessionInfo Info;
  [ProtoMember(5)]
  public bool IsInTwoDocumentsDiffMode;
  [ProtoMember(6)]
  public Guid MainDocumentId;
  [ProtoMember(7)]
  public Guid DiffDocumentId;
  [ProtoMember(8)]
  public DiffModeState SectionDiffState;
  [ProtoMember(9)]
  public byte[] ProfileState;
  [ProtoMember(10)]
  public Dictionary<Guid, List<Tuple<int, PanelObjectPairState>>> DocumentPanelStates;

  public SessionState() {
    Documents = new List<ILoadedDocumentState>();
    GlobalPanelStates = new List<PanelObjectPairState>();
    OpenSections = new List<OpenSectionState>();
    Info = new SessionInfo();
    SectionDiffState = new DiffModeState();
    DocumentPanelStates = new Dictionary<Guid, List<Tuple<int, PanelObjectPairState>>>();
  }
}

[ProtoContract]
public class DiffModeState {
  [ProtoMember(1)]
  public bool IsEnabled;
  [ProtoMember(2)]
  public OpenSectionState LeftSection;
  [ProtoMember(3)]
  public OpenSectionState RightSection;

  public DiffModeState() {
    LeftSection = new OpenSectionState();
    RightSection = new OpenSectionState();
  }
}

public class PanelHostInfo {
  public PanelHostInfo(IToolPanel panel, LayoutAnchorable host) {
    Panel = panel;
    Host = host;
  }

  public IToolPanel Panel { get; set; }
  public LayoutAnchorable Host { get; set; }
  public ToolPanelKind PanelKind => Panel.PanelKind;
}

public class DocumentHostInfo {
  public DocumentHostInfo(IRDocumentHost document, LayoutDocument host) {
    DocumentHost = document;
    Host = host;
  }

  public IRDocumentHost DocumentHost { get; set; }
  public IRDocument Document => DocumentHost.TextView;
  public IRTextSection Section => Document.Section;
  public LayoutDocument Host { get; set; }
  public LayoutDocumentPane HostParent { get; set; }
  public bool IsActiveDocument { get; set; }
}

public class SessionSettings {
  public bool AutoReloadDocument { get; set; }
}

public class SessionStateManager : IDisposable {
  // {IR section ID -> list [{panel ID, state}]}
  private object lockObject_;
  private List<ILoadedDocument> documents_;
  private Dictionary<ToolPanelKind, object> globalPanelStates_;
  private PanelStateManager panelStateManager_;
  private List<CancelableTask> pendingTasks_;
  private ICompilerInfoProvider compilerInfo_;
  private bool watchDocumentChanges_;
  private bool disposed_;

  public SessionStateManager(string filePath, SessionKind sessionKind, ICompilerInfoProvider compilerInfo) {
    lockObject_ = new object();
    compilerInfo_ = compilerInfo;
    Info = new SessionInfo(filePath, sessionKind, compilerInfo.CompilerIRName, compilerInfo.IR.Mode);
    Info.Notes = "";
    documents_ = new List<ILoadedDocument>();
    globalPanelStates_ = new Dictionary<ToolPanelKind, object>();
    panelStateManager_ = new PanelStateManager();
    pendingTasks_ = new List<CancelableTask>();
    DocumentHosts = new List<DocumentHostInfo>();
    SectionDiffState = new DiffModeInfo();
    SessionStartTime = DateTime.UtcNow;
    IsAutoSaveEnabled = sessionKind != SessionKind.DebugSession;
    SyncDiffedDocuments = false;
  }

  public SessionInfo Info { get; set; }
  public List<ILoadedDocument> Documents => documents_;
  public ILoadedDocument MainDocument { get; set; }
  public ILoadedDocument DiffDocument { get; set; }
  public List<DocumentHostInfo> DocumentHosts { get; set; }
  public ProfileData ProfileData { get; set; }
  public ProfileFilterState ProfileFilter { get; set; }
  public DiffModeInfo SectionDiffState { get; set; }
  public bool NotifiedSessionStart { get; set; }
  public DateTime SessionStartTime { get; set; }
  public bool IsAutoSaveEnabled { get; set; }
  public bool IsInTwoDocumentsDiffMode => DiffDocument != null && SyncDiffedDocuments;
  public bool SyncDiffedDocuments { get; set; }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  public event EventHandler DocumentChanged;

  public static Task<SessionState> DeserializeSession(byte[] data) {
    return Task.Run(() => {
      byte[] decompressedData = CompressionUtils.Decompress(data);
      var state = UIStateSerializer.Deserialize<SessionState>(decompressedData);
      return state;
    });
  }

  public void EnterTwoDocumentDiffMode(ILoadedDocument diffDocument) {
    DiffDocument = diffDocument;
    SyncDiffedDocuments = true;
  }

  public void ExitTwoDocumentDiffMode() {
    DiffDocument = null;
    SyncDiffedDocuments = false;
  }

  public void RegisterLoadedDocument(ILoadedDocument docInfo) {
    documents_.Add(docInfo);

    if (!Info.IsFileSession && !Info.IsDebugSession) {
      docInfo.SetupDocumentWatcher();
      docInfo.DocumentChanged += DocumentWatcher_Changed;
      docInfo.ChangeDocumentWatcherState(watchDocumentChanges_);
    }
  }

  public void RemoveLoadedDocuemnt(ILoadedDocument document) {
    document.ChangeDocumentWatcherState(false);
    document.DocumentChanged -= DocumentWatcher_Changed;
    panelStateManager_.RemoveDocumentPanelStates(document.Id);
    documents_.Remove(document);
  }

  public ILoadedDocument FindLoadedDocument(IRTextSection section) {
    var summary = section.ParentFunction.ParentSummary;
    return documents_.Find(item => item.Summary == summary);
  }

  public ILoadedDocument FindLoadedDocument(IRTextFunction func) {
    var summary = func.ParentSummary;
    return documents_.Find(item => item.Summary == summary);
  }

  public ILoadedDocument FindLoadedDocument(IRTextSummary summary) {
    return documents_.Find(item => item.Summary == summary);
  }

  public IRTextFunction FindFunctionWithId(int funcNumber, Guid summaryId) {
    foreach (var doc in documents_) {
      if (doc.Summary.Id == summaryId) {
        return doc.Summary.GetFunctionWithId(funcNumber);
      }
    }

    return null;
  }

  public bool AreSectionSignaturesComputed(IRTextSection section) {
    var loadedDoc = FindLoadedDocument(section);
    return loadedDoc?.Loader.SectionSignaturesComputed ?? false;
  }

  public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section) {
    if (section == null) {
      globalPanelStates_[panel.PanelKind] = stateObject;
      return;
    }

    //? TODO: By not serializing the panel state object,
    //? references to object in a FunctionIR could be kept around
    //? after the section is unloaded, increasing memory usage more and more
    //? when switching sections
    var docInfo = FindLoadedDocument(section);
    panelStateManager_.SavePanelState(stateObject, panel, docInfo, section);
  }

  public void SaveDiffModePanelState(object stateObject, IToolPanel panel, IRTextSection section) {
    panelStateManager_.SaveDiffModePanelState(stateObject, panel, section);
  }

  public object LoadDiffModePanelState(IToolPanel panel, IRTextSection section) {
    return panelStateManager_.LoadDiffModePanelState(panel, section);
  }

  public void ClearDiffModePanelState() {
    panelStateManager_.ClearDiffModePanelState();
  }

  public object LoadPanelState(IToolPanel panel, IRTextSection section) {
    if (section == null) {
      return globalPanelStates_.TryGetValue(panel.PanelKind, out object value) ? value : null;
    }

    var docInfo = FindLoadedDocument(section);
    return panelStateManager_.LoadPanelState(panel, docInfo, section);
  }

  public void SaveDocumentState(object stateObject, IRTextSection section) {
    var docInfo = FindLoadedDocument(section);
    docInfo.SaveSectionState(stateObject, section);
  }

  public object LoadDocumentState(IRTextSection section) {
    var docInfo = FindLoadedDocument(section);
    return docInfo.LoadSectionState(section);
  }

  public Task<byte[]> SerializeSession() {
    var state = new SessionState();
    state.Info = Info;
    state.IsInTwoDocumentsDiffMode = IsInTwoDocumentsDiffMode;

    foreach (var docInfo in documents_) {
      var docState = docInfo.SerializeDocument();
      state.Documents.Add(docState);
      
      // Add panel states for this document
      var panelStates = panelStateManager_.SerializePanelStatesForDocument(docInfo);
      if (panelStates.Count > 0) {
        state.DocumentPanelStates[docInfo.Id] = panelStates;
      }

      if (docInfo == MainDocument) {
        state.MainDocumentId = docState.Id;
      }

      // For two-document diff mode, save the document IDs
      // so they are restored properly later.
      if (IsInTwoDocumentsDiffMode) {
        if (docInfo == DiffDocument) {
          state.DiffDocumentId = docState.Id;
        }
      }
    }

    foreach (var panelState in globalPanelStates_) {
      state.GlobalPanelStates.Add(
        new PanelObjectPairState(panelState.Key, panelState.Value as byte[]));
    }

    foreach (var docHost in DocumentHosts) {
      state.OpenSections.Add(CreateOpenSectionState(docHost.DocumentHost.Section));
    }

    if (SectionDiffState.IsEnabled && SectionDiffState.IsChangeCompleted) {
      state.SectionDiffState.IsEnabled = true;
      state.SectionDiffState.LeftSection = CreateOpenSectionState(SectionDiffState.LeftSection);
      state.SectionDiffState.RightSection = CreateOpenSectionState(SectionDiffState.RightSection);
    }

    return Task.Run(() => {
      byte[] data = UIStateSerializer.Serialize(state);
      byte[] compressedData = CompressionUtils.Compress(data);
      return compressedData;
    });
  }

  public void EndSession() {
    foreach (var docInfo in documents_) {
      docInfo.ChangeDocumentWatcherState(false);
      docInfo.Dispose();
    }

    documents_.Clear();
    IsAutoSaveEnabled = false;
  }

  public async Task CancelPendingTasks() {
    List<CancelableTask> tasks;

    lock (lockObject_) {
      tasks = pendingTasks_.CloneList();
    }

    foreach (var task in tasks) {
      task.Cancel();
      await task.WaitToCompleteAsync();
    }
  }

  public void RegisterCancelableTask(CancelableTask task) {
    lock (lockObject_) {
      pendingTasks_.Add(task);
    }
  }

  public void UnregisterCancelableTask(CancelableTask task) {
    lock (lockObject_) {
      pendingTasks_.Remove(task);
    }
  }

  public void ChangeDocumentWatcherState(bool enabled) {
    watchDocumentChanges_ = enabled;

    foreach (var docInfo in documents_) {
      docInfo.ChangeDocumentWatcherState(enabled);
    }
  }

  private OpenSectionState CreateOpenSectionState(IRTextSection section) {
    var loadedDoc = FindLoadedDocument(section);
    return new OpenSectionState(loadedDoc.Id, section.Id);
  }

  private void DocumentWatcher_Changed(object sender, EventArgs e) {
    DocumentChanged?.Invoke(sender, e);
  }

  protected virtual void Dispose(bool disposing) {
    if (!disposed_) {
      documents_.ForEach(item => item.Dispose());
      documents_.Clear();
      MainDocument = null;
      DiffDocument = null;
      disposed_ = true;
    }
  }

  ~SessionStateManager() {
    Dispose(false);
  }
}