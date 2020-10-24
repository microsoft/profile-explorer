// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace IRExplorerCore.IR {
    public sealed class FunctionIR : IRElement {
        private Dictionary<ulong, IRElement> elementMap_;
        private int instructionCount_;
        private int tupleCount_;

        public FunctionIR() : base(IRElementId.NewFunctionId()) {
            ReturnType = TypeIR.GetUnknown();
            Parameters = new List<OperandIR>();
            Blocks = new List<BlockIR>();
        }

        public FunctionIR(string name, TypeIR returnType) : this() {
            Name = name;
            ReturnType = returnType;
        }

        public new string Name { get; set; }

        public TypeIR ReturnType { get; set; }
        public List<OperandIR> Parameters { get; }
        public List<BlockIR> Blocks { get; }
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

        public IEnumerable<IRElement> AllElements {
            get {
                foreach (var block in Blocks) {
                    yield return block;

                    foreach (var tuple in block.Tuples) {
                        yield return tuple;

                        if (tuple is InstructionIR instr) {
                            foreach (var op in instr.Destinations) {
                                yield return op;
                            }

                            foreach (var op in instr.Sources) {
                                yield return op;
                            }
                        }
                    }
                }
            }
        }

        public void ForEachElement(Func<IRElement, bool> action) {
            foreach (var element in AllElements) {
                if (!action(element)) {
                    return;
                }
            }
        }

        public IEnumerable<TupleIR> AllTuples {
            get {
                foreach (var block in Blocks) {
                    foreach (var tuple in block.Tuples) {
                        yield return tuple;
                    }
                }
            }
        }

        public void ForEachTuple(Func<TupleIR, bool> action) {
            foreach (var tuple in AllTuples) {
                if (!action(tuple)) {
                    return;
                }
            }
        }

        public IEnumerable<InstructionIR> AllInstructions {
            get {
                foreach (var block in Blocks) {
                    foreach (var tuple in block.Tuples) {
                        if (tuple is InstructionIR instr) {
                            yield return instr;
                        }
                    }
                }

            }
        }
    
        public void ForEachInstruction(Func<InstructionIR, bool> action) {
            foreach (var instr in AllInstructions) {
                if (!action(instr)) {
                    return;
                }
            }
        }

        public int InstructionCount {
            get {
                if (instructionCount_ == 0) {
                    ForEachInstruction((instr) => {
                        instructionCount_++;
                        return true;
                    });
                }

                return instructionCount_;
            }
        }

        public int TupleCount {
            get {
                if (tupleCount_ == 0) {
                    ForEachTuple((tuple) => {
                        tupleCount_++;
                        return true;
                    });
                }

                return tupleCount_;
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
