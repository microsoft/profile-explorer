// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using CoreLib.GraphViz;
using ProtoBuf;

namespace Client {
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
    public class DocumentSettings : SettingsBase, INotifyPropertyChanged {
        public DocumentSettings() {
            Reset();
        }

        [ProtoMember(1)] public bool ShowBlockSeparatorLine { get; set; }

        [ProtoMember(2)] public string FontName { get; set; }

        [ProtoMember(3)] public double FontSize { get; set; }

        [ProtoMember(4)] public bool HighlightCurrentLine { get; set; }

        [ProtoMember(5)] public bool ShowBlockFolding { get; set; }

        [ProtoMember(6)] public bool HighlightSourceDefinition { get; set; }

        [ProtoMember(7)] public bool HighlightDestinationUses { get; set; }

        [ProtoMember(8)] public bool HighlightInstructionOperands { get; set; }

        [ProtoMember(9)] public bool ShowInfoOnHover { get; set; }

        [ProtoMember(10)] public bool ShowInfoOnHoverWithModifier { get; set; }

        [ProtoMember(11)] public bool ShowPreviewPopup { get; set; }

        [ProtoMember(12)] public bool ShowPreviewPopupWithModifier { get; set; }

        [ProtoMember(13)] public Color BackgroundColor { get; set; }

        [ProtoMember(14)] public Color AlternateBackgroundColor { get; set; }

        [ProtoMember(15)] public Color MarginBackgroundColor { get; set; }

        [ProtoMember(16)] public Color TextColor { get; set; }

        [ProtoMember(17)] public Color BlockSeparatorColor { get; set; }

        [ProtoMember(18)] public Color SelectedValueColor { get; set; }

        [ProtoMember(19)] public Color DefinitionValueColor { get; set; }

        [ProtoMember(20)] public Color UseValueColor { get; set; }

        [ProtoMember(21)] public Color BorderColor { get; set; }

        [ProtoMember(22)] public string SyntaxHighlightingFilePath { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public override void Reset() {
            ShowBlockSeparatorLine = true;
            FontName = "Consolas";
            FontSize = 12;
            HighlightCurrentLine = true;
            ShowBlockFolding = true;
            HighlightSourceDefinition = true;
            HighlightDestinationUses = true;
            HighlightInstructionOperands = true;
            ShowInfoOnHover = true;
            ShowInfoOnHoverWithModifier = true;
            ShowPreviewPopup = true;
            ShowPreviewPopupWithModifier = false;
            SyntaxHighlightingFilePath = "";
            BackgroundColor = Utils.ColorFromString("#FFFAFA");
            AlternateBackgroundColor = Utils.ColorFromString("#f5f5f5");
            TextColor = Colors.Black;
            BlockSeparatorColor = Colors.Silver;
            MarginBackgroundColor = Colors.Gainsboro;
            SelectedValueColor = Utils.ColorFromString("#C5DEEA");
            DefinitionValueColor = Utils.ColorFromString("#F2E67C");
            UseValueColor = Utils.ColorFromString("#95DBAD");
            BorderColor = Colors.Black;
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<DocumentSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is DocumentSettings settings &&
                   ShowBlockSeparatorLine == settings.ShowBlockSeparatorLine &&
                   FontName == settings.FontName &&
                   Math.Abs(FontSize - settings.FontSize) < double.Epsilon &&
                   HighlightCurrentLine == settings.HighlightCurrentLine &&
                   ShowBlockFolding == settings.ShowBlockFolding &&
                   HighlightSourceDefinition == settings.HighlightSourceDefinition &&
                   HighlightDestinationUses == settings.HighlightDestinationUses &&
                   HighlightInstructionOperands == settings.HighlightInstructionOperands &&
                   ShowInfoOnHover == settings.ShowInfoOnHover &&
                   ShowInfoOnHoverWithModifier == settings.ShowInfoOnHoverWithModifier &&
                   ShowPreviewPopup == settings.ShowPreviewPopup &&
                   ShowPreviewPopupWithModifier == settings.ShowPreviewPopupWithModifier &&
                   BackgroundColor.Equals(settings.BackgroundColor) &&
                   AlternateBackgroundColor.Equals(settings.AlternateBackgroundColor) &&
                   MarginBackgroundColor.Equals(settings.MarginBackgroundColor) &&
                   TextColor.Equals(settings.TextColor) &&
                   BlockSeparatorColor.Equals(settings.BlockSeparatorColor) &&
                   SelectedValueColor.Equals(settings.SelectedValueColor) &&
                   DefinitionValueColor.Equals(settings.DefinitionValueColor) &&
                   UseValueColor.Equals(settings.UseValueColor) &&
                   BorderColor.Equals(settings.BorderColor) &&
                   SyntaxHighlightingFilePath == settings.SyntaxHighlightingFilePath;
        }
    }

    [ProtoContract(SkipConstructor = true)]
    [ProtoInclude(100, typeof(FlowGraphSettings))]
    [ProtoInclude(200, typeof(ExpressionGraphSettings))]
    public class GraphSettings : SettingsBase {
        public GraphSettings() {
            Reset();
        }

        [ProtoMember(1)] public bool SyncSelectedNodes { get; set; }

        [ProtoMember(2)] public bool SyncMarkedNodes { get; set; }

        [ProtoMember(3)] public bool BringNodesIntoView { get; set; }

        [ProtoMember(4)] public bool ShowPreviewPopup { get; set; }

        [ProtoMember(5)] public bool ShowPreviewPopupWithModifier { get; set; }

        [ProtoMember(6)] public bool ColorizeNodes { get; set; }

        [ProtoMember(7)] public bool ColorizeEdges { get; set; }

        [ProtoMember(8)] public bool HighlightConnectedNodesOnHover { get; set; }

        [ProtoMember(9)] public bool HighlightConnectedNodesOnSelection { get; set; }

        [ProtoMember(10)] public Color BackgroundColor { get; set; }

        [ProtoMember(11)] public Color TextColor { get; set; }

        [ProtoMember(12)] public Color NodeColor { get; set; }

        [ProtoMember(13)] public Color NodeBorderColor { get; set; }

        [ProtoMember(14)] public Color EdgeColor { get; set; }

        public override void Reset() {
            SyncSelectedNodes = true;
            SyncMarkedNodes = true;
            BringNodesIntoView = true;
            ShowPreviewPopup = true;
            ColorizeNodes = true;
            ColorizeEdges = true;
            HighlightConnectedNodesOnHover = true;
            HighlightConnectedNodesOnSelection = true;
            BackgroundColor = Utils.ColorFromString("#EFECE2");
            TextColor = Colors.Black;
            EdgeColor = Colors.Black;
            NodeColor = Utils.ColorFromString("#CBCBCB");
            NodeBorderColor = Utils.ColorFromString("#000000");
        }

        public override bool Equals(object obj) {
            return obj is GraphSettings options &&
                   TextColor.Equals(options.TextColor) &&
                   EdgeColor.Equals(options.EdgeColor) &&
                   NodeColor.Equals(options.NodeColor) &&
                   NodeBorderColor.Equals(options.NodeBorderColor) &&
                   SyncSelectedNodes == options.SyncSelectedNodes &&
                   SyncMarkedNodes == options.SyncMarkedNodes &&
                   BringNodesIntoView == options.BringNodesIntoView &&
                   ShowPreviewPopup == options.ShowPreviewPopup &&
                   ShowPreviewPopupWithModifier == options.ShowPreviewPopupWithModifier &&
                   ColorizeNodes == options.ColorizeNodes &&
                   ColorizeEdges == options.ColorizeEdges &&
                   HighlightConnectedNodesOnHover == options.HighlightConnectedNodesOnHover &&
                   HighlightConnectedNodesOnSelection == options.HighlightConnectedNodesOnSelection &&
                   BackgroundColor.Equals(options.BackgroundColor);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class FlowGraphSettings : GraphSettings {
        public FlowGraphSettings() {
            Reset();
        }

        [ProtoMember(1)] public Color EmptyNodeColor { get; set; }

        [ProtoMember(2)] public Color BranchNodeBorderColor { get; set; }

        [ProtoMember(3)] public Color SwitchNodeBorderColor { get; set; }

        [ProtoMember(4)] public Color LoopNodeBorderColor { get; set; }

        [ProtoMember(5)] public Color ReturnNodeBorderColor { get; set; }

        [ProtoMember(6)] public bool MarkLoopBlocks { get; set; }

        [ProtoMember(7, OverwriteList = true)] public Color[] LoopNodeColors { get; set; }

        [ProtoMember(8)] public bool ShowImmDominatorEdges { get; set; }

        [ProtoMember(9)] public Color DominatorEdgeColor { get; set; }

        public override void Reset() {
            base.Reset();
            TextColor = Colors.Black;
            NodeColor = Utils.ColorFromString("#CBCBCB");
            NodeBorderColor = Utils.ColorFromString("#000000");
            EmptyNodeColor = Utils.ColorFromString("#F4F4F4");
            BranchNodeBorderColor = Utils.ColorFromString("#0042B6");
            SwitchNodeBorderColor = Utils.ColorFromString("#8500BE");
            LoopNodeBorderColor = Utils.ColorFromString("#008D00");
            ReturnNodeBorderColor = Utils.ColorFromString("#B30606");
            MarkLoopBlocks = true;

            LoopNodeColors = new Color[] {
                Utils.ColorFromString("#FCD1A4"),
                Utils.ColorFromString("#FFA56D"),
                Utils.ColorFromString("#FF7554"),
                Utils.ColorFromString("#FC5B5B")
            };

            ShowImmDominatorEdges = true;
            DominatorEdgeColor = Utils.ColorFromString("#0042B6");
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<FlowGraphSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is FlowGraphSettings options &&
                   base.Equals(obj) &&
                   EmptyNodeColor.Equals(options.EmptyNodeColor) &&
                   BranchNodeBorderColor.Equals(options.BranchNodeBorderColor) &&
                   SwitchNodeBorderColor.Equals(options.SwitchNodeBorderColor) &&
                   LoopNodeBorderColor.Equals(options.LoopNodeBorderColor) &&
                   ReturnNodeBorderColor.Equals(options.ReturnNodeBorderColor) &&
                   MarkLoopBlocks == options.MarkLoopBlocks &&
                   ShowImmDominatorEdges == options.ShowImmDominatorEdges &&
                   DominatorEdgeColor.Equals(options.DominatorEdgeColor) &&
                   EqualityComparer<Color[]>.Default.Equals(LoopNodeColors, options.LoopNodeColors);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class ExpressionGraphSettings : GraphSettings {
        static ExpressionGraphSettings() {
           // StateSerializer.RegisterDerivedClass<ExpressionGraphSettings, GraphSettings>();
        }

        public ExpressionGraphSettings() {
            Reset();
        }

        [ProtoMember(1)] public Color UnaryInstructionNodeColor { get; set; }

        [ProtoMember(2)] public Color BinaryInstructionNodeColor { get; set; }

        [ProtoMember(3)] public Color CopyInstructionNodeColor { get; set; }

        [ProtoMember(4)] public Color PhiInstructionNodeColor { get; set; }

        [ProtoMember(5)] public Color OperandNodeColor { get; set; }

        [ProtoMember(6)] public Color NumberOperandNodeColor { get; set; }

        [ProtoMember(7)] public Color IndirectionOperandNodeColor { get; set; }

        [ProtoMember(8)] public Color AddressOperandNodeColor { get; set; }

        [ProtoMember(9)] public Color LoopPhiBackedgeColor { get; set; }

        [ProtoMember(10)] public bool PrintVariableNames { get; set; }

        [ProtoMember(11)] public bool PrintSSANumbers { get; set; }

        [ProtoMember(12)] public bool GroupInstructions { get; set; }

        [ProtoMember(13)] public bool PrintBottomUp { get; set; }

        [ProtoMember(14)] public int MaxExpressionDepth { get; set; }

        [ProtoMember(15)] public bool SkipCopyInstructions { get; set; }
        
        [ProtoMember(16)] public Color LoadStoreInstructionNodeColor { get; set; }

        [ProtoMember(17)] public Color CallInstructionNodeColor { get; set; }


        public ExpressionGraphPrinterOptions GetGraphPrinterOptions() {
            return new ExpressionGraphPrinterOptions {
                PrintVariableNames = PrintVariableNames,
                PrintSSANumbers = PrintSSANumbers,
                GroupInstructions = GroupInstructions,
                PrintBottomUp = PrintBottomUp,
                SkipCopyInstructions = SkipCopyInstructions,
                MaxExpressionDepth = MaxExpressionDepth
            };
        }

        public override void Reset() {
            base.Reset();
            UnaryInstructionNodeColor = Utils.ColorFromString("#FFFACD");
            BinaryInstructionNodeColor = Utils.ColorFromString("#FFE4C4");
            CopyInstructionNodeColor = Utils.ColorFromString("#F5F5F5");
            PhiInstructionNodeColor = Utils.ColorFromString("#B6E8DE");
            OperandNodeColor = Utils.ColorFromString("#D3F8D5");
            NumberOperandNodeColor = Utils.ColorFromString("#c6def1");
            IndirectionOperandNodeColor = Utils.ColorFromString("#b8bedd");
            AddressOperandNodeColor = Utils.ColorFromString("#D8BFD8");
            LoopPhiBackedgeColor = Utils.ColorFromString("#178D1F");
            LoadStoreInstructionNodeColor = Utils.ColorFromString("#FFCAD1");
            CallInstructionNodeColor = Utils.ColorFromString("#F0E68C");
            PrintVariableNames = true;
            PrintSSANumbers = true;
            GroupInstructions = true;
            PrintBottomUp = false;
            SkipCopyInstructions = false;
            MaxExpressionDepth = 8;
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<ExpressionGraphSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is ExpressionGraphSettings options &&
                   base.Equals(obj) &&
                   UnaryInstructionNodeColor.Equals(options.UnaryInstructionNodeColor) &&
                   BinaryInstructionNodeColor.Equals(options.BinaryInstructionNodeColor) &&
                   CopyInstructionNodeColor.Equals(options.CopyInstructionNodeColor) &&
                   PhiInstructionNodeColor.Equals(options.PhiInstructionNodeColor) &&
                   OperandNodeColor.Equals(options.OperandNodeColor) &&
                   NumberOperandNodeColor.Equals(options.NumberOperandNodeColor) &&
                   IndirectionOperandNodeColor.Equals(options.IndirectionOperandNodeColor) &&
                   AddressOperandNodeColor.Equals(options.AddressOperandNodeColor) &&
                   LoopPhiBackedgeColor.Equals(options.LoopPhiBackedgeColor) &&
                   LoadStoreInstructionNodeColor.Equals(options.LoadStoreInstructionNodeColor) &&
                   CallInstructionNodeColor.Equals(options.CallInstructionNodeColor) &&
                   PrintVariableNames == options.PrintVariableNames &&
                   PrintSSANumbers == options.PrintSSANumbers &&
                   GroupInstructions == options.GroupInstructions &&
                   PrintBottomUp == options.PrintBottomUp &&
                   SkipCopyInstructions == options.SkipCopyInstructions &&
                   MaxExpressionDepth == options.MaxExpressionDepth;
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

        public ApplicationSettings() {
            Reset();
        }

        public void Reset() {
            RecentFiles = new List<string>();
            RecentComparedFiles = new List<Tuple<string, string>>();
            AutoReloadDocument = true;
            ThemeIndex = 2; // Blue theme.
            DocumentSettings = new DocumentSettings();
            FlowGraphSettings = new FlowGraphSettings();
            ExpressionGraphSettings = new ExpressionGraphSettings();
            RemarkSettings = new RemarkSettings();

            DocumentSettings.Reset();
            FlowGraphSettings.Reset();
            ExpressionGraphSettings.Reset();
            RemarkSettings.Reset();
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
