// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests {
    [TestClass]
    public class DominatorAlgorithmTests {
        [TestMethod]
        public void FallbackAlgorithmTerminatesForDominators() {
            var function = TestFunctions.QuirkyFunction;
            var dominatorAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators);
            Assert.IsTrue(dominatorAlgorithm.Dominates(function.BlockByNumber(1), function.BlockByNumber(3)));
            Assert.IsFalse(dominatorAlgorithm.Dominates(function.BlockByNumber(3), function.BlockByNumber(1)));
        }

        [TestMethod]
        public void QueryCacheAndFallbackAlgorithmMatchForDominatorsWithNormalIR() {
            var function = TestFunctions.SimpleFunction;
            var fallbackAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators);
            var cachedAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators | DominatorAlgorithmOptions.BuildQueryCache);
            for (ushort i = 0; i < 5; ++i) {
                for (ushort j = 0; j < 5; ++j) {
                    Assert.AreEqual(
                        fallbackAlgorithm.Dominates(function.BlockByNumber(i), function.BlockByNumber(j)),
                        cachedAlgorithm.Dominates(function.BlockByNumber(i), function.BlockByNumber(j)));
                }
            }
        }

        [TestMethod]
        public void QueryCacheAndFallbackAlgorithmMatchForDominatorsWithQuirkyIR() {
            var function = TestFunctions.QuirkyFunction;
            var fallbackAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators);
            var cachedAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators | DominatorAlgorithmOptions.BuildQueryCache);
            for (ushort i = 0; i < 5; ++i) {
                for (ushort j = 0; j < 5; ++j) {
                    Assert.AreEqual(
                        fallbackAlgorithm.Dominates(function.BlockByNumber(i), function.BlockByNumber(j)),
                        cachedAlgorithm.Dominates(function.BlockByNumber(i), function.BlockByNumber(j)));
                }
            }
        }

        [TestMethod]
        public void FallbackAlgorithmTerminatesForPostDominators() {
            var function = TestFunctions.QuirkyFunction;
            var dominatorAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators);
            Assert.IsFalse(dominatorAlgorithm.Dominates(function.BlockByNumber(1), function.BlockByNumber(3)));
            Assert.IsTrue(dominatorAlgorithm.Dominates(function.BlockByNumber(3), function.BlockByNumber(1)));
        }


        [TestMethod]
        public void QueryCacheAndFallbackAlgorithmMatchForPostDominatorsWithNormalIR() {
            var function = TestFunctions.SimpleFunction;
            var fallbackAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators);
            var cachedAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators | DominatorAlgorithmOptions.BuildQueryCache);
            for (ushort i = 0; i < 5; ++i) {
                for (ushort j = 0; j < 5; ++j) {
                    Assert.AreEqual(
                        fallbackAlgorithm.Dominates(function.BlockByNumber(i), function.BlockByNumber(j)),
                        cachedAlgorithm.Dominates(function.BlockByNumber(i), function.BlockByNumber(j)));
                }
            }
        }

        [TestMethod]
        public void QueryCacheAndFallbackAlgorithmMatchForPostDominatorsWithQuirkyIR() {
            var function = TestFunctions.QuirkyFunction;
            var fallbackAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators);
            var cachedAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators | DominatorAlgorithmOptions.BuildQueryCache);
            for (ushort i = 0; i < 5; ++i) {
                for (ushort j = 0; j < 5; ++j) {
                    Assert.AreEqual(
                        fallbackAlgorithm.Dominates(function.BlockByNumber(i), function.BlockByNumber(j)),
                        cachedAlgorithm.Dominates(function.BlockByNumber(i), function.BlockByNumber(j)));
                }
            }
        }

        [TestMethod]
        public void GetDominatorsReturnsAllNodesThatDominateProvidedNodeForNormalIR() {
            var function = TestFunctions.SimpleFunction;
            var algorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators | DominatorAlgorithmOptions.BuildQueryCache);

            // entry node has no dominators
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                algorithm.GetDominators(function.BlockByNumber(0)).ToList());

            // block 1 is dominated by block 0
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(0) },
                algorithm.GetDominators(function.BlockByNumber(1)).ToList());

            // block 2 is dominated by blocks 1 and 0
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(0), function.BlockByNumber(1) },
                algorithm.GetDominators(function.BlockByNumber(2)).ToList());

            // block 3 is dominated by block 0 and 1
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(0), function.BlockByNumber(1) },
                algorithm.GetDominators(function.BlockByNumber(3)).ToList());

            // block 4 is dominated by block 0, 1, and 3
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(0), function.BlockByNumber(1), function.BlockByNumber(3) },
                algorithm.GetDominators(function.BlockByNumber(4)).ToList());
        }

        [TestMethod]
        public void GetDominatorsReturnsAllNodesThatDominateProvidedNodeForQuirkyIR() {
            var function = TestFunctions.QuirkyFunction;
            var algorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.Dominators | DominatorAlgorithmOptions.BuildQueryCache);

            // block 0 has no dominators
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                algorithm.GetDominators(function.BlockByNumber(0)).ToList());

            // block 1 is dominated by block 0
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(0) },
                algorithm.GetDominators(function.BlockByNumber(1)).ToList());

            // block 2 is not in the graph
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                algorithm.GetDominators(function.BlockByNumber(2)).ToList());

            // block 3 is dominated by block 0 and 1
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(0), function.BlockByNumber(1) },
                algorithm.GetDominators(function.BlockByNumber(3)).ToList());

            // block 4 is dominated by block 0, 1, and 3
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(0), function.BlockByNumber(1), function.BlockByNumber(3) },
                algorithm.GetDominators(function.BlockByNumber(4)).ToList());
        }

        [TestMethod]
        public void GetPostDominatorsReturnsAllNodesThatPostDominateProvidedNodeForNormalIR() {
            var function = TestFunctions.SimpleFunction;
            var algorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators | DominatorAlgorithmOptions.BuildQueryCache);

            // block 0 is post-dominated by blocks 1, 3, and 4
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(1), function.BlockByNumber(3), function.BlockByNumber(4) },
                algorithm.GetDominators(function.BlockByNumber(0)).ToList());

            // block 1 is post-dominated by blocks 3 and 4
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(3), function.BlockByNumber(4) },
                algorithm.GetDominators(function.BlockByNumber(1)).ToList());

            // block 2 is post-dominated by blocks 3 and 4
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(3), function.BlockByNumber(4) },
                algorithm.GetDominators(function.BlockByNumber(2)).ToList());

            // block 3 is post-dominated by block 4
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(4) },
                algorithm.GetDominators(function.BlockByNumber(3)).ToList());

            // block 4 has no post-dominators
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                algorithm.GetDominators(function.BlockByNumber(4)).ToList());
        }

        [TestMethod]
        public void GetPostDominatorsReturnsAllNodesThatPostDominateProvidedNodeForQuirkyIR() {
            var function = TestFunctions.QuirkyFunction;
            var algorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.PostDominators | DominatorAlgorithmOptions.BuildQueryCache);

            // block 0 is post-dominated by blocks 1, 3, and 4
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(1), function.BlockByNumber(3), function.BlockByNumber(4) },
                algorithm.GetDominators(function.BlockByNumber(0)).ToList());

            // block 1 is post-dominated by blocks 3 and 4
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(3), function.BlockByNumber(4) },
                algorithm.GetDominators(function.BlockByNumber(1)).ToList());

            // block 2 is not in the graph
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                algorithm.GetDominators(function.BlockByNumber(2)).ToList());

            // block 3 is post-dominated by block 4
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(4) },
                algorithm.GetDominators(function.BlockByNumber(3)).ToList());

            // block 4 has no post-dominators
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                algorithm.GetDominators(function.BlockByNumber(4)).ToList());
        }
    }
}
