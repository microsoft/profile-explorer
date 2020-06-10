// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Core.IR {
    public sealed class FunctionIR : IRElement {
        public string Name { get; set; }
        public TypeIR ReturnType { get; set; }
        public List<OperandIR> Parameters { get; set; }
        public List<BlockIR> Blocks { get; set; }
        public Dictionary<ulong, IRElement> elementMap_;
        public BlockIR EntryBlock => Blocks.Count > 0 ? Blocks[0] : null;
        public BlockIR ExitBlock => Blocks.Count > 0 ? Blocks[Blocks.Count - 1] : null;

        public FunctionIR() : base(IRElementId.NewFunctionId()) {
            ReturnType = TypeIR.GetUnknown();
            Parameters = new List<OperandIR>();
            Blocks = new List<BlockIR>();
            elementMap_ = new Dictionary<ulong, IRElement>();
        }

        public FunctionIR(string name, TypeIR returnType) : this() {
            Name = name;
            ReturnType = returnType;
        }

        public IRElement GetElementWithId(ulong id) {
            if (elementMap_.TryGetValue(id, out var value)) {
                return value;
            }

            return null;
        }

        public void BuildElementIdMap() {
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

        public void ForEachTuple(Func<TupleIR, bool> action)
        {
            foreach (var block in Blocks)
            {
                foreach (var tuple in block.Tuples)
                {
                    if (!action(tuple))
                    {
                        return;
                    }
                }
            }
        }

        public void ForEachInstruction(Func<InstructionIR, bool> action)
        {
            foreach (var block in Blocks)
            {
                foreach (var tuple in block.Tuples)
                {
                    if (tuple is InstructionIR instr)
                    {
                        if(!action(instr))
                        {
                            return;
                        }
                    }
                }
            }
        }

        public void ForEachElement(Func<IRElement, bool> action)
        {
            foreach(var element in elementMap_.Values)
            {
                if (!action(element))
                {
                    return;
                }
            }
        }


        public override void Accept(IRVisitor visitor) {
            visitor.Visit(this);
        }

        public override bool Equals(object obj)
        {
            return object.ReferenceEquals(this, obj);
        }

        public override int GetHashCode()
        {
            return Name?.GetHashCode() ?? 0;
        }
    }
}
