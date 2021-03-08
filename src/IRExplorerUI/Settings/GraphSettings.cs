// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    public class GraphColors {
        
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
}
