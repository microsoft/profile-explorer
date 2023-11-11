// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
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

        public FlowGraphSettings Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<FlowGraphSettings>(serialized);
        }

        protected override GraphSettings MakeClone() {
            return Clone();
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
}
