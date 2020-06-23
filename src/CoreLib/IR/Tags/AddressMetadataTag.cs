using System.Collections.Generic;

namespace CoreLib.IR.Tags {
    public class AddressMetadataTag : ITag {
        public AddressMetadataTag(Dictionary<long, IRElement> map) {
            AddressToElementMap = map;
        }

        public Dictionary<long, IRElement> AddressToElementMap { get; set; }
        public string Name => "Address metadata";
        public IRElement Parent { get; set; }
    }
}
