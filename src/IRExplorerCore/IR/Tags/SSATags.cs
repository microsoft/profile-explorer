// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR;

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
  public IRElement OwnerElement => (IRElement)Owner;
  public InstructionIR OwnerInstruction => OwnerElement.ParentInstruction;
  public int DefinitionId { get; set; }
  public OperandIR DefinitionOperand => Definition?.Owner as OperandIR;
  public TupleIR DefinitionTuple => DefinitionOperand.ParentTuple;
  public InstructionIR DefinitionInstruction => DefinitionTuple as InstructionIR;
  public string Name => "SSA use-definition link";
  public TaggedObject Owner { get; set; } // Source operand.

  public override bool Equals(object obj) {
    return obj is SSAUseTag tag && DefinitionId == tag.DefinitionId;
  }

  public override int GetHashCode() {
    return HashCode.Combine(DefinitionId);
  }

  public override string ToString() {
    return $"SSA UD-link: {DefinitionId}";
  }
}

public sealed class SSADefinitionTag : ITag, ISSAValue {
  public SSADefinitionTag(int defId) {
    DefinitionId = defId;
    Users = new List<SSAUseTag>();
  }

  public List<SSAUseTag> Users { get; }
  public bool HasUsers => Users.Count > 0;
  public bool HasSingleUser => Users.Count == 1;
  public IRElement OwnerElement => (IRElement)Owner;
  public int DefinitionId { get; set; }
  public OperandIR DefinitionOperand => Owner as OperandIR;
  public TupleIR DefinitionTuple => ((IRElement)Owner).ParentTuple;
  public InstructionIR DefinitionInstruction => ((IRElement)Owner).ParentTuple as InstructionIR;
  public string Name => "SSA definition";
  public TaggedObject Owner { get; set; } // Destination operand.

  public override bool Equals(object obj) {
    return obj is SSADefinitionTag tag && DefinitionId == tag.DefinitionId;
  }

  public override int GetHashCode() {
    return HashCode.Combine(DefinitionId);
  }

  public override string ToString() {
    var builder = new StringBuilder();
    builder.AppendLine($"SSA definition: {DefinitionId}");
    builder.AppendLine($"  - parent: {((IRElement)Owner).Id}");
    builder.AppendLine($"  - users: {Users.Count}");

    foreach (var user in Users) {
      builder.AppendLine($"    - {((IRElement)user.Owner).Id}");
    }

    return builder.ToString();
  }
}
