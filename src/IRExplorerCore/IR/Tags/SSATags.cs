// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public interface ISSAValue {
        long DefinitionId { get; set; }
        OperandIR DefinitionOperand { get; }
        TupleIR DefinitionTuple { get; }

        InstructionIR DefinitionInstruction { get; }
    }

    public sealed class SSAUseTag : ITag, ISSAValue {
        public SSAUseTag(long definitionId, SSADefinitionTag definition) {
            DefinitionId = definitionId;
            Definition = definition;
        }

        public SSADefinitionTag Definition { get; set; }
        public long DefinitionId { get; set; }

        public OperandIR DefinitionOperand => Definition?.Owner as OperandIR;
        public TupleIR DefinitionTuple => DefinitionOperand.ParentTuple;
        public InstructionIR DefinitionInstruction => DefinitionTuple as InstructionIR;

        public string Name => "SSA use-definition link";
        public TaggedObject Owner { get; set; } // Source operand.
        public IRElement OwnerElement => (IRElement)Owner;
        public InstructionIR OwnerInstruction => OwnerElement.ParentInstruction;

        public override bool Equals(object obj) {
            return obj is SSAUseTag tag && DefinitionId == tag.DefinitionId;
        }

        public override int GetHashCode() {
            return HashCode.Combine(DefinitionId);
        }

        public override string ToString() {
            return "";
            //return $"SSA UD-link: {DefinitionId}";
        }
    }

    public sealed class SSADefinitionTag : ITag, ISSAValue {
        public List<SSAUseTag> Users { get; }

        public SSADefinitionTag(long defId) {
            DefinitionId = defId;
            Users = new List<SSAUseTag>();
        }

        public bool HasUsers => Users.Count > 0;
        public bool HasSingleUser => Users.Count == 1;
        public long DefinitionId { get; set; }
        public OperandIR DefinitionOperand => Owner as OperandIR;
        public TupleIR DefinitionTuple => ((IRElement)Owner).ParentTuple;
        public InstructionIR DefinitionInstruction => ((IRElement)Owner).ParentTuple as InstructionIR;
        public string Name => "SSA definition";
        public TaggedObject Owner { get; set; } // Destination operand.
        public IRElement OwnerElement => (IRElement)Owner;

        public override bool Equals(object obj) {
            return obj is SSADefinitionTag tag && DefinitionId == tag.DefinitionId;
        }

        public override int GetHashCode() {
            return HashCode.Combine(DefinitionId);
        }

        public override string ToString() {
            var builder = new StringBuilder();
            // builder.AppendLine($"SSA definition: {DefinitionId}");
            // builder.AppendLine($"  - parent: {((IRElement)Owner).Id}");
            // builder.AppendLine($"  - users: {Users.Count}");
            //
            // foreach (var user in Users) {
            //     builder.Append($"    - {((IRElement)user.Owner).Id}");
            // }

            return builder.ToString();
        }
    }
}