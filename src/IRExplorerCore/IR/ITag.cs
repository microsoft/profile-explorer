// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace IRExplorerCore.IR {
    public interface ITag {
        string Name { get; }
        TaggedObject Owner { get; set; }
    }
}
