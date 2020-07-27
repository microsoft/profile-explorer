using IRExplorerCore.Analysis;
using IRExplorerCore.UTC;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests {
    [TestClass]
    public class DominatorAlgorithmTests {
        string functionText =
@"BLOCK 0 Out(1)                                            
ENTRY        ___local_stdio_printf_options ()                           #90
BLOCK 1 In(0) Out(3)                                      
PRAGMA          OPBLKSTART Level: 2                                     #90
                OPRET     &?_OptionsStorage@?1??__local_stdio_printf_options@@9@9 (4|N=4) #92
                OPGOTO    &$LN1                           (?|N=4)       #92
BLOCK 2 Out(3)                                            
PRAGMA          OPBLKEND Level: 2                                       #93
BLOCK 3 In(1,2) Out(4)                                    
$LN1@local_stdi:                                          ; uses = 1 
BLOCK 4 In(3)                                             
EXIT                                                                    #93
BLOCK                                                     ";

        string fixedFunctionText =
@"BLOCK 0 Out(1)
ENTRY        func ()
BLOCK 1 In(0) Out(2,3)
BLOCK 2 In(1) Out(3)
BLOCK 3 In(1,2) Out(4)
BLOCK 4 In(3)
EXIT
BLOCK";

        [TestMethod]
        public void FallbackAlgorithmTerminatesForDominators() {
            var function = new UTCParser(functionText, null, null).Parse();
            var dominatorAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators);
            Assert.IsTrue(dominatorAlgorithm.Dominates(function.Blocks[1], function.Blocks[3]));
            Assert.IsFalse(dominatorAlgorithm.Dominates(function.Blocks[3], function.Blocks[1]));
        }

        [TestMethod]
        public void QueryCacheAndFallbackAlgorithmMatchForDominatorsWithNormalIR() {
            var function = new UTCParser(fixedFunctionText, null, null).Parse();
            var fallbackAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators);
            var cachedAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators | DominatorAlgorithmOptions.BuildQueryCache);
            for (int i = 0; i < 5; ++i) {
                for (int j = 0; j < 5; ++j) {
                    Assert.AreEqual(
                        fallbackAlgorithm.Dominates(function.Blocks[i], function.Blocks[j]),
                        cachedAlgorithm.Dominates(function.Blocks[i], function.Blocks[j]));
                }
            }
        }

        [TestMethod]
        public void QueryCacheAndFallbackAlgorithmMatchForDominatorsWithQuirkyIR() {
            var function = new UTCParser(functionText, null, null).Parse();
            var fallbackAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators);
            var cachedAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators | DominatorAlgorithmOptions.BuildQueryCache);
            for (int i = 0; i < 5; ++i) {
                for (int j = 0; j < 5; ++j) {
                    Assert.AreEqual(
                        fallbackAlgorithm.Dominates(function.Blocks[i], function.Blocks[j]),
                        cachedAlgorithm.Dominates(function.Blocks[i], function.Blocks[j]));
                }
            }
        }

        [TestMethod]
        public void FallbackAlgorithmTerminatesForPostDominators() {
            var function = new UTCParser(functionText, null, null).Parse();
            var dominatorAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators);
            Assert.IsFalse(dominatorAlgorithm.Dominates(function.Blocks[1], function.Blocks[3]));
            Assert.IsTrue(dominatorAlgorithm.Dominates(function.Blocks[3], function.Blocks[1]));
        }


        [TestMethod]
        public void QueryCacheAndFallbackAlgorithmMatchForPostDominatorsWithNormalIR() {
            var function = new UTCParser(fixedFunctionText, null, null).Parse();
            var fallbackAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators);
            var cachedAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators | DominatorAlgorithmOptions.BuildQueryCache);
            for (int i = 0; i < 5; ++i) {
                for (int j = 0; j < 5; ++j) {
                    Assert.AreEqual(
                        fallbackAlgorithm.Dominates(function.Blocks[i], function.Blocks[j]),
                        cachedAlgorithm.Dominates(function.Blocks[i], function.Blocks[j]));
                }
            }
        }

        [TestMethod]
        public void QueryCacheAndFallbackAlgorithmMatchForPostDominatorsWithQuirkyIR() {
            var function = new UTCParser(functionText, null, null).Parse();
            var fallbackAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators);
            var cachedAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators | DominatorAlgorithmOptions.BuildQueryCache);
            for (int i = 0; i < 5; ++i) {
                for (int j = 0; j < 5; ++j) {
                    Assert.AreEqual(
                        fallbackAlgorithm.Dominates(function.Blocks[i], function.Blocks[j]),
                        cachedAlgorithm.Dominates(function.Blocks[i], function.Blocks[j]));
                }
            }
        }
    }
}
