using System;
using System.Collections.Generic;
using System.Text;

namespace Core.IR.Tags
{
    public class AddressMetadataTag : ITag {
        public string Name { get => "Address metadata"; }
        public IRElement Parent { get; set; }
        public Dictionary<long, IRElement> AddressToElementMap { get; set; }

        public AddressMetadataTag(Dictionary<long, IRElement> map) {
            AddressToElementMap = map;
        }
    }
}
