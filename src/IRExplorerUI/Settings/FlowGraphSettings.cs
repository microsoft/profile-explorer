// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    //? THEME ID -> ColorPalette
    //? after deserialize - foreach theme, set default?
    
    [ProtoContract(SkipConstructor = true)]
    public class ThemeColors {
        [ProtoContract]
        public class TthemeColorSet : Dictionary<string, Color> {
            public Guid Id { get; set; }

            public TthemeColorSet(Guid id) {
                Id = id;
            }
        }

        [ProtoMember(1)]
        private Dictionary<Guid, TthemeColorSet> colorSets_;
        [ProtoMember(2)]
        private Dictionary<string, Color> defaultColorValues_;

        public ThemeColors DefaultTheme { get; set; }
        
        public ThemeColors() {
            InitializeReferenceMembers();
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            colorSets_ ??= new Dictionary<Guid, TthemeColorSet>();
            defaultColorValues_ ??= new Dictionary<string, Color>();
        }

        public void AddColorSet(TthemeColorSet tthemeColorSet) {
            colorSets_[tthemeColorSet.Id] = tthemeColorSet;
        }
        
        public void SetColor(Guid id, string valueName, Color color) {
            var colorSet = GetOrCreateColorSet(id);
            colorSet[valueName] = color;
        }
        
        public bool HasCustomColor(Guid id, string valueName) {
            return colorSets_.TryGetValue(id, out var colorSet) &&
                   colorSet.ContainsKey(valueName);
        }
        
        public void SetDefaultColor(string valueName, Color color) {
            defaultColorValues_[valueName] = color;
        }

        public Color GetColor(Guid id, string valueName) {
            if (colorSets_.TryGetValue(id, out var colorSet) &&
                colorSet.TryGetValue(valueName, out var color)) {
                return color;
            }

            if (DefaultTheme != null) {
                return DefaultTheme.GetColor(id, valueName);
            }
            else if (defaultColorValues_.TryGetValue(valueName, out var defaultColor)) {
                return defaultColor;
            } 

            return Colors.Transparent;
        }
        
        private TthemeColorSet GetOrCreateColorSet(Guid id) {
            if(!colorSets_.TryGetValue(id, out var colorSet)) {
                colorSet = new TthemeColorSet(id);
                colorSets_[id] = colorSet;
            }

            return colorSet;
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class FlowGraphSettings : GraphSettings {
        public FlowGraphSettings() {
            Reset();
        }

        static readonly Guid ID = new Guid("5C60F693-BEF5-E011-A485-80EE7300C695");

        public static ThemeColors.TthemeColorSet CreateDefaultThemeColors(ApplicationThemeKind themeKind) {
            var theme = new ThemeColors.TthemeColorSet(ID) {
                {nameof(EmptyNodeColor), Utils.ColorFromString("#F4F4F4")},
                {nameof(BranchNodeBorderColor), Utils.ColorFromString("#0042B6")},
                {nameof(SwitchNodeBorderColor), Utils.ColorFromString("#8500BE")},
                {nameof(LoopNodeBorderColor), Utils.ColorFromString("#008D00")},
                {nameof(ReturnNodeBorderColor), Utils.ColorFromString("#B30606")},
                {nameof(DominatorEdgeColor), Utils.ColorFromString("#0042B6")},
            };

            return theme;
        }
        
        public Color EmptyNodeColor {
            get => theme_.GetColor(ID, nameof(EmptyNodeColor));
            set => theme_.SetColor(ID, nameof(EmptyNodeColor), value);
        }
        
        public Color BranchNodeBorderColor {
            get => theme_.GetColor(ID, nameof(BranchNodeBorderColor));
            set => theme_.SetColor(ID, nameof(BranchNodeBorderColor), value);
        }
        
        public Color SwitchNodeBorderColor {
            get => theme_.GetColor(ID, nameof(SwitchNodeBorderColor));
            set => theme_.SetColor(ID, nameof(SwitchNodeBorderColor), value);
        }
        
        public Color LoopNodeBorderColor {
            get => theme_.GetColor(ID, nameof(LoopNodeBorderColor));
            set => theme_.SetColor(ID, nameof(LoopNodeBorderColor), value);
        }
        
        public Color ReturnNodeBorderColor {
            get => theme_.GetColor(ID, nameof(ReturnNodeBorderColor));
            set => theme_.SetColor(ID, nameof(ReturnNodeBorderColor), value);
        }
        
        public Color DominatorEdgeColor {
            get => theme_.GetColor(ID, nameof(DominatorEdgeColor));
            set => theme_.SetColor(ID, nameof(DominatorEdgeColor), value);
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

        private ThemeColors theme_;

        public void SwitchTheme(ThemeColors theme) {
            theme_ = theme;
        }
        
        public override void Reset() {
            base.Reset();
            LoadThemeSettingsImpl();
            MarkLoopBlocks = true;
            ShowImmDominatorEdges = true;
        }

        protected override void LoadThemeSettingsImpl() {
            base.LoadThemeSettingsImpl();
            
        }
        
        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            var deserialized = StateSerializer.Deserialize<FlowGraphSettings>(serialized);
            deserialized.theme_ = theme_;
            return deserialized;
        }

        public override bool Equals(object obj) {
            return obj is FlowGraphSettings options &&
                   base.Equals(obj) &&
                   //Utils.AreEqual(themeColors_, options.themeColors_) &&
                   MarkLoopBlocks == options.MarkLoopBlocks &&
                   ShowImmDominatorEdges == options.ShowImmDominatorEdges;
        }
    }
}
