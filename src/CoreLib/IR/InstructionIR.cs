// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public enum InstructionKind {
        Unary,
        Binary,
        Branch,
        Goto,
        Switch,
        Phi,
        Call,
        Return,
        Exception,
        Other
    }

    public sealed class InstructionIR : TupleIR {
        public InstructionIR(IRElementId elementId, InstructionKind kind, BlockIR parent) :
            base(elementId, TupleKind.Instruction, parent) {
            Kind = kind;
            Sources = new List<OperandIR>(1); // Usually 1 destination.
            Destinations = new List<OperandIR>(2); // Usually at most 2 sources.
        }

        public new InstructionKind Kind { get; set; }
        public object Opcode { get; set; }
        public ReadOnlyMemory<char> OpcodeText { get; set; }
        public TextLocation OpcodeLocation { get; set; }
        public List<OperandIR> Sources { get; set; }
        public List<OperandIR> Destinations { get; set; }

        public bool IsUnary => Kind == InstructionKind.Unary;
        public bool IsBinary => Kind == InstructionKind.Binary;
        public bool IsBranch => Kind == InstructionKind.Branch;
        public bool IsGoto => Kind == InstructionKind.Goto;
        public bool IsSwitch => Kind == InstructionKind.Switch;
        public bool IsCall => Kind == InstructionKind.Call;
        public bool IsReturn => Kind == InstructionKind.Return;

        public T OpcodeAs<T>() where T : Enum {
            return (T) Opcode;
        }

        public bool OpcodeIs<T>(T value) where T : Enum {
            return Opcode != null && ((T) Opcode).Equals(value);
        }

        public override void Accept(IRVisitor visitor) {
            visitor.Visit(this);
        }

        public override bool Equals(object obj) {
            return obj is InstructionIR instruction &&
                   base.Equals(obj) &&
                   Kind == instruction.Kind &&
                   Opcode == instruction.Opcode &&
                   EqualityComparer<List<OperandIR>>.Default.Equals(
                       Sources, instruction.Sources) &&
                   EqualityComparer<List<OperandIR>>.Default.Equals(Destinations,
                                                                    instruction.Destinations);
        }

        public override int GetHashCode() {
            int hashCode = -493299099;
            hashCode = hashCode * -1521134295 + base.GetHashCode();
            hashCode = hashCode * -1521134295 + Kind.GetHashCode();
            hashCode = hashCode * -1521134295 + (Opcode?.GetHashCode() ?? 0);

            hashCode = hashCode * -1521134295 +
                       EqualityComparer<List<OperandIR>>.Default.GetHashCode(Sources);

            hashCode = hashCode * -1521134295 +
                       EqualityComparer<List<OperandIR>>.Default.GetHashCode(Destinations);

            return hashCode;
        }

        public override string ToString() {
            var result = new StringBuilder();

            result.AppendFormat("  > instr kind: {0}, {1} ({2}), (id: {3})\n", Kind,
                                OpcodeText, Opcode, Id);

            for (int i = 0; i < Destinations.Count; i++) {
                result.AppendFormat("    - dest {0}: {1}\n", i, Destinations[i]);
            }

            for (int i = 0; i < Sources.Count; i++) {
                result.AppendFormat("    - source {0}: {1}\n", i, Sources[i]);
            }

            return result.ToString();
        }
    }
}
