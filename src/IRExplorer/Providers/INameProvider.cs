// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;

namespace IRExplorer {
    public interface INameProvider {
        string GetSectionName(IRTextSection section);

        // GetBlockName
    }
}
