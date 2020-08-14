// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public interface ISSAValue {
        int DefinitionId { get; set; }
        OperandIR DefinitionOperand { get; }
        TupleIR DefinitionTuple { get; }

        InstructionIR DefinitionInstruction { get; }
    }

    public sealed class SSAUseTag : ITag, ISSAValue {
        public SSAUseTag(int definitionId, SSADefinitionTag definition) {
            DefinitionId = definitionId;
            Definition = definition;
        }

        public SSADefinitionTag Definition { get; set; }
        public int DefinitionId { get; set; }

        public OperandIR DefinitionOperand => Definition?.Owner as OperandIR;
        public TupleIR DefinitionTuple => Definition?.Owner.ParentTuple;

        public InstructionIR DefinitionInstruction =>
            Definition?.Owner.ParentTuple as InstructionIR;

        public string Name => "SSA use-definition link";
        public IRElement Owner { get; set; } // Source operand.
        public InstructionIR OwnerInstruction => Owner.ParentInstruction;

        public override bool Equals(object obj) {
            return obj is SSAUseTag tag && DefinitionId == tag.DefinitionId;
        }

        public override int GetHashCode() {
            return HashCode.Combine(DefinitionId);
        }

        public override string ToString() {
            return $"SSA UD-link: {DefinitionId}, {Definition.Owner}";
        }
    }

    public sealed class SSADefinitionTag : ITag, ISSAValue {
        public List<SSAUseTag> Users;

        public SSADefinitionTag(int defId) {
            DefinitionId = defId;
            Users = new List<SSAUseTag>();
        }

        public bool HasUsers => Users.Count > 0;
        public bool HasSingleUser => Users.Count == 1;
        public int DefinitionId { get; set; }
        public OperandIR DefinitionOperand => Owner as OperandIR;
        public TupleIR DefinitionTuple => Owner.ParentTuple;
        public InstructionIR DefinitionInstruction => Owner.ParentTuple as InstructionIR;
        public string Name => "SSA definition";
        public IRElement Owner { get; set; } // Destination operand.

        public override bool Equals(object obj) {
            return obj is SSADefinitionTag tag && DefinitionId == tag.DefinitionId;
        }

        public override int GetHashCode() {
            return HashCode.Combine(DefinitionId);
        }

        public override string ToString() {
            var builder = new StringBuilder();
            builder.AppendLine($"SSA definition: {DefinitionId}");
            builder.AppendLine($"  - parent: {Owner}");
            builder.AppendLine($"  - users: {Users.Count}");

            foreach (var user in Users) {
                builder.AppendLine($"    - {user.Owner}");

                if (user.Owner is TupleIR tuple) {
                    string tupleString = tuple.ToString();
                    tupleString = tupleString.Replace("\n", "\n        ");
                    builder.AppendLine($"      {tupleString}");
                }
                else if (user.Owner is OperandIR operand) {
                    string tupleString = operand.Parent.ToString();
                    tupleString = tupleString.Replace("\n", "\n        ");
                    builder.AppendLine($"      {tupleString}");
                }
            }

            return builder.ToString();
        }
    }
}
