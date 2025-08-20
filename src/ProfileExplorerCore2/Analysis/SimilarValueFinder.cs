// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using ProfileExplorerCore2.IR;

namespace ProfileExplorerCore2.Analysis;

public class SimilarValueFinder {
  private FunctionIR function_;
  private Dictionary<int, InstructionIR> ssaDefTable_;

  public SimilarValueFinder(FunctionIR function) {
    function_ = function;
    ssaDefTable_ = new Dictionary<int, InstructionIR>();
    BuildSSADefinitionTable();
  }

  public InstructionIR Find(InstructionIR instr) {
    if (instr.Destinations.Count == 0) {
      return null;
    }

    int? ssaDefId = ReferenceFinder.GetSSADefinitionId(instr.Destinations[0]);

    if (ssaDefId.HasValue &&
        ssaDefTable_.TryGetValue(ssaDefId.Value, out var similarInstr)) {
      return similarInstr;
    }

    return null;
  }

  private void BuildSSADefinitionTable() {
    // Precompute a table mapping each SSA definition ID to its definition.
    foreach (var block in function_.Blocks) {
      foreach (var tuple in block.Tuples) {
        if (tuple is InstructionIR instr && instr.Destinations.Count > 0) {
          int? ssaDefId =
            ReferenceFinder.GetSSADefinitionId(instr.Destinations[0]);

          if (ssaDefId.HasValue) {
            ssaDefTable_[ssaDefId.Value] = instr;
          }
        }
      }
    }
  }

  private bool IsSimilarInstruction(InstructionIR instr, InstructionIR otherInstr) {
    return instr.Opcode.Equals(otherInstr.Opcode) &&
           instr.Destinations.Count == otherInstr.Destinations.Count &&
           instr.Sources.Count == otherInstr.Sources.Count;
  }
}