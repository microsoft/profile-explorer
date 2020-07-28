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
        private const string DiamondFunctionText = 
@"BLOCK 0 Out(1)
ENTRY        func ()
BLOCK 1 In(0) Out(2,3)
BLOCK 2 In(1) Out(4)
BLOCK 3 In(1) Out(4)
BLOCK 4 In(2,3)
EXIT
BLOCK";
        private const string QuirkyFunctionText =
@"BLOCK 0 Out(1)
ENTRY func()
BLOCK 1 In(0) Out(3)
BLOCK 2 Out(3)
BLOCK 3 In(1,2) Out(4)
BLOCK 4 In(3)
EXIT
BLOCK";
        private const string EngineeringACompilerSampleFunction = 
@"BLOCK 0 Out(1)
ENTRY func ()
BLOCK 1 In(0,3) Out(2,5)
BLOCK 2 In(1) Out(3)
BLOCK 3 In(2,7) Out(4,1)
BLOCK 5 In(1) Out(6,8)
BLOCK 6 In(5) Out(7)
BLOCK 7 In(6,8) Out(3)
BLOCK 8 In(5) Out(7)
BLOCK 4 In(3)
EXIT
BLOCK";

        [TestMethod]
        public void DominanceFrontierForDiamond() {
            var function = new UTCParser(DiamondFunctionText, null, null).Parse();
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
            var function = new UTCParser(EngineeringACompilerSampleFunction, null, null).Parse();
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
            var function = new UTCParser(QuirkyFunctionText, null, null).Parse();
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
            var function = new UTCParser(DiamondFunctionText, null, null).Parse();
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
            var function = new UTCParser(EngineeringACompilerSampleFunction, null, null).Parse();
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
            var function = new UTCParser(QuirkyFunctionText, null, null).Parse();
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
