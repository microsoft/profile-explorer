// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows.Media;
using IRExplorerCore.Graph;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class ExpressionGraphSettings : GraphSettings {
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

        public ExpressionGraphSettings Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<ExpressionGraphSettings>(serialized);
        }
        
        protected override GraphSettings MakeClone() {
            return Clone();
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
}
