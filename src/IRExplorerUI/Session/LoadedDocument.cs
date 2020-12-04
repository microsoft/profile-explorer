// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using IRExplorerCore;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract]
    public class LoadedDocumentState {
        [ProtoMember(1)]
        public string FilePath;
        [ProtoMember(2)]
        public byte[] DocumentText;
        [ProtoMember(3)]
        public List<Tuple<ulong, byte[]>> SectionStates;
        [ProtoMember(4)]
        public List<Tuple<ulong, PanelObjectPairState>> PanelStates;
        [ProtoMember(5)]
        public Guid Id;

        public LoadedDocumentState() {
            SectionStates = new List<Tuple<ulong, byte[]>>();
            PanelStates = new List<Tuple<ulong, PanelObjectPairState>>();
        }

        public LoadedDocumentState(Guid id) : this() {
            Id = id;
        }
    }

    public class LoadedDocument : IDisposable {
        private FileSystemWatcher documentWatcher_;
        public Dictionary<IRTextSection, List<PanelObjectPair>> PanelStates;
        public Dictionary<IRTextSection, object> SectionStates;

        public LoadedDocument(string filePath, Guid id) {
            FilePath = filePath;
            PanelStates = new Dictionary<IRTextSection, List<PanelObjectPair>>();
            SectionStates = new Dictionary<IRTextSection, object>();
            Id = id;
        }

        public Guid Id { get; set; }
        public string FilePath { get; set; }
        public IRTextSectionLoader Loader { get; set; }
        public IRTextSummary Summary { get; set; }
        public bool IsDebugDocument { get; set; }

        public string FileName {
            get {
                try {
                    return Path.GetFileName(FilePath);
                }
                catch (Exception) {
                    return "";
                }
            }
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

        public void SaveSectionState(object stateObject, IRTextSection section) {
            SectionStates[section] = stateObject;
        }

        public object LoadSectionState(IRTextSection section) {
            return SectionStates.TryGetValue(section, out var stateObject) ? stateObject : null;
        }

        public void SetupDocumentWatcher() {
            try {
                string fileDir = Path.GetDirectoryName(FilePath);
                string fileName = Path.GetFileName(FilePath);
                documentWatcher_ = new FileSystemWatcher(fileDir, fileName);
                documentWatcher_.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                documentWatcher_.Changed += DocumentWatcher_Changed;
            }
            catch(Exception ex) {
                Trace.TraceError($"Failed to setup document watcher for file {FilePath}: {ex.Message}");
                documentWatcher_ = null;
            }
        }

        public LoadedDocumentState SerializeDocument() {
            var state = new LoadedDocumentState(Id);
            state.FilePath = FilePath;
            state.DocumentText = Loader.GetDocumentTextBytes();

            foreach (var sectionState in SectionStates) {
                state.SectionStates.Add(new Tuple<ulong, byte[]>(sectionState.Key.Id,
                                                                 sectionState.Value as byte[]));
            }

            foreach (var panelState in PanelStates) {
                foreach (var panelStatePair in panelState.Value) {
                    if (panelStatePair.Panel.SavesStateToFile) {
                        state.PanelStates.Add(new Tuple<ulong, PanelObjectPairState>(
                                                  panelState.Key.Id,
                                                  new PanelObjectPairState(panelStatePair.Panel.PanelKind,
                                                                           panelStatePair.StateObject)));
                    }
                }
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

            DocumentChanged?.Invoke(this, new EventArgs());
        }

        #region IDisposable Support

        private bool disposed_;

        protected virtual void Dispose(bool disposing) {
            if (!disposed_) {
                Loader.Dispose();
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
}
