// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AvalonDock.Layout;
using ICSharpCode.AvalonEdit.Document;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI {
    public enum SessionKind {
        Default = 0,
        FileSession = 1,
        DebugSession = 2
    }

    [ProtoContract]
    public class SessionInfo {
        [ProtoMember(1)]
        public string FilePath;
        [ProtoMember(2)]
        public SessionKind Kind;
        [ProtoMember(3)]
        public string Notes;

        public SessionInfo() { }

        public SessionInfo(string filePath, SessionKind kind) {
            FilePath = filePath;
            Kind = kind;
        }

        public bool IsDebugSession => Kind == SessionKind.DebugSession;
        public bool IsFileSession => Kind == SessionKind.FileSession;
    }

    [ProtoContract]
    public class PanelObjectPairState {
        [ProtoMember(1)]
        public ToolPanelKind PanelKind;
        [ProtoMember(2)]
        public byte[] StateObject;

        public PanelObjectPairState() { }

        //? TODO: state objects should be just byte[] everywhere
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
        public List<LoadedDocumentState> Documents;
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

        public SessionState() {
            Documents = new List<LoadedDocumentState>();
            GlobalPanelStates = new List<PanelObjectPairState>();
            OpenSections = new List<OpenSectionState>();
            Info = new SessionInfo();
            SectionDiffState = new DiffModeState();
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
        public IRDocument DocumentView => DocumentHost.TextView;
        public LayoutDocument Host { get; set; }
        public LayoutDocumentPane HostParent { get; set; }
        public bool IsActiveDocument { get; set; }
    }

    public class SessionSettings {
        public bool AutoReloadDocument { get; set; }
    }

    public class PanelObjectPair {
        public IToolPanel Panel { get; set; }
        public object StateObject { get; set; }

        public PanelObjectPair(IToolPanel panel, object stateObject) {
            Panel = panel;
            StateObject = stateObject;
        }
    }

    public class BaseDiffSectionGroup {
        public BaseDiffSectionGroup(IRTextSection baseSection, 
                                    IRTextSection diffSection, 
                                    IRTextSection stateSection) {
            BaseSection = baseSection;
            DiffSection = diffSection;
            StateSection = stateSection;
        }

        public IRTextSection BaseSection { get; set; }
        public IRTextSection DiffSection { get; set; }
        public IRTextSection StateSection { get; set; }

        public override bool Equals(object obj) {
            return obj is BaseDiffSectionGroup group &&
                   EqualityComparer<IRTextSection>.Default.Equals(BaseSection, group.BaseSection) &&
                   EqualityComparer<IRTextSection>.Default.Equals(DiffSection, group.DiffSection) &&
                   EqualityComparer<IRTextSection>.Default.Equals(StateSection, group.StateSection);
        }

        public override int GetHashCode() {
            return HashCode.Combine(BaseSection, DiffSection, StateSection);
        }
    }

    public class SessionStateManager : IDisposable {
        // {IR section ID -> list [{panel ID, state}]}
        private object lockObject_;
        private List<LoadedDocument> documents_;
        private Dictionary<ToolPanelKind, object> globalPanelStates_;
        private Dictionary<BaseDiffSectionGroup, List<PanelObjectPair>> diffPanelStates_;
        private List<CancelableTask> pendingTasks_;
        private bool watchDocumentChanges_;

        public SessionStateManager(string filePath, SessionKind sessionKind) {
            lockObject_ = new object();
            Info = new SessionInfo(filePath, sessionKind);
            Info.Notes = "";
            documents_ = new List<LoadedDocument>();
            globalPanelStates_ = new Dictionary<ToolPanelKind, object>();
            diffPanelStates_ = new Dictionary<BaseDiffSectionGroup, List<PanelObjectPair>>();
            pendingTasks_ = new List<CancelableTask>();
            DocumentHosts = new List<DocumentHostInfo>();
            SectionDiffState = new DiffModeInfo();
            SessionStartTime = DateTime.UtcNow;
            IsAutoSaveEnabled = sessionKind != SessionKind.DebugSession;
            SyncDiffedDocuments = false;
        }

        public SessionInfo Info { get; set; }
        public List<LoadedDocument> Documents => documents_;
        public LoadedDocument MainDocument { get; set; }
        public LoadedDocument DiffDocument { get; set; }
        public List<DocumentHostInfo> DocumentHosts { get; set; }
        public ProfileData ProfileData { get; set; }

        public DiffModeInfo SectionDiffState { get; set; }
        public bool NotifiedSessionStart { get; set; }
        public DateTime SessionStartTime { get; set; }
        public bool IsAutoSaveEnabled { get; set; }
        public bool IsInTwoDocumentsDiffMode => DiffDocument != null && SyncDiffedDocuments;
        public bool SyncDiffedDocuments { get; set; }

        public event EventHandler DocumentChanged;

        public void EnterTwoDocumentDiffMode(LoadedDocument diffDocument) {
            DiffDocument = diffDocument;
            SyncDiffedDocuments = true;
        }

        public void ExitTwoDocumentDiffMode() {
            DiffDocument = null;
            SyncDiffedDocuments = false;
        }

        public void RegisterLoadedDocument(LoadedDocument docInfo) {
            documents_.Add(docInfo);

            if (!Info.IsFileSession && !Info.IsDebugSession) {
                docInfo.SetupDocumentWatcher();
                docInfo.DocumentChanged += DocumentWatcher_Changed;
                docInfo.ChangeDocumentWatcherState(watchDocumentChanges_);
            }
        }

        public void RemoveLoadedDocuemnt(LoadedDocument document) {
            document.ChangeDocumentWatcherState(false);
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
            docInfo.SavePanelState(stateObject, panel, section);
        }

        public void SaveDiffModePanelState(object stateObject, IToolPanel panel, IRTextSection section) {
            var group = new BaseDiffSectionGroup(SectionDiffState.LeftSection,
                                                 SectionDiffState.RightSection, section);
            if(!diffPanelStates_.TryGetValue(group, out var list)) {
                list = new List<PanelObjectPair>();
                diffPanelStates_.Add(group, list);
            }

            var state = list.Find(item => item.Panel == panel);

            if (state != null) {
                state.StateObject = stateObject;
            }
            else {
                list.Add(new PanelObjectPair(panel, stateObject));
            }
        }

        public object LoadDiffModePanelState(IToolPanel panel, IRTextSection section) {
            var group = new BaseDiffSectionGroup(SectionDiffState.LeftSection,
                                                 SectionDiffState.RightSection, section);
            if (diffPanelStates_.TryGetValue(group, out var list)) {
                var state = list.Find(item => item.Panel == panel);
                return state?.StateObject;
            }

            return null;
        }

        public void ClearDiffModePanelState() {
            diffPanelStates_.Clear();
        }

        public object LoadPanelState(IToolPanel panel, IRTextSection section) {
            if (section == null) {
                return globalPanelStates_.TryGetValue(panel.PanelKind, out var value) ? value : null;
            }

            var docInfo = FindLoadedDocument(section);
            return docInfo.LoadPanelState(panel, section);
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

            if(ProfileData != null) {
                state.ProfileState = ProfileData.Serialize();
            }

            return Task.Run(() => {
                var data = StateSerializer.Serialize(state);
                var compressedData = CompressionUtils.Compress(data);
                return compressedData;
            });
        }

        private OpenSectionState CreateOpenSectionState(IRTextSection section) {
            var loadedDoc = FindLoadedDocument(section);
            return new OpenSectionState(loadedDoc.Id, section.Id);
        }

        public static Task<SessionState> DeserializeSession(byte[] data) {
            return Task.Run(() => {
                var decompressedData = CompressionUtils.Decompress(data);
                var state = StateSerializer.Deserialize<SessionState>(decompressedData);
                return state;
            });
        }

        public void EndSession() {
            List<CancelableTask> tasks;

            lock (lockObject_) {
                tasks = pendingTasks_.CloneList();
            }

            foreach (var task in tasks) {
                task.Cancel();
                task.WaitToComplete();
            }

            foreach (var docInfo in documents_) {
                docInfo.ChangeDocumentWatcherState(false);
                docInfo.Dispose();
            }

            documents_.Clear();
            IsAutoSaveEnabled = false;
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

        private void DocumentWatcher_Changed(object sender, EventArgs e) {
            DocumentChanged?.Invoke(sender, e);
        }

        public void ChangeDocumentWatcherState(bool enabled) {
            watchDocumentChanges_ = enabled;

            foreach (var docInfo in documents_) {
                docInfo.ChangeDocumentWatcherState(enabled);
            }
        }

        #region IDisposable Support

        private bool disposed_;

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

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
