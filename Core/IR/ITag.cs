// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Core.IR {
    public interface ITag {
        string Name { get; }
        IRElement Parent { get; set; }
    }
}
