// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore.Utilities;
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
        BlockArgumentsMerge,
        Call,
        Return,
        Exception,
        Loop,
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
        public List<OperandIR> Sources { get; }
        public List<OperandIR> Destinations { get; }
        public List<RegionIR> NestedRegions { get; set; }

        public bool IsUnary => Kind == InstructionKind.Unary;
        public bool IsBinary => Kind == InstructionKind.Binary;
        public bool IsBranch => Kind == InstructionKind.Branch;
        public bool IsGoto => Kind == InstructionKind.Goto;
        public bool IsSwitch => Kind == InstructionKind.Switch;
        public bool IsCall => Kind == InstructionKind.Call;
        public bool IsReturn => Kind == InstructionKind.Return;
        public bool HasNestedRegions => NestedRegions != null && NestedRegions.Count > 0;

        public T OpcodeAs<T>() where T : Enum {
            return (T)Opcode;
        }

        public bool OpcodeIs<T>(T value) where T : Enum {
            return (Opcode != null) &&
                   (Opcode is T) &&
                   ((T)Opcode).Equals(value);
        }

        public override void Accept(IRVisitor visitor) {
            visitor.Visit(this);
        }

        public override string ToString() {
            var builder = new StringBuilder();
            builder.AppendLine($"instr kind: {Kind}, opcode: {OpcodeText} ({Opcode}), id: {Id}");

            for (int i = 0; i < Destinations.Count; i++) {
                builder.AppendLine($"o dest {i}: {Destinations[i]}".Indent(2));
            }

            for (int i = 0; i < Sources.Count; i++) {
                builder.AppendLine($"o src {i}: {Sources[i]}".Indent(2));
            }

            return builder.ToString();
        }
    }
}