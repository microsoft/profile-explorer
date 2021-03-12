// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class FlowGraphSettings : GraphSettings {
        public FlowGraphSettings() {
            Reset();
        }

        private static readonly Guid Id = new Guid("5C60F693-BEF5-E011-A485-80EE7300C695");
        private ThemeColorSet theme_;

        public static ThemeColorSet CreateDefaultThemeColors(ApplicationThemeKind themeKind) {
            var theme = new ThemeColorSet(Id) {
                {nameof(EmptyNodeColor), Utils.ColorFromString("#F4F4F4")},
                {nameof(BranchNodeBorderColor), Utils.ColorFromString("#0042B6")},
                {nameof(SwitchNodeBorderColor), Utils.ColorFromString("#8500BE")},
                {nameof(LoopNodeBorderColor), Utils.ColorFromString("#008D00")},
                {nameof(ReturnNodeBorderColor), Utils.ColorFromString("#B30606")},
                {nameof(DominatorEdgeColor), Utils.ColorFromString("#0042B6")},
            };

            var baseTheme = GraphSettings.CreateDefaultThemeColors(themeKind);
            return theme.CombineWith(baseTheme);
        }
        
        public Color EmptyNodeColor {
            get => theme_[nameof(EmptyNodeColor)];
            set => theme_[nameof(EmptyNodeColor)] = value;
        }
        
        public Color BranchNodeBorderColor {
            get => theme_[nameof(BranchNodeBorderColor)];        
            set => theme_[nameof(BranchNodeBorderColor)] = value;
        }

        public Color SwitchNodeBorderColor {
            get => theme_[nameof(SwitchNodeBorderColor)];        
            set => theme_[nameof(SwitchNodeBorderColor)] = value;
        }

        public Color LoopNodeBorderColor {
            get => theme_[nameof(LoopNodeBorderColor)];
            set => theme_[nameof(LoopNodeBorderColor)] = value;  
        }

        public Color ReturnNodeBorderColor {
            get => theme_[nameof(ReturnNodeBorderColor)];
            set => theme_[nameof(ReturnNodeBorderColor)] = value;
        }

        public Color DominatorEdgeColor {
            get => theme_[nameof(DominatorEdgeColor)];
            set => theme_[nameof(DominatorEdgeColor)] = value;
        }
        
        //public Color[] LoopNodeColors {
        //    get => theme_.GetColor(ID, nameof(LoopNodeColors));
        //    set => theme_.SetColor(ID, nameof(LoopNodeColors), value);
        //}
        
        public Color[] LoopNodeColors {
            get => new Color[] {Colors.Aquamarine};
            set {
                
            }
        }
        
        [ProtoMember(1)] public bool MarkLoopBlocks { get; set; }
        [ProtoMember(2)] public bool ShowImmDominatorEdges { get; set; }

        public void SwitchTheme(ThemeColorSet theme) {
            theme_ = theme;
        }
        
        public override void Reset() {
            base.Reset();
            App.ResetSettingsTheme(Id);
            MarkLoopBlocks = true;
            ShowImmDominatorEdges = true;
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            var deserialized = StateSerializer.Deserialize<FlowGraphSettings>(serialized);
            deserialized.theme_ = theme_.Clone();
            return deserialized;
        }

        public override bool Equals(object obj) {
            return obj is FlowGraphSettings options &&
                   base.Equals(obj) &&
                   theme_.Equals(options.theme_) &&
                   MarkLoopBlocks == options.MarkLoopBlocks &&
                   ShowImmDominatorEdges == options.ShowImmDominatorEdges;
        }
    }
}
