using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public class RegionIR : IRElement {
        public RegionIR(IRElementId elementId, TupleIR owner, RegionIR parentRegion) : base(elementId) {
            ParentRegion = parentRegion;
            Owner = owner;
            Blocks = new List<BlockIR>();
            ChildRegions = new List<RegionIR>();

            if (parentRegion != null) {
                parentRegion.ChildRegions.Add(this);
            }
        }

        public TupleIR Owner { get; set; }
        public RegionIR ParentRegion { get; set; }
        public List<RegionIR> ChildRegions { get; set;}
        public List<BlockIR> Blocks { get; set; }

        public bool IsEmpty => Blocks == null || Blocks.Count == 0;
        public bool HasChildRegions => ChildRegions != null && ChildRegions.Count > 0;

        public override void Accept(IRVisitor visitor) {
            visitor.Visit(this);
        }

        public override string ToString() {
            var result = new StringBuilder();
            result.AppendLine($"region: {Id}");
            result.AppendLine($"  o blocks: {Blocks.Count}");
            result.AppendLine($"  o nested regions: {ChildRegions.Count}");
            return result.ToString();
        }
    }
}