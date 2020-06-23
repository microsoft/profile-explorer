// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CoreLib;

namespace Client {
    public interface INameProvider {
        string GetSectionName(IRTextSection section);

        // GetBlockName
    }
}
