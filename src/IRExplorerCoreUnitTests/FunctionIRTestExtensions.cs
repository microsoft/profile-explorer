using IRExplorerCore.IR;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests {
    public static class FunctionIRTestExtensions {
        public static BlockIR BlockByNumber(this FunctionIR function, ushort blockNumber) {
            var block = function.GetElementWithId(IRElementId.ToLong(blockId: blockNumber)) as BlockIR;
            if (block != null) {
                Assert.AreEqual(block.Number, blockNumber);
                return block;
            }
            Assert.Fail("Failed to find block by number");
            throw new System.Exception("Unreached");
        }
    }
}
