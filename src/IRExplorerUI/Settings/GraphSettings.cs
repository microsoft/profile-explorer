// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    [ProtoInclude(100, typeof(FlowGraphSettings))]
    [ProtoInclude(200, typeof(ExpressionGraphSettings))]
    public class GraphSettings : SettingsBase {
        private ThemeColorSet theme_;
        
        public GraphSettings() {
            Reset();
        }

        public static ThemeColorSet CreateDefaultThemeColors(ApplicationThemeKind themeKind) {
            var theme = new ThemeColorSet(Guid.Empty) {
                {nameof(BackgroundColor), Utils.ColorFromString("#F4F4F4")},
                {nameof(TextColor), Colors.Black},
                {nameof(EdgeColor), Colors.Black},
                {nameof(NodeColor), Utils.ColorFromString("#CBCBCB")},
                {nameof(NodeBorderColor), Colors.Black},
            };
            
            return theme;
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
            get => theme_[nameof(BackgroundColor)];
            set => theme_[nameof(BackgroundColor)] = value;
        }

        public Color TextColor {
            get => theme_[nameof(TextColor)];
            set => theme_[nameof(TextColor)] = value;
        }

        public Color NodeColor {
            get => theme_[nameof(NodeColor)];
            set => theme_[nameof(NodeColor)] = value;
        }

        public Color NodeBorderColor {
            get => theme_[nameof(NodeBorderColor)];
            set => theme_[nameof(NodeBorderColor)] = value;
        }

        public Color EdgeColor {
            get => theme_[nameof(EdgeColor)];
            set => theme_[nameof(EdgeColor)] = value;
        }

        public override void Reset() {
            SyncSelectedNodes = true;
            SyncMarkedNodes = true;
            BringNodesIntoView = true;
            ShowPreviewPopup = true;
            ColorizeNodes = true;
            ColorizeEdges = true;
            HighlightConnectedNodesOnHover = true;
            HighlightConnectedNodesOnSelection = true;
        }

        public override bool Equals(object obj) {
            return obj is GraphSettings options &&
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
