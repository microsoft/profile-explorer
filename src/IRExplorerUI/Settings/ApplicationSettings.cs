// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerUI.Query;
using ProtoBuf;

namespace IRExplorerUI {
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

        [ProtoMember(12)]
        public List<string> RecentTextSearches;

        [ProtoMember(13)]
        public Dictionary<Guid, byte[]> FunctionTaskOptions;

        [ProtoMember(14)]
        public Dictionary<ToolPanelKind, LightDocumentSettings> LightDocumentSettings;

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

        public void LoadThemeSettings() {
            //? TODO: Reload settings for other panels

            foreach (var pair in LightDocumentSettings) {
                pair.Value.LoadThemeSettings();
            }
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            RecentFiles ??= new List<string>();
            RecentTextSearches ??= new List<string>();
            RecentComparedFiles ??= new List<Tuple<string, string>>();
            DocumentSettings ??= new DocumentSettings();
            FlowGraphSettings ??= new FlowGraphSettings();
            ExpressionGraphSettings ??= new ExpressionGraphSettings();
            RemarkSettings ??= new RemarkSettings();
            DiffSettings ??= new DiffSettings();
            SectionSettings ??= new SectionSettings();
            FunctionTaskOptions ??= new Dictionary<Guid, byte[]>();
            LightDocumentSettings ??= new Dictionary<ToolPanelKind, LightDocumentSettings>();

            //? REMOVE
            /// if(string.IsNullOrEmpty(DocumentSettings.SyntaxHighlightingName)) {
            //DocumentSettings.SyntaxHighlightingName = "UTC IR";
            //}
        }

        public void AddRecentFile(string path) {
            // Keep at most N recent files, and move this one on the top of the list.
            // Search as case-insensitive so that C:\file and c:\file are considered the same.
            int index = RecentFiles.FindIndex(file => file.Equals(path, 
                StringComparison.InvariantCultureIgnoreCase));

            if(index != -1) {
                RecentFiles.RemoveAt(index);
            }
            else if (RecentFiles.Count >= 10) {
                RecentFiles.RemoveAt(RecentFiles.Count - 1);
            }

            RecentFiles.Insert(0, path);
        }

        public void ClearRecentFiles() {
            RecentFiles.Clear();
        }

        public void AddRecentTextSearch(string text) {
            //? TODO: Use some weights (number of times used) to sort the list
            //? and make it less likely to evict some often-used term
            // Keep at most N recent files, and move this one on the top of the list.
            if (RecentTextSearches.Contains(text)) {
                RecentTextSearches.Remove(text);
            }
            else if (RecentTextSearches.Count >= 20) {
                RecentTextSearches.RemoveAt(RecentTextSearches.Count - 1);
            }

            RecentTextSearches.Insert(0, text);
        }

        public void ClearRecentTextSearches() {
            RecentTextSearches.Clear();
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

            // Also add both files to the recent file list.
            AddRecentFile(basePath);
            AddRecentFile(diffPath);
        }

        public void ClearRecentComparedFiles() {
            RecentComparedFiles.Clear();
        }

        public void SaveFunctionTaskOptions(FunctionTaskInfo taskInfo, byte[] data) {
            FunctionTaskOptions[taskInfo.Id] = data;
        }

        public byte[] LoadFunctionTaskOptions(FunctionTaskInfo taskInfo) {
            if(FunctionTaskOptions.TryGetValue(taskInfo.Id, out var data)) {
                return data;
            }

            return null;
        }

        public LightDocumentSettings LoadLightDocumentSettings(ToolPanelKind panelKind) {
            LightDocumentSettings settings;

            if (!LightDocumentSettings.TryGetValue(panelKind, out settings)) {
                settings = new LightDocumentSettings();
                LightDocumentSettings[panelKind] = settings;
            }

            if(settings.SyncStyleWithDocument) {
                settings = (LightDocumentSettings)settings.Clone();
                settings.BackgroundColor = DocumentSettings.BackgroundColor;
                settings.TextColor = DocumentSettings.TextColor;
                settings.SearchResultColor = DocumentSettings.SearchResultColor;
            }

            return settings;
        }

        public void SaveLightDocumentSettings(ToolPanelKind panelKind, LightDocumentSettings settings) {
            LightDocumentSettings[panelKind] = settings;
        }
    }
}
