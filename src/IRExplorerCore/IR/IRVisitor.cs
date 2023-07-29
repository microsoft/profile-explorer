// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace IRExplorerCore.IR {
    public interface IRVisitor {
        void Visit(IRElement value);
        void Visit(OperandIR value);
        void Visit(TupleIR value);
        void Visit(BlockLabelIR value);
        void Visit(InstructionIR value);
        void Visit(BlockIR value);
        void Visit(RegionIR value);
        void Visit(FunctionIR value);
    }
}