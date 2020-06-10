// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core;
using ProtoBuf;
using AvalonDock.Layout;

namespace Client {
    public enum SessionKind {
        Default = 0,
        FileSession = 1,
        DebugSession = 2
    };

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

        public PanelObjectPairState(ToolPanelKind panelKind, object stateObject) {
            PanelKind = panelKind;
            StateObject = stateObject as byte[];
        }
    }

    [ProtoContract]
    public class SessionState {
        [ProtoMember(1)]
        public List<LoadedDocumentState> Documents;
        [ProtoMember(2)]
        public List<PanelObjectPairState> GlobalPanelStates;
        [ProtoMember(3)]
        public List<ulong> OpenSections;
        [ProtoMember(4)]
        public SessionInfo Info;

        public SessionState() {
            Documents = new List<LoadedDocumentState>();
            GlobalPanelStates = new List<PanelObjectPairState>();
            OpenSections = new List<ulong>();
            Info = new SessionInfo();
        }
    }


    public class PanelHostInfo {
        public IToolPanel Panel { get; set; }
        public LayoutAnchorable Host { get; set; }
        public ToolPanelKind PanelKind => Panel.PanelKind;

        public PanelHostInfo(IToolPanel panel, LayoutAnchorable host) {
            Panel = panel;
            Host = host;
        }
    }

    public class DocumentHostInfo {
        public IRDocumentHost Document { get; set; }
        public IRDocument DocumentView => Document.TextView;
        public LayoutDocument Host { get; set; }
        public LayoutDocumentPane HostParent { get; set; }
        public bool IsActiveDocument { get; set; }

        public DocumentHostInfo(IRDocumentHost document, LayoutDocument host) {
            Document = document;
            Host = host;
        }
    }


    public class DiffModeInfo {
        public bool IsEnabled { get; set; }
        public IRDocumentHost LeftDocument { get; set; }
        public IRDocumentHost RightDocument { get; set; }
        public IRTextSection LeftSection { get; set; }
        public IRTextSection RightSection { get; set; }
        public IRDocumentHost IgnoreNextScrollEventDocument { get; set; }
        public ManualResetEvent DiffModeChangeCompleted;

        public DiffModeInfo() {
            DiffModeChangeCompleted = new ManualResetEvent(true);
        }

        public void StartModeChange() {
            DiffModeChangeCompleted.WaitOne();
            DiffModeChangeCompleted.Reset();
        }

        public void EndModeChange() {
            DiffModeChangeCompleted.Set();
        }

        public void End() {
            IsEnabled = false;
            LeftDocument = RightDocument = null;
            LeftSection = RightSection = null;
            IgnoreNextScrollEventDocument = null;
        }

        public bool IsDiffDocument(IRDocumentHost docHost) {
            return docHost == LeftDocument ||
                   docHost == RightDocument;
        }
    }


    public class SessionSettings {
        public bool AutoReloadDocument { get; set; }
    }

    public class PanelObjectPair {
        public IToolPanel Panel;
        public object StateObject;

        public PanelObjectPair(IToolPanel panel, object stateObject) {
            Panel = panel;
            StateObject = stateObject;
        }
    }

    public class SessionStateManager {
        // {IR section ID -> list [{panel ID, state}]}
        private List<LoadedDocument> documents_;
        private Dictionary<ToolPanelKind, object> globalPanelStates_;
        private List<CancelableTaskInfo> pendingTasks_;
        private bool watchDocumentChanges_;

        public SessionInfo Info { get; set; }
        public List<LoadedDocument> Documents => documents_;
        public List<DocumentHostInfo> DocumentHosts { get; set; }
        public DiffModeInfo DiffState { get; set; }
        public bool NotifiedSessionStart { get; set; }
        public DateTime SessionStartTime { get; set; }
        public bool IsAutoSaveEnabled { get; set; }

        public event EventHandler DocumentChanged;

        public SessionStateManager(string filePath, SessionKind sessionKind) {
            Info = new SessionInfo(filePath, sessionKind);
            Info.Notes = "";

            documents_ = new List<LoadedDocument>();
            globalPanelStates_ = new Dictionary<ToolPanelKind, object>();
            pendingTasks_ = new List<CancelableTaskInfo>();

            DocumentHosts = new List<DocumentHostInfo>();
            DiffState = new DiffModeInfo();
            SessionStartTime = DateTime.UtcNow;
        }

        public void NewLoadedDocument(LoadedDocument docInfo) {
            documents_.Add(docInfo);

            if (!Info.IsFileSession && !Info.IsDebugSession) {
                docInfo.SetupDocumentWatcher();
                docInfo.DocumentChanged += DocumentWatcher_Changed;
                docInfo.ChangeDocumentWatcherState(watchDocumentChanges_);
            }
        }

        public void RemoveLoadedDocuemnt(LoadedDocument diffDocument_) {
            diffDocument_.ChangeDocumentWatcherState(false);
            documents_.Remove(diffDocument_);
            diffDocument_.Dispose();
        }

        public LoadedDocument FindDocument(IRTextSection section) {
            var summary = section.ParentFunction.ParentSummary;
            return documents_.Find((item) => item.Summary == summary);
        }

        public LoadedDocument FindDocument(IRTextFunction func) {
            var summary = func.ParentSummary;
            return documents_.Find((item) => item.Summary == summary);
        }

        public LoadedDocument FindDocument(IRTextSummary summary) {
            return documents_.Find((item) => item.Summary == summary);
        }

        public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section) {
            if (section == null) {
                globalPanelStates_[panel.PanelKind] = stateObject;
                return;
            }

            var docInfo = FindDocument(section);
            docInfo.SavePanelState(stateObject, panel, section);
        }

        public object LoadPanelState(IToolPanel panel, IRTextSection section) {
            if (section == null) {
                if (globalPanelStates_.TryGetValue(panel.PanelKind, out var value)) {
                    return value;
                }

                return null;
            }

            var docInfo = FindDocument(section);
            return docInfo.LoadPanelState(panel, section);
        }

        public void SaveDocumentState(object stateObject, IRTextSection section) {
            var docInfo = FindDocument(section);
            docInfo.SaveSectionState(stateObject, section);
        }

        public object LoadDocumentState(IRTextSection section) {
            var docInfo = FindDocument(section);
            return docInfo.LoadSectionState(section);
        }

        public Task<byte[]> SerializeSession(SectionLoader docLoader) {
            var state = new SessionState();
            state.Info = Info;

            foreach (var docInfo in documents_) {
                state.Documents.Add(docInfo.SerializeDocument());
            }

            foreach (var panelState in globalPanelStates_) {
                state.GlobalPanelStates.Add(
                    new PanelObjectPairState(panelState.Key, panelState.Value as byte[]));
            }

            foreach (var docHost in DocumentHosts) {
                state.OpenSections.Add(docHost.Document.Section.Id);
            }

            return Task.Run(() => {
                var data = StateSerializer.Serialize<SessionState>(state);
                var compressedData = CompressionUtils.Compress(data);
                return compressedData;
            });
        }

        public static Task<SessionState> DeserializeSession(byte[] data) {
            return Task.Run(() => {
                var decompressedData = CompressionUtils.Decompress(data);
                var state = StateSerializer.Deserialize<SessionState>(decompressedData);
                return state;
            });
        }

        public void EndSession() {
            List<CancelableTaskInfo> tasks;

            lock (this) {
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
        }

        public void RegisterCancelableTask(CancelableTaskInfo task) {
            lock (this) {
                pendingTasks_.Add(task);
            }
        }

        public void UnregisterCancelableTask(CancelableTaskInfo task) {
            lock (this) {
                pendingTasks_.Remove(task);
            }
        }

        private void DocumentWatcher_Changed(object sender, EventArgs e) {
            DocumentChanged?.Invoke(sender, e);
        }

        internal void ChangeDocumentWatcherState(bool enabled) {
            watchDocumentChanges_ = enabled;

            foreach (var docInfo in documents_) {
                docInfo.ChangeDocumentWatcherState(enabled);
            }
        }

    }
}
