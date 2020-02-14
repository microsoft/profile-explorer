// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Core.IR {
    public interface IRVisitor {
        void Visit(IRElement value);
        void Visit(OperandIR value);
        void Visit(TupleIR value);
        void Visit(BlockLabelIR value);
        void Visit(InstructionIR value);
        void Visit(BlockIR value);
        void Visit(FunctionIR value);
    }
}
