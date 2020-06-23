// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Text;

namespace CoreLib.IR {
    public class IRPrinter {
        private StringBuilder builder_;
        private FunctionIR function_;

        public IRPrinter(FunctionIR function) {
            function_ = function;
        }

        public string Print() {
            builder_ = new StringBuilder();
            PrintFunctionStart();

            foreach (var block in function_.Blocks) {
                PrintBlock(block);
            }

            PrintFunctionEnd();
            return builder_.ToString();
        }

        private void PrintFunctionStart() {
            builder_.Append($"func {function_.Name}(");
            bool needsComma = false;

            foreach (var param in function_.Parameters) {
                if (needsComma) {
                    builder_.Append(", ");
                }
                else {
                    needsComma = true;
                }

                PrintOperand(param, true);
            }

            builder_.AppendLine($") -> {function_.ReturnType}:");
        }

        private void PrintFunctionEnd() {
            builder_.AppendLine("// function end");
        }

        private void PrintBlock(BlockIR block) {
            builder_.Append($"block {block.Number}:");
            builder_.AppendLine();

            foreach (var tuple in block.Tuples) {
                PrintTuple(tuple);
                builder_.AppendLine();
            }
        }

        private void PrintTuple(TupleIR tuple) {
            if (tuple.Kind != TupleKind.Label) {
                builder_.Append("    ");
            }

            switch (tuple.Kind) {
                case TupleKind.Instruction: {
                    PrintInstruction((InstructionIR) tuple);
                    break;
                }
                case TupleKind.Label: {
                    builder_.Append($"  label {((BlockLabelIR) tuple).Name}:");
                    break;
                }
                case TupleKind.Exception: {
                    builder_.Append("exception");
                    break;
                }
                case TupleKind.Metadata: {
                    builder_.Append("metadata");
                    break;
                }
                case TupleKind.Other: {
                    builder_.Append("other");
                    break;
                }
            }
        }

        private void PrintInstruction(InstructionIR instr) {
            bool needsComma = false;

            foreach (var destOp in instr.Destinations) {
                if (needsComma) {
                    builder_.Append(", ");
                }
                else {
                    needsComma = true;
                }

                PrintOperand(destOp, true);
            }

            if (instr.Destinations.Count > 0) {
                builder_.Append(" = ");
            }

            if (instr.IsBranch) {
                builder_.Append("branch ");
            }
            else if (instr.IsGoto) {
                builder_.Append("goto ");
            }
            else if (instr.IsSwitch) {
                builder_.Append("switch ");
            }
            else if (instr.IsReturn) {
                builder_.Append("return ");
            }

            builder_.Append($"{instr.OpcodeText} ");
            needsComma = false;

            foreach (var sourceOp in instr.Sources) {
                if (needsComma) {
                    builder_.Append(", ");
                }
                else {
                    needsComma = true;
                }

                PrintOperand(sourceOp);
            }
        }

        private void PrintOperand(OperandIR op, bool printKind = false) {
            string result = op.Kind switch {
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
                _                 => ""
            };

            var ssaTag = op.GetTag<ISSAValue>();

            if (ssaTag != null) {
                result += $"<{ssaTag.DefinitionId}>";
            }

            builder_.Append(result);
        }
    }
}
