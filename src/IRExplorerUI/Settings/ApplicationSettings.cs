// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
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
        public List<Tuple<string, string, string>> RecentProfileFiles;

        [ProtoMember(15)]
        public string DefaultCompilerIR;

        [ProtoMember(16)]
        public IRMode DefaultIRMode;

        [ProtoMember(17)]
        public Dictionary<BinaryFileKind, ExternalDisassemblerOptions> ExternalDisassemblerOptions;

        [ProtoMember(18)]
        public ProfileDataProviderOptions ProfileOptions;

        [ProtoMember(19)]
        public SymbolFileSourceOptions SymbolOptions { get; set; }

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
            RecentTextSearches ??= new List<string>();
            RecentComparedFiles ??= new List<Tuple<string, string>>();
            RecentProfileFiles ??= new List<Tuple<string, string, string>>();
            DocumentSettings ??= new DocumentSettings();
            FlowGraphSettings ??= new FlowGraphSettings();
            ExpressionGraphSettings ??= new ExpressionGraphSettings();
            RemarkSettings ??= new RemarkSettings();
            DiffSettings ??= new DiffSettings();
            SectionSettings ??= new SectionSettings();
            FunctionTaskOptions ??= new Dictionary<Guid, byte[]>();
            ExternalDisassemblerOptions ??= new Dictionary<BinaryFileKind, ExternalDisassemblerOptions>();
            ProfileOptions ??= new ProfileDataProviderOptions();
            SymbolOptions ??= new SymbolFileSourceOptions();
        }

        public ExternalDisassemblerOptions GetExternalDisassemblerOptions(BinaryFileKind fileKind) {
            if (ExternalDisassemblerOptions.TryGetValue(fileKind, out var options)) {
                return options;
            }

            options = new ExternalDisassemblerOptions(fileKind);
            ExternalDisassemblerOptions[fileKind] = options;
            return options;
        }

        public bool IsExternalDisassemblerEnabled(BinaryFileKind fileKind) {
            return GetExternalDisassemblerOptions(fileKind).IsEnabled;
        }

        public bool IsExternalDisassemblerEnabled() {
            foreach (var options in ExternalDisassemblerOptions.Values) {
                if (options.IsEnabled) return true;
            }

            return false;
        }

        public void AddRecentFile(string path) {
            // Keep at most N recent files, and move this one on the top of the list.
            // Search as case-insensitive so that C:\file and c:\file are considered the same.
            int index = RecentFiles.FindIndex(file => file.Equals(path,
                StringComparison.InvariantCultureIgnoreCase));

            if (index != -1) {
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

        public void AddRecentProfileFiles(string profilePath, string binaryPath, string debugPath) {
            // Keep at most N recent files, and move this one on the top of the list.
            var pair = new Tuple<string, string, string>(profilePath, binaryPath, debugPath);

            if (RecentProfileFiles.Contains(pair)) {
                RecentProfileFiles.Remove(pair);
            }
            else if (RecentProfileFiles.Count >= 10) {
                RecentProfileFiles.RemoveAt(RecentProfileFiles.Count - 1);
            }

            RecentProfileFiles.Insert(0, pair);
        }

        public void ClearRecentProfileFiles() {
            RecentProfileFiles.Clear();
        }

        public void SaveFunctionTaskOptions(FunctionTaskInfo taskInfo, byte[] data) {
            FunctionTaskOptions[taskInfo.Id] = data;
        }

        public byte[] LoadFunctionTaskOptions(FunctionTaskInfo taskInfo) {
            if (FunctionTaskOptions.TryGetValue(taskInfo.Id, out var data)) {
                return data;
            }

            return null;
        }

        public void SwitchDefaultCompilerIR(string irName, IRMode irMode) {
            DefaultCompilerIR = irName;
            DefaultIRMode = irMode;
        }

        public void CompilerIRSwitched(string irName, IRMode irMode) {
            //? TODO: Hack to get the default IR style picked when the IR changes
            //? Should remember a last {ir -> ir style name} and restore based on that 
            DocumentSettings.SyntaxHighlightingName = null;
            App.ReloadSyntaxHighlightingFiles(irName);
        }
    }
}
