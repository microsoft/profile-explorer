// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace Core.IR {
    public interface ISSAValue {
        int DefinitionId { get; set; }
        OperandIR DefinitionOperand { get; }
        TupleIR DefinitionTuple { get; }

        InstructionIR DefinitionInstruction { get; }
    }

    public sealed class SSAUseTag : ITag, ISSAValue {
        public string Name { get => "SSA use-definition link"; }
        public IRElement Parent { get; set; }
        public int DefinitionId { get; set; }
        public SSADefinitionTag Definition { get; set; }

        public SSAUseTag(int definitionId, SSADefinitionTag definition) {
            DefinitionId = definitionId;
            Definition = definition;
        }

        public OperandIR DefinitionOperand => Definition?.Parent as OperandIR;
        public TupleIR DefinitionTuple => Definition?.Parent.ParentTuple as TupleIR;
        public InstructionIR DefinitionInstruction => Definition?.Parent.ParentTuple as InstructionIR;

        public override bool Equals(object obj) {
            return obj is SSAUseTag tag &&
                   DefinitionId == tag.DefinitionId;
        }

        public override int GetHashCode() {
            return HashCode.Combine(DefinitionId);
        }

        public override string ToString() {
            return $"SSA UD-link: {DefinitionId}, {Definition.Parent}";
        }
    }

    public sealed class SSADefinitionTag : ITag, ISSAValue {
        public string Name { get => "SSA definition"; }
        public IRElement Parent { get; set; }
        public int DefinitionId { get; set; }
        public List<SSAUseTag> Users;

        public SSADefinitionTag(int defId) {
            DefinitionId = defId;
            Users = new List<SSAUseTag>();
        }

        public bool HasUsers => Users.Count > 0;
        public OperandIR DefinitionOperand => Parent as OperandIR;
        public TupleIR DefinitionTuple => Parent.ParentTuple as TupleIR;
        public InstructionIR DefinitionInstruction => Parent.ParentTuple as InstructionIR;

        public override bool Equals(object obj) {
            return obj is SSADefinitionTag tag &&
                   DefinitionId == tag.DefinitionId;
        }

        public override int GetHashCode() {
            return HashCode.Combine(DefinitionId);
        }

        public override string ToString() {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"SSA definition: {DefinitionId}");
            builder.AppendLine($"  - parent: {Parent}");
            builder.AppendLine($"  - users: {Users.Count}");

            foreach (var user in Users) {
                builder.AppendLine($"    - {user.Parent}");

                if (user.Parent is TupleIR tuple) {
                    var tupleString = tuple.ToString();
                    tupleString = tupleString.Replace("\n", "\n        ");
                    builder.AppendLine($"      {tupleString}");
                }
                else if (user.Parent is OperandIR operand) {
                    var tupleString = operand.Parent.ToString();
                    tupleString = tupleString.Replace("\n", "\n        ");
                    builder.AppendLine($"      {tupleString}");
                }
            }

            return builder.ToString();
        }
    }
}
