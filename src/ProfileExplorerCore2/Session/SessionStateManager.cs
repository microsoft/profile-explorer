// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProfileExplorerCore2.Compilers.Architecture;
using ProfileExplorerCore2.Profile.Data;
using ProfileExplorerCore2.Profile.Processing;
using ProfileExplorerCore2.Providers;
using ProfileExplorerCore2.Utilities;
using ProtoBuf;

namespace ProfileExplorerCore2.Session;

public enum SessionKind {
  Default = 0,
  FileSession = 1,
  DebugSession = 2
}

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
  public List<LoadedDocumentState> Documents;
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

  public SessionState() {
    Documents = new List<LoadedDocumentState>();
    OpenSections = new List<OpenSectionState>();
    Info = new SessionInfo();
  }
}

public class SessionStateManager : IDisposable {
  private object lockObject_;
  private List<LoadedDocument> documents_;
  private List<CancelableTask> pendingTasks_;
  private ICompilerInfoProvider compilerInfo_;
  private bool disposed_;

  public SessionStateManager(string filePath, SessionKind sessionKind, ICompilerInfoProvider compilerInfo) {
    lockObject_ = new object();
    compilerInfo_ = compilerInfo;
    Info = new SessionInfo(filePath, sessionKind, compilerInfo.CompilerIRName, compilerInfo.IR.Mode);
    Info.Notes = "";
    documents_ = new List<LoadedDocument>();
    pendingTasks_ = new List<CancelableTask>();
    SessionStartTime = DateTime.UtcNow;
    IsAutoSaveEnabled = sessionKind != SessionKind.DebugSession;
  }

  public SessionInfo Info { get; set; }
  public List<LoadedDocument> Documents => documents_;
  public LoadedDocument MainDocument { get; set; }
  public LoadedDocument DiffDocument { get; set; }
  public ProfileData ProfileData { get; set; }
  public ProfileFilterState ProfileFilter { get; set; }
  public DateTime SessionStartTime { get; set; }
  public bool IsAutoSaveEnabled { get; set; }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  public static Task<SessionState> DeserializeSession(byte[] data) {
    return Task.Run(() => {
      byte[] decompressedData = CompressionUtils.Decompress(data);
      var state = StateSerializer.Deserialize<SessionState>(decompressedData);
      return state;
    });
  }

  public void RegisterLoadedDocument(LoadedDocument docInfo) {
    documents_.Add(docInfo);
  }

  public void RemoveLoadedDocuemnt(LoadedDocument document) {
    documents_.Remove(document);
  }

  public LoadedDocument FindLoadedDocument(IRTextSection section) {
    var summary = section.ParentFunction.ParentSummary;
    return documents_.Find(item => item.Summary == summary);
  }

  public LoadedDocument FindLoadedDocument(IRTextFunction func) {
    var summary = func.ParentSummary;
    return documents_.Find(item => item.Summary == summary);
  }

  public LoadedDocument FindLoadedDocument(IRTextSummary summary) {
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

  public void EndSession() {
    documents_.Clear();
    IsAutoSaveEnabled = false;
  }

  public async Task CancelPendingTasks() {
    List<CancelableTask> tasks;
    lock (lockObject_) {
      tasks = new List<CancelableTask>(pendingTasks_);
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

  protected virtual void Dispose(bool disposing) {
    if (!disposed_) {
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