using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests {
    [TestClass]
    public class FunctionAnalysisCacheTests {
        private static readonly FunctionIR SimpleFunction = TestFunctions.SimpleFunction;
        private static readonly FunctionIR DiamondFunction = TestFunctions.DiamondFunction;

        [TestCleanup]
        public void Cleanup() {
            FunctionAnalysisCache.TestReset();
        }

        [TestMethod]
        public void GetReturnsSameInstanceForSameFunction() {
            Assert.IsNotNull(FunctionAnalysisCache.Get(SimpleFunction));
            Assert.AreSame(
                FunctionAnalysisCache.Get(SimpleFunction),
                FunctionAnalysisCache.Get(SimpleFunction));
        }

        [TestMethod]
        public void GetReturnsDifferentInstancesForDifferentFunction() {
            Assert.AreNotSame(
                FunctionAnalysisCache.Get(SimpleFunction),
                FunctionAnalysisCache.Get(DiamondFunction));
        }

        [TestMethod]
        public async Task GetDominanceFrontierAsyncReturnsSameInstanceWhenCaching() {
            var cache = FunctionAnalysisCache.Get(SimpleFunction);
            var expected = await cache.GetDominanceFrontierAsync();
            Assert.AreSame(expected, await cache.GetDominanceFrontierAsync());
        }

        [TestMethod]
        public async Task GetDominanceFrontierAsyncReturnsDifferentInstanceWhenNotCaching() {
            FunctionAnalysisCache.DisableCache();
            var cache = FunctionAnalysisCache.Get(SimpleFunction);
            Assert.AreNotSame(
                await cache.GetDominanceFrontierAsync(),
                await cache.GetDominanceFrontierAsync());
        }

        [TestMethod]
        public async Task GetDominanceFrontierReturnsCorrectInstance() {
            var cache = FunctionAnalysisCache.Get(DiamondFunction);
            var dominanceFrontier = await cache.GetDominanceFrontierAsync();
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { DiamondFunction.BlockByNumber(4) },
                dominanceFrontier.FrontierOf(DiamondFunction.BlockByNumber(2)).ToList());
        }
    }
}
