// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using IRExplorerCore.Graph;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class ExpressionGraphColors {
        public ExpressionGraphColors() {
            //? TODO: Set default dark theme colors
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
        [ProtoMember(10)] public Color LoadStoreInstructionNodeColor { get; set; }
        [ProtoMember(11)] public Color CallInstructionNodeColor { get; set; }
        
        public override bool Equals(object obj) {
            return obj is ExpressionGraphColors options &&
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
                   CallInstructionNodeColor.Equals(options.CallInstructionNodeColor);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class ExpressionGraphSettings : GraphSettings {
        public ExpressionGraphSettings() {
            Reset();
        }

        public Color UnaryInstructionNodeColor {
            get => currentThemeColors_.UnaryInstructionNodeColor;
            set => currentThemeColors_.UnaryInstructionNodeColor = value;
        }
        
        public Color BinaryInstructionNodeColor {
            get => currentThemeColors_.BinaryInstructionNodeColor;
            set => currentThemeColors_.BinaryInstructionNodeColor = value;
        }
        
        public Color CopyInstructionNodeColor {
            get => currentThemeColors_.CopyInstructionNodeColor;
            set => currentThemeColors_.CopyInstructionNodeColor = value;
        }
        
        public Color PhiInstructionNodeColor {
            get => currentThemeColors_.PhiInstructionNodeColor;
            set => currentThemeColors_.PhiInstructionNodeColor = value;
        }
        
        public Color OperandNodeColor {
            get => currentThemeColors_.OperandNodeColor;
            set => currentThemeColors_.OperandNodeColor = value;
        }
        
        public Color NumberOperandNodeColor {
            get => currentThemeColors_.NumberOperandNodeColor;
            set => currentThemeColors_.NumberOperandNodeColor = value;
        }
        
        public Color IndirectionOperandNodeColor {
            get => currentThemeColors_.IndirectionOperandNodeColor;
            set => currentThemeColors_.IndirectionOperandNodeColor = value;
        }
        
        public Color AddressOperandNodeColor {
            get => currentThemeColors_.AddressOperandNodeColor;
            set => currentThemeColors_.AddressOperandNodeColor = value;
        }
        
        public Color LoopPhiBackedgeColor {
            get => currentThemeColors_.LoopPhiBackedgeColor;
            set => currentThemeColors_.LoopPhiBackedgeColor = value;
        }
        
        public Color LoadStoreInstructionNodeColor {
            get => currentThemeColors_.LoadStoreInstructionNodeColor;
            set => currentThemeColors_.LoadStoreInstructionNodeColor = value;
        }
        public Color CallInstructionNodeColor {
            get => currentThemeColors_.CallInstructionNodeColor;
            set => currentThemeColors_.CallInstructionNodeColor = value;
        }

        [ProtoMember(1)] public bool PrintVariableNames { get; set; }

        [ProtoMember(2)] public bool PrintSSANumbers { get; set; }

        [ProtoMember(3)] public bool GroupInstructions { get; set; }

        [ProtoMember(4)] public bool PrintBottomUp { get; set; }

        [ProtoMember(5)] public int MaxExpressionDepth { get; set; }

        [ProtoMember(6)] public bool SkipCopyInstructions { get; set; }
        
        [ProtoMember(7)]
        private Dictionary<ApplicationThemeKind, ExpressionGraphColors> themeColors_;
        private ExpressionGraphColors currentThemeColors_;
        
        protected override void LoadThemeSettingsImpl() {
            base.LoadThemeSettingsImpl();
            themeColors_ ??= new Dictionary<ApplicationThemeKind, ExpressionGraphColors>();

            if (!themeColors_.TryGetValue(App.Theme.Kind, out var colors)) {
                colors = new ExpressionGraphColors();
                themeColors_[App.Theme.Kind] = colors;
            }

            currentThemeColors_ = colors;
        }

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
            LoadThemeSettingsImpl();
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
                   Utils.AreEqual(themeColors_, options.themeColors_) &&
                   PrintVariableNames == options.PrintVariableNames &&
                   PrintSSANumbers == options.PrintSSANumbers &&
                   GroupInstructions == options.GroupInstructions &&
                   PrintBottomUp == options.PrintBottomUp &&
                   SkipCopyInstructions == options.SkipCopyInstructions &&
                   MaxExpressionDepth == options.MaxExpressionDepth;
        }
    }
}
