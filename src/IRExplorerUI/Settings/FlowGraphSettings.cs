// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class FlowGraphColors {
        public FlowGraphColors() {
            //? TODO: Set default dark theme colors
            EmptyNodeColor = Utils.ColorFromString("#F4F4F4");
            BranchNodeBorderColor = Utils.ColorFromString("#0042B6");
            SwitchNodeBorderColor = Utils.ColorFromString("#8500BE");
            LoopNodeBorderColor = Utils.ColorFromString("#008D00");
            ReturnNodeBorderColor = Utils.ColorFromString("#B30606");
            DominatorEdgeColor = Utils.ColorFromString("#0042B6");

            LoopNodeColors = new Color[] {
                Utils.ColorFromString("#FCD1A4"),
                Utils.ColorFromString("#FFA56D"),
                Utils.ColorFromString("#FF7554"),
                Utils.ColorFromString("#FC5B5B")
            };
        }
        
        [ProtoMember(1)] public Color EmptyNodeColor { get; set; }
        [ProtoMember(2)] public Color BranchNodeBorderColor { get; set; }
        [ProtoMember(3)] public Color SwitchNodeBorderColor { get; set; }
        [ProtoMember(4)] public Color LoopNodeBorderColor { get; set; }
        [ProtoMember(5)] public Color ReturnNodeBorderColor { get; set; }
        [ProtoMember(6)] public Color DominatorEdgeColor { get; set; }
        [ProtoMember(7, OverwriteList = true)] public Color[] LoopNodeColors { get; set; }
        
        public override bool Equals(object obj) {
            return obj is FlowGraphColors options &&
                   base.Equals(obj) &&
                   EmptyNodeColor.Equals(options.EmptyNodeColor) &&
                   BranchNodeBorderColor.Equals(options.BranchNodeBorderColor) &&
                   SwitchNodeBorderColor.Equals(options.SwitchNodeBorderColor) &&
                   LoopNodeBorderColor.Equals(options.LoopNodeBorderColor) &&
                   ReturnNodeBorderColor.Equals(options.ReturnNodeBorderColor) &&
                   DominatorEdgeColor.Equals(options.DominatorEdgeColor) &&
                   EqualityComparer<Color[]>.Default.Equals(LoopNodeColors, options.LoopNodeColors);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class FlowGraphSettings : GraphSettings {
        public FlowGraphSettings() {
            Reset();
        }

        public Color EmptyNodeColor {
            get => currentThemeColors_.EmptyNodeColor;
            set => currentThemeColors_.EmptyNodeColor = value;
        }
        
        public Color BranchNodeBorderColor {
            get => currentThemeColors_.BranchNodeBorderColor;
            set => currentThemeColors_.BranchNodeBorderColor = value;
        }
        
        public Color SwitchNodeBorderColor {
            get => currentThemeColors_.SwitchNodeBorderColor;
            set => currentThemeColors_.SwitchNodeBorderColor = value;
        }
        
        public Color LoopNodeBorderColor {
            get => currentThemeColors_.LoopNodeBorderColor;
            set => currentThemeColors_.LoopNodeBorderColor = value;
        }
        
        public Color ReturnNodeBorderColor {
            get => currentThemeColors_.ReturnNodeBorderColor;
            set => currentThemeColors_.ReturnNodeBorderColor = value;
        }
        
        public Color DominatorEdgeColor {
            get => currentThemeColors_.DominatorEdgeColor;
            set => currentThemeColors_.DominatorEdgeColor = value;
        }
        
        public Color[] LoopNodeColors {
            get => currentThemeColors_.LoopNodeColors;
            set => currentThemeColors_.LoopNodeColors = value;
        }
        
        [ProtoMember(1)] public bool MarkLoopBlocks { get; set; }

        [ProtoMember(2)] public bool ShowImmDominatorEdges { get; set; }

        [ProtoMember(3)]
        private Dictionary<ApplicationThemeKind, FlowGraphColors> themeColors_;
        private FlowGraphColors currentThemeColors_;
        
        public override void Reset() {
            base.Reset();
            LoadThemeSettingsImpl();
            MarkLoopBlocks = true;
            ShowImmDominatorEdges = true;
        }

        protected override void LoadThemeSettingsImpl() {
            base.LoadThemeSettingsImpl();
            themeColors_ ??= new Dictionary<ApplicationThemeKind, FlowGraphColors>();

            if (!themeColors_.TryGetValue(App.Theme.Kind, out var colors)) {
                colors = new FlowGraphColors();
                themeColors_[App.Theme.Kind] = colors;
            }

            currentThemeColors_ = colors;
        }
        
        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<FlowGraphSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is FlowGraphSettings options &&
                   base.Equals(obj) &&
                   Utils.AreEqual(themeColors_, options.themeColors_) &&
                   MarkLoopBlocks == options.MarkLoopBlocks &&
                   ShowImmDominatorEdges == options.ShowImmDominatorEdges;
        }
    }
}
