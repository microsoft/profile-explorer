// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using Core;
using ProtoBuf;

namespace Client {
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

        public LoadedDocumentState() {
            SectionStates = new List<Tuple<ulong, byte[]>>();
            PanelStates = new List<Tuple<ulong, PanelObjectPairState>>();
        }
    }

    public class LoadedDocument : IDisposable {
        private FileSystemWatcher documentWatcher_;
        private bool disposed_;

        public string FilePath { get; set; }
        public SectionLoader Loader { get; set; }
        public IRTextSummary Summary { get; set; }
        public Dictionary<IRTextSection, List<PanelObjectPair>> PanelStates;
        public Dictionary<IRTextSection, object> SectionStates;
        public bool IsDebugDocument;

        public string FileName {
            get {
                try {
                    return Path.GetFileName(FilePath);
                }
                catch (Exception ex) {
                    return "";
                }
            }
        }

        public event EventHandler DocumentChanged;

        public LoadedDocument(string filePath) {
            FilePath = filePath;
            PanelStates = new Dictionary<IRTextSection, List<PanelObjectPair>>();
            SectionStates = new Dictionary<IRTextSection, object>();
        }

        public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section) {

            if (!PanelStates.TryGetValue(section, out var list)) {
                list = new List<PanelObjectPair>();
                PanelStates.Add(section, list);
            }

            var state = list.Find((item) => item.Panel == panel);

            if (state != null) {
                state.StateObject = stateObject;
            }
            else {
                list.Add(new PanelObjectPair(panel, stateObject));
            }
        }

        public object LoadPanelState(IToolPanel panel, IRTextSection section) {
            if (PanelStates.TryGetValue(section, out var list)) {
                var state = list.Find((item) => item.Panel == panel);
                return state?.StateObject;
            }

            return null;
        }

        public void SaveSectionState(object stateObject, IRTextSection section) {
            SectionStates[section] = stateObject;
        }

        public object LoadSectionState(IRTextSection section) {
            if (SectionStates.TryGetValue(section, out var stateObject)) {
                return stateObject;
            }

            return null;
        }

        public void SetupDocumentWatcher() {
            var fileDir = Path.GetDirectoryName(FilePath);
            var fileName = Path.GetFileName(FilePath);
            documentWatcher_ = new FileSystemWatcher(fileDir, fileName);
            documentWatcher_.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.LastAccess;
            documentWatcher_.Changed += DocumentWatcher_Changed;
        }

        public LoadedDocumentState SerializeDocument() {
            var state = new LoadedDocumentState();
            state.FilePath = FilePath;
            state.DocumentText = Loader.GetDocumentText();

            foreach (var sectionState in SectionStates) {
                state.SectionStates.Add(new Tuple<ulong, byte[]>(sectionState.Key.Id,
                    sectionState.Value as byte[]));
            }

            foreach (var panelState in PanelStates) {
                foreach (var panelStatePair in panelState.Value) {
                    if (panelStatePair.Panel.SavesStateToFile) {
                        state.PanelStates.Add(new Tuple<ulong, PanelObjectPairState>
                            (panelState.Key.Id,
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

        protected virtual void Dispose(bool disposing) {
            if (!disposed_) {
                if (disposing) {
                    if (documentWatcher_ != null) {
                        documentWatcher_.Dispose();
                    }
                }

                disposed_ = true;
            }
        }

        public void Dispose() {
            Dispose(true);
        }
    }
}
