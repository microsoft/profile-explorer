// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using ProtoBuf;

namespace IRExplorer {
    public class SettingsBase {
        public virtual void Reset() { }

        public virtual SettingsBase Clone() {
            throw new NotImplementedException();
        }

        public virtual bool HasChanges(SettingsBase other) {
            return !other.Equals(this);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class ApplicationSettings {
        [ProtoMember(1)]
        public List<string> RecentFiles;

        [ProtoMember(2)]
        public bool AutoReloadDocument;
        
        [ProtoMember(3)]
        public string MainWindowPlacement;
        
        [ProtoMember(4)]
        public int ThemeIndex;
        
        [ProtoMember(5)]
        public List<Tuple<string, string>> RecentComparedFiles;
        
        [ProtoMember(6)]
        public DocumentSettings DocumentSettings;
        
        [ProtoMember(7)]
        public FlowGraphSettings FlowGraphSettings;
        
        [ProtoMember(8)]
        public ExpressionGraphSettings ExpressionGraphSettings;
        
        [ProtoMember(9)]
        public RemarkSettings RemarkSettings;

        [ProtoMember(10)]
        public DiffSettings DiffSettings;

        [ProtoMember(11)]
        public SectionSettings SectionSettings;

        public ApplicationSettings() {
            Reset();
        }

        public void Reset() {
            InitializeReferenceMembers();

            DocumentSettings.Reset();
            FlowGraphSettings.Reset();
            ExpressionGraphSettings.Reset();
            RemarkSettings.Reset();
            DiffSettings.Reset();
            SectionSettings.Reset();
            AutoReloadDocument = true;
            ThemeIndex = 2; // Blue theme.
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            RecentFiles ??= new List<string>();
            RecentComparedFiles ??= new List<Tuple<string, string>>();
            DocumentSettings ??= new DocumentSettings();
            FlowGraphSettings ??= new FlowGraphSettings();
            ExpressionGraphSettings ??= new ExpressionGraphSettings();
            RemarkSettings ??= new RemarkSettings();
            DiffSettings ??= new DiffSettings();
            SectionSettings ??= new SectionSettings();
        }

        public void AddRecentFile(string path) {
            // Keep at most N recent files, and move this one on the top of the list.
            if (RecentFiles.Contains(path)) {
                RecentFiles.Remove(path);
            }
            else if (RecentFiles.Count >= 10) {
                RecentFiles.RemoveAt(RecentFiles.Count - 1);
            }

            RecentFiles.Insert(0, path);
        }

        public void ClearRecentFiles() {
            RecentFiles.Clear();
        }

        public void AddRecentComparedFiles(string basePath, string diffPath) {
            // Keep at most N recent files, and move this one on the top of the list.
            var pair = new Tuple<string, string>(basePath, diffPath);

            if (RecentComparedFiles.Contains(pair)) {
                RecentComparedFiles.Remove(pair);
            }
            else if (RecentComparedFiles.Count >= 10) {
                RecentComparedFiles.RemoveAt(RecentComparedFiles.Count - 1);
            }

            RecentComparedFiles.Insert(0, pair);
        }

        public void ClearRecentComparedFiles() {
            RecentComparedFiles.Clear();
        }
    }
}
