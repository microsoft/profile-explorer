// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace IRExplorerCore.IR.Tags {
    public class AddressMetadataTag : ITag {
        public AddressMetadataTag(Dictionary<long, IRElement> map) {
            AddressToElementMap = map;
        }

        public Dictionary<long, IRElement> AddressToElementMap { get; set; }
        public string Name => "Address metadata";
        public IRElement Owner { get; set; }
    }
}
