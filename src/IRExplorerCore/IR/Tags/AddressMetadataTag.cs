// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR.Tags {
    public class AddressMetadataTag : ITag {
        public AddressMetadataTag() {
            AddressToElementMap = new Dictionary<long, IRElement>();
            OffsetToElementMap = new Dictionary<long, IRElement>();
        }

        public Dictionary<long, IRElement> AddressToElementMap { get; set; }
        public Dictionary<long, IRElement> OffsetToElementMap { get; set; }
        public string Name => "Address metadata";
        public TaggedObject Owner { get; set; }

        public override string ToString() {
            var builder = new StringBuilder();

            foreach (var pair in OffsetToElementMap) {
                builder.Append($"{pair.Key} = {pair.Value}");
            }

            return builder.ToString();
        }
    }
}
