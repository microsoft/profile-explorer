// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace Core.IR {
    public class IRPrinter {
        FunctionIR function_;
        StringBuilder builder_;

        public IRPrinter(FunctionIR function) {
            function_ = function;
        }

        public string Print() {
            builder_ = new StringBuilder();

            PrintFunctionStart();

            foreach(var block in function_.Blocks) {
                PrintBlock(block);
            }

            PrintFunctionEnd();
            return builder_.ToString();
        }

        void PrintFunctionStart() {
            builder_.Append($"func {function_.Name}(");
            bool needsComma = false;

            foreach (var param in function_.Parameters) {
                if (needsComma) {
                    builder_.Append(", ");
                }
                else needsComma = true;

                PrintOperand(param, printKind: true);
            }

            builder_.AppendLine($") -> {function_.ReturnType}:");
        }

        void PrintFunctionEnd() {
            builder_.AppendLine("// function end");
        }

        void PrintBlock(BlockIR block) {
            builder_.Append($"block {block.Number}:");
            builder_.AppendLine();

            foreach(var tuple in block.Tuples) {
                PrintTuple(tuple);
                builder_.AppendLine();
            }
        }

        void PrintTuple(TupleIR tuple) {
            if(tuple.Kind != TupleKind.Label) {
                builder_.Append("    "); ;
            }

            switch (tuple.Kind) {
                case TupleKind.Instruction: {
                    PrintInstruction((InstructionIR)tuple);
                    break;
                }
                case TupleKind.Label: {
                    builder_.Append($"  label {((BlockLabelIR)tuple).Name}:");
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

        void PrintInstruction(InstructionIR instr) {
            bool needsComma = false;

            foreach (var destOp in instr.Destinations) {
                if (needsComma) {
                    builder_.Append(", ");
                }
                else needsComma = true;

                PrintOperand(destOp, printKind: true);
            }

            if(instr.Destinations.Count > 0) {
                builder_.Append(" = ");
            }

            if(instr.IsBranch) {
                builder_.Append("branch ");
            }
            else if(instr.IsGoto) {
                builder_.Append("goto ");
            }
            else if(instr.IsSwitch) {
                builder_.Append("switch ");
            }
            else if(instr.IsReturn) {
                builder_.Append("return ");
            }

            builder_.Append($"{instr.OpcodeText} ");
            needsComma = false;

            foreach (var sourceOp in instr.Sources) {
                if (needsComma) {
                    builder_.Append(", ");
                }
                else needsComma = true;

                PrintOperand(sourceOp);
            }
        }

        void PrintOperand(OperandIR op, bool printKind = false) {
            string result = "";

            switch (op.Kind) {
                case OperandKind.Variable:
                result = printKind ? string.Format("var {0}.{1}", op.NameValue, op.Type) :
                                     string.Format("{0}.{1}", op.NameValue, op.Type);
                break;
                case OperandKind.Temporary:
                result = printKind ? string.Format("temp {0}.{1}", op.NameValue, op.Type) :
                                     string.Format("{0}.{1}", op.NameValue, op.Type); ;
                break;
                case OperandKind.IntConstant:
                result = printKind ? string.Format("intconst {0}.{1}", op.IntValue, op.Type) :
                                     string.Format("{0}.{1}", op.IntValue, op.Type);
                break;
                case OperandKind.FloatConstant:
                result = printKind ? string.Format("floatconst {0}.{1}", op.FloatValue, op.Type) :
                                     string.Format("{0}.{1}", op.FloatValue, op.Type);
                break;
                case OperandKind.Indirection:
                result = printKind ? string.Format("indir [{0}.{1}]", op.Value, op.Type) :
                                     string.Format("[{0}.{1}]", op.Value, op.Type);
                break;
                case OperandKind.Address:
                result = printKind ? string.Format("address &{0}.{1}", op.NameValue, op.Type) :
                                     string.Format("&{0}.{1}", op.NameValue, op.Type);
                break;
                case OperandKind.LabelAddress:
                result = printKind ? string.Format("label &{0}.{1}", op.NameValue) :
                                     string.Format("&{0}", op.NameValue);
                break;
                case OperandKind.Other:
                result = "other";
                break;
            }

            var ssaTag = op.GetTag<ISSAValue>();

            if (ssaTag != null) {
                result += $"<{ssaTag.DefinitionId}>";
            }

            builder_.Append(result);
        }
    }
}
