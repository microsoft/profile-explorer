// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public sealed class BlockIR : IRElement {
        public BlockIR(IRElementId elementId, int number, FunctionIR parent) :
            base(elementId) {
            Number = number;
            Parent = parent;
            Tuples = new List<TupleIR>();
            Successors = new List<BlockIR>();
            Predecessors = new List<BlockIR>();
        }

        public int Number { get; set; }
        public List<TupleIR> Tuples { get; set; }
        public List<BlockIR> Successors { get; set; }
        public List<BlockIR> Predecessors { get; set; }
        public BlockLabelIR Label { get; set; }
        public FunctionIR Parent { get; set; }

        public bool IsEmpty => Tuples == null || Tuples.Count == 0;

        public bool IsBranchBlock {
            get {
                var instr = TransferInstruction;
                return instr?.IsBranch ?? false;
            }
        }

        public bool IsSwitchBlock {
            get {
                var instr = TransferInstruction;
                return instr?.IsSwitch ?? false;
            }
        }

        public bool IsReturnBlock {
            get {
                var instr = TransferInstruction;

                if (instr != null) {
                    return instr.IsReturn;
                }

                // Consider exit block to also be a return block.
                return this == Parent.ExitBlock;
            }
        }

        public bool HasLoopBackedge {
            get {
                return Predecessors.FindIndex(predBlock => predBlock.Number >= Number) != -1;
            }
        }

        public TupleIR FirstTuple => IsEmpty ? null : Tuples[0];
        public TupleIR LastTuple => IsEmpty ? null : Tuples[^1];
        public bool HasLabel => Label != null;

        public IEnumerable<TupleIR> TuplesBack {
            get {
                for (int i = Tuples.Count - 1; i >= 0; i--) {
                    yield return Tuples[i];
                }
            }
        }

        public InstructionIR FirstInstruction {
            get {
                if (IsEmpty) {
                    return null;
                }

                foreach (var t in Tuples) {
                    if (t.IsInstruction) {
                        return t as InstructionIR;
                    }
                }

                return null;
            }
        }

        public InstructionIR LastInstruction {
            get {
                if (IsEmpty) {
                    return null;
                }

                for (int i = Tuples.Count - 1; i >= 0; i--) {
                    if (Tuples[i].IsInstruction) {
                        return Tuples[i] as InstructionIR;
                    }
                }

                return null;
            }
        }

        public IEnumerable<InstructionIR> Instructions {
            get {
                foreach(var tuple in Tuples) {
                    if (tuple is InstructionIR instr) {
                        yield return instr;
                        ;
                    }
                }
            }
        }

        public IEnumerable<InstructionIR> InstructionsBack {
            get {
                for (int i = Tuples.Count - 1; i >= 0; i--) {
                    if (Tuples[i] is InstructionIR instr) {
                        yield return instr;
                        ;
                    }
                }
            }
        }

        public InstructionIR TransferInstruction {
            get {
                var instr = LastInstruction;

                if (instr != null &&
                    (instr.IsBranch || instr.IsGoto || instr.IsSwitch || instr.IsReturn)) {
                    return instr;
                }

                return null;
            }
        }

        public BlockIR BranchTargetBlock {
            get {
                var branchInstr = TransferInstruction;

                if (branchInstr != null && branchInstr.IsBranch) {
                    foreach (var sourceOp in branchInstr.Sources) {
                        if (sourceOp.IsLabelAddress) {
                            var label = sourceOp.BlockLabelValue;
                            return label.Parent;
                        }
                    }
                }

                return null;
            }
        }

        public override void Accept(IRVisitor visitor) {
            visitor.Visit(this);
        }

        public override string ToString() {
            var result = new StringBuilder();
            result.AppendFormat("+ block number: {0} (id: {1})\n", Number, Id);

            if (Predecessors.Count > 0) {
                result.Append("  o preds: ");

                foreach (var block in Predecessors) {
                    result.AppendFormat("{0} ", block.Number);
                }

                result.AppendLine();
            }

            if (Successors.Count > 0) {
                result.Append("  o succs: ");

                foreach (var block in Successors) {
                    result.AppendFormat("{0} ", block.Number);
                }

                result.AppendLine();
            }

            result.AppendFormat("  o tuples: {0}\n", Tuples.Count);

            foreach (var tuple in Tuples) {
                result.AppendFormat("{0}\n", tuple);
            }

            return result.ToString();
        }

        public override bool Equals(object obj) {
            return obj is BlockIR iR && base.Equals(obj) && Number == iR.Number;
        }

        public override int GetHashCode() {
            return HashCode.Combine(base.GetHashCode(), Number);
        }
    }
}
