// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class GraphColors {
        public GraphColors() {
            //? TODO: Set default dark theme colors
            BackgroundColor = Utils.ColorFromString("#EFECE2");
            TextColor = Colors.Black;
            EdgeColor = Colors.Black;
            NodeColor = Utils.ColorFromString("#CBCBCB");
            NodeBorderColor = Utils.ColorFromString("#000000");
        }
        
        [ProtoMember(1)] public Color BackgroundColor { get; set; }
        [ProtoMember(2)] public Color TextColor { get; set; }
        [ProtoMember(3)] public Color NodeColor { get; set; }
        [ProtoMember(4)] public Color NodeBorderColor { get; set; }
        [ProtoMember(5)] public Color EdgeColor { get; set; }   
        
        public override bool Equals(object obj) {
            return obj is GraphColors options &&
                   TextColor.Equals(options.TextColor) &&
                   EdgeColor.Equals(options.EdgeColor) &&
                   NodeColor.Equals(options.NodeColor) &&
                   NodeBorderColor.Equals(options.NodeBorderColor) &&
                   BackgroundColor.Equals(options.BackgroundColor);
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

        public Color BackgroundColor {
            get => currentThemeColors_.BackgroundColor;
            set => currentThemeColors_.BackgroundColor = value;
        }

        public Color TextColor {
            get => currentThemeColors_.TextColor;
            set => currentThemeColors_.TextColor = value;
        }
        
        public Color NodeColor {
            get => currentThemeColors_.NodeColor;
            set => currentThemeColors_.NodeColor = value;
        }
        
        public Color NodeBorderColor {
            get => currentThemeColors_.NodeBorderColor;
            set => currentThemeColors_.NodeBorderColor = value;
        }
        
        public Color EdgeColor {
            get => currentThemeColors_.EdgeColor;
            set => currentThemeColors_.EdgeColor = value;
        }

        [ProtoMember(18)]
        private Dictionary<ApplicationThemeKind, GraphColors> themeColors_;
        private GraphColors currentThemeColors_;
        
        public override void Reset() {
            LoadThemeSettingsImpl();
            SyncSelectedNodes = true;
            SyncMarkedNodes = true;
            BringNodesIntoView = true;
            ShowPreviewPopup = true;
            ColorizeNodes = true;
            ColorizeEdges = true;
            HighlightConnectedNodesOnHover = true;
            HighlightConnectedNodesOnSelection = true;
        }

        protected virtual void LoadThemeSettingsImpl() {
            themeColors_ ??= new Dictionary<ApplicationThemeKind, GraphColors>();

            if (!themeColors_.TryGetValue(App.Theme.Kind, out var colors)) {
                colors = new GraphColors();
                themeColors_[App.Theme.Kind] = colors;
            }

            currentThemeColors_ = colors;
        }

        [ProtoAfterDeserialization]
        public void LoadThemeSettings() {
            LoadThemeSettingsImpl();
        }

        public override bool Equals(object obj) {
            return obj is GraphSettings options &&
                   Utils.AreEqual(themeColors_, options.themeColors_) &&
                   SyncSelectedNodes == options.SyncSelectedNodes &&
                   SyncMarkedNodes == options.SyncMarkedNodes &&
                   BringNodesIntoView == options.BringNodesIntoView &&
                   ShowPreviewPopup == options.ShowPreviewPopup &&
                   ShowPreviewPopupWithModifier == options.ShowPreviewPopupWithModifier &&
                   ColorizeNodes == options.ColorizeNodes &&
                   ColorizeEdges == options.ColorizeEdges &&
                   HighlightConnectedNodesOnHover == options.HighlightConnectedNodesOnHover &&
                   HighlightConnectedNodesOnSelection == options.HighlightConnectedNodesOnSelection;
        }
    }
}
