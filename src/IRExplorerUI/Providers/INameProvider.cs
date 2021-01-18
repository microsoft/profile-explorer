// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public interface INameProvider {
        ICompilerIRInfo IR { get; set; }
        string GetSectionName(IRTextSection section, bool includeNumber = false);
        string GetBlockName(BlockIR block);
        string GetBlockLabelName(BlockIR block);
        string GetBlockAndLabelName(BlockIR block);
    }
}
