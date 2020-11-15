// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis {
    public class SimilarValueFinder {
        private FunctionIR function_;
        private Dictionary<int, InstructionIR> ssaDefTable_;

        public SimilarValueFinder(FunctionIR function) {
            function_ = function;
            ssaDefTable_ = new Dictionary<int, InstructionIR>();
            BuildSSADefinitionTable();
        }

        private void BuildSSADefinitionTable() {
            // Precompute a table mapping each SSA definition ID to its definition.
            foreach (var block in function_.Blocks) {
                foreach (var tuple in block.Tuples) {
                    if (tuple is InstructionIR instr && instr.Destinations.Count > 0) {
                        var ssaDefId =
                            ReferenceFinder.GetSSADefinitionId(instr.Destinations[0]);

                        if (ssaDefId.HasValue) {
                            ssaDefTable_[ssaDefId.Value] = instr;
                        }
                    }
                }
            }
        }

        public InstructionIR Find(InstructionIR instr) {
            if (instr.Destinations.Count == 0) {
                return null;
            }

            var ssaDefId = ReferenceFinder.GetSSADefinitionId(instr.Destinations[0]);

            if (ssaDefId.HasValue &&
                ssaDefTable_.TryGetValue(ssaDefId.Value, out var similarInstr)) {
                return similarInstr;
            }

            return null;
        }

        private bool IsSimilarInstruction(InstructionIR instr, InstructionIR otherInstr) {
            return instr.Opcode.Equals(otherInstr.Opcode) &&
                   instr.Destinations.Count == otherInstr.Destinations.Count &&
                   instr.Sources.Count == otherInstr.Sources.Count;
        }
    }
}
