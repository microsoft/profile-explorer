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
    public class DominanceFrontierTests {
        [TestMethod]
        public void DominanceFrontierForDiamond() {
            var function = TestFunctions.DiamondFunction;
            var dominanceAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.BuildQueryCache | DominatorAlgorithmOptions.Dominators);
            var dominanceFrontier = new DominanceFrontier(function, dominanceAlgorithm);
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(0)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(1)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(4) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(2)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(4) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(3)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(4)).ToList());
        }

        [TestMethod]
        public void DominanceFrontierForSampleFromEngineeringACompiler() {
            var function = TestFunctions.EngineeringACompilerSampleFunction;
            var dominanceAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.BuildQueryCache | DominatorAlgorithmOptions.Dominators);
            var dominanceFrontier = new DominanceFrontier(function, dominanceAlgorithm);
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(0)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(1) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(1)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(3) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(2)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(1) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(3)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(4)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(3) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(5)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(7) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(6)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(3) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(7)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(7) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(8)).ToList());
        }

        [TestMethod]
        public void DominanceFrontierForQuirkyIR() {
            var function = TestFunctions.QuirkyFunction;
            var dominanceAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.BuildQueryCache | DominatorAlgorithmOptions.Dominators);
            var dominanceFrontier = new DominanceFrontier(function, dominanceAlgorithm);

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(0)).ToList());

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(1)).ToList());

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(2)).ToList());

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(3)).ToList());

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(4)).ToList());
        }

        [TestMethod]
        public void PostDominanceFrontierForDiamond() {
            var function = TestFunctions.DiamondFunction;
            var dominanceAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.BuildQueryCache | DominatorAlgorithmOptions.PostDominators);
            var dominanceFrontier = new DominanceFrontier(function, dominanceAlgorithm);
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(0)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(1)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(1) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(2)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(1) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(3)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(4)).ToList());
        }

        [TestMethod]
        public void PostDominanceFrontierForSampleFromEngineeringACompiler() {
            var function = TestFunctions.EngineeringACompilerSampleFunction;
            var dominanceAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.BuildQueryCache | DominatorAlgorithmOptions.PostDominators);
            var dominanceFrontier = new DominanceFrontier(function, dominanceAlgorithm);
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(0)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(3) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(1)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(1) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(2)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(3) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(3)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(4)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(1) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(5)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(5) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(6)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(1) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(7)).ToList());
            CollectionAssert.AreEquivalent(
                new List<BlockIR> { function.BlockByNumber(5) },
                dominanceFrontier.FrontierOf(function.BlockByNumber(8)).ToList());
        }

        [TestMethod]
        public void PostDominanceFrontierForQuirkyIR() {
            var function = TestFunctions.QuirkyFunction;
            var dominanceAlgorithm = new DominatorAlgorithm(function, DominatorAlgorithmOptions.BuildQueryCache | DominatorAlgorithmOptions.PostDominators);
            var dominanceFrontier = new DominanceFrontier(function, dominanceAlgorithm);

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(0)).ToList());

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(1)).ToList());

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(2)).ToList());

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(3)).ToList());

            CollectionAssert.AreEquivalent(
                new List<BlockIR> { },
                dominanceFrontier.FrontierOf(function.BlockByNumber(4)).ToList());
        }
    }
}
