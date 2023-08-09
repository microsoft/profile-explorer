// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IRExplorerCore.IR {
    public class IRPrinter {
        private StringBuilder builder_;
        private FunctionIR function_;
        private Dictionary<long, long> ssaValueIdMap_;
        private int indentLevel_;

        public IRPrinter(FunctionIR function) {
            function_ = function;
            indentLevel_ = 0;
            ssaValueIdMap_ = new Dictionary<long, long>();
        }

        public string Print() {
            builder_ = new StringBuilder();
            PrintFunctionStart();

            if (function_.RootRegion != null) {
                PrintRegion(function_.RootRegion);
            }
            else {
                foreach (var block in function_.Blocks) {
                    PrintBlock(block);
                }
            }

            PrintFunctionEnd();
            return builder_.ToString();
        }

        private void AppendNI(string text = "") {
            builder_.Append(text);
        }

        private void Append(string text = "") {
            AppendIndentation();
            builder_.Append(text);
        }

        private void AppendLine(string text = "") {
            AppendIndentation();
            builder_.AppendLine(text);
        }

        private void AppendIndentation() {
            for(int i = 0; i < indentLevel_; i++) {
                builder_.Append("  ");
            }
        }

        private void PrintFunctionStart() {
            Append($"func {function_.Name}(");
            bool needsComma = false;

            foreach (var param in function_.Parameters) {
                if (needsComma) {
                    Append(", ");
                }
                else {
                    needsComma = true;
                }

                PrintOperand(param, false, true);
            }

            AppendNI($") -> {function_.ReturnType}:\n");
        }

        private void PrintFunctionEnd() {
            AppendLine("// function end");
        }

        private void PrintBlock(BlockIR block) {
            indentLevel_++;
            Append($"block {{#{block.IndexInFunction}, B{block.Number}}}, ");
            AppendNI($"  loc: {block.TextLocation}, len: {block.TextLength} ");

            AppendNI(" P");
            block.Predecessors.ForEach((p) => AppendNI($" {p.Number}"));
            AppendNI(", S");
            block.Successors.ForEach((p) => AppendNI($" {p.Number}"));

            if (block.BlockArguments != null) {
                AppendNI((", Args: "));
                block.BlockArguments.ForEach((arg) => AppendNI($" {arg.Id}, offset {arg.TextLocation.Offset} |"));
            }

            AppendNI("{\n");
            indentLevel_++;

            foreach (var tuple in block.Tuples) {
                PrintTuple(tuple);
                AppendLine();
            }

            indentLevel_--;
            AppendLine("}");
            indentLevel_--;
        }

        private void PrintTuple(TupleIR tuple) {
            switch (tuple.Kind) {
                case TupleKind.Instruction: {
                    PrintInstruction((InstructionIR)tuple);
                    break;
                }
                case TupleKind.Label: {
                    Append($"  label {tuple.Name}:");
                    break;
                }
                case TupleKind.Exception: {
                    Append("exception");
                    break;
                }
                case TupleKind.Metadata: {
                    Append("metadata");
                    break;
                }
                case TupleKind.Other: {
                    Append("other");
                    break;
                }
            }

            AppendNI($"  | #{tuple.IndexInBlock}, B{tuple.ParentBlock.Number}");
            AppendNI($"  | {tuple.TextLocation}, len: {tuple.TextLength}");

            if(tuple is InstructionIR instr && instr.HasNestedRegions) {
                AppendNI(" {");
                foreach (var region in instr.NestedRegions) {
                    AppendLine();
                    PrintRegion(region);
                }
                Append("} // regions end");
            }
        }

        private void PrintRegion(RegionIR region) {
            indentLevel_++;
            Append($"region {region.Id} {{");
            AppendNI($"  loc: {region.TextLocation}, len: {region.TextLength} ");
            AppendLine();

            foreach (var block in region.Blocks) {
                PrintBlock(block);
            }

            AppendLine("}");
            indentLevel_--;
        }

        private void PrintInstruction(InstructionIR instr) {
            bool isFirstOp = true;

            foreach (var destOp in instr.Destinations) {
                if (!isFirstOp) {
                    AppendNI(", ");
                }

                PrintOperand(destOp, isFirstOp, true);
                isFirstOp = false;
            }

            if (instr.Destinations.Count > 0) {
                AppendNI(" = ");
                AppendNI($"{instr.OpcodeText} ");
            }
            else {
                Append($"{instr.OpcodeText} ");
            }

            isFirstOp = true;

            foreach (var sourceOp in instr.Sources) {
                if (!isFirstOp) {
                    AppendNI(", ");
                }

                PrintOperand(sourceOp, false, false);
                isFirstOp = false;
            }
        }

        private long GetSSAValueId(long defId) {
            if(!ssaValueIdMap_.TryGetValue(defId, out long ssaId)) {
                ssaId = ssaValueIdMap_.Count + 1;
                ssaValueIdMap_.Add(defId, ssaId);
            }

            return ssaId;
        }

        private void PrintOperand(OperandIR op, bool isFirstOp, bool printKind = false) {
            string result = op.Kind switch             {
                OperandKind.Variable => printKind
                    ? $"var {op.NameValue}.{op.Type}"
                    : $"{op.NameValue}.{op.Type}",
                OperandKind.Temporary => printKind
                    ? $"temp {op.NameValue}.{op.Type}"
                    : $"{op.NameValue}.{op.Type}",
                OperandKind.IntConstant => printKind
                    ? $"intconst {op.IntValue}.{op.Type}"
                    : $"{op.IntValue}.{op.Type}",
                OperandKind.FloatConstant => printKind
                    ? $"floatconst {op.FloatValue}.{op.Type}"
                    : $"{op.FloatValue}.{op.Type}",
                OperandKind.Indirection => printKind
                    ? $"indir [{op.Value}.{op.Type}]"
                    : $"[{op.Value}.{op.Type}]",
                OperandKind.Address => printKind
                    ? $"address &{op.NameValue}.{op.Type}"
                    : $"&{op.NameValue}.{op.Type}",
                OperandKind.LabelAddress => printKind
                    ? string.Format("label &{0}.{1}", op.NameValue)
                    : $"&{op.NameValue}",
                OperandKind.Other => "other",
                _ => ""
            };

            var ssaTag = op.GetTag<ISSAValue>();

            if (ssaTag != null) {
                result += $"<{GetSSAValueId(ssaTag.DefinitionId)}>";
            }

            if (isFirstOp) {
                Append(result);
            }
            else {
                AppendNI(result);
            }
        }
    }
}