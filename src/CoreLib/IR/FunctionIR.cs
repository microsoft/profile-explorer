// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace IRExplorerCore.IR {
    public sealed class FunctionIR : IRElement {
        private Dictionary<ulong, IRElement> elementMap_;

        public FunctionIR() : base(IRElementId.NewFunctionId()) {
            ReturnType = TypeIR.GetUnknown();
            Parameters = new List<OperandIR>();
            Blocks = new List<BlockIR>();
        }

        public FunctionIR(string name, TypeIR returnType) : this() {
            Name = name;
            ReturnType = returnType;
        }

        public string Name { get; set; }
        public TypeIR ReturnType { get; set; }
        public List<OperandIR> Parameters { get; set; }
        public List<BlockIR> Blocks { get; set; }
        public BlockIR EntryBlock => Blocks.Count > 0 ? Blocks[0] : null;
        public BlockIR ExitBlock => Blocks.Count > 0 ? Blocks[^1] : null;

        public IRElement GetElementWithId(ulong id) {
            BuildElementIdMap();
            return elementMap_.TryGetValue(id, out var value) ? value : null;
        }

        public void BuildElementIdMap() {
            if (elementMap_ != null) {
                return;
            }

            elementMap_ = new Dictionary<ulong, IRElement>();

            foreach (var block in Blocks) {
                elementMap_[block.Id] = block;

                foreach (var tuple in block.Tuples) {
                    elementMap_[tuple.Id] = tuple;

                    if (tuple is InstructionIR instr) {
                        foreach (var op in instr.Destinations) {
                            elementMap_[op.Id] = op;
                        }

                        foreach (var op in instr.Sources) {
                            elementMap_[op.Id] = op;
                        }
                    }
                }
            }
        }

        public void ForEachTuple(Func<TupleIR, bool> action) {
            foreach (var block in Blocks) {
                foreach (var tuple in block.Tuples) {
                    if (!action(tuple)) {
                        return;
                    }
                }
            }
        }

        public void ForEachInstruction(Func<InstructionIR, bool> action) {
            foreach (var block in Blocks) {
                foreach (var tuple in block.Tuples) {
                    if (tuple is InstructionIR instr) {
                        if (!action(instr)) {
                            return;
                        }
                    }
                }
            }
        }

        public void ForEachElement(Func<IRElement, bool> action) {
            BuildElementIdMap();

            foreach (var element in elementMap_.Values) {
                if (!action(element)) {
                    return;
                }
            }
        }

        public override void Accept(IRVisitor visitor) {
            visitor.Visit(this);
        }

        public override bool Equals(object obj) {
            return ReferenceEquals(this, obj);
        }

        public override int GetHashCode() {
            return Name?.GetHashCode() ?? 0;
        }
    }
}
