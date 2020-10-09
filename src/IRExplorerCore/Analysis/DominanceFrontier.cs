// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore.IR;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IRExplorerCore.Analysis {
    public class DominanceFrontier {
        private readonly Dictionary<BlockIR, List<BlockIR>> frontierMap_;
        public DominanceFrontier(FunctionIR function, DominatorAlgorithm dominanceAlgorithm) {
            frontierMap_ = BuildDominanceFrontiers(function, dominanceAlgorithm);
        }

        public IReadOnlyList<BlockIR> FrontierOf(BlockIR block) {
            return frontierMap_[block];
        }

        private static Dictionary<BlockIR, List<BlockIR>>
            BuildDominanceFrontiers(FunctionIR function, DominatorAlgorithm dominanceAlgorithm) {
            // Algorithm adapted from Engineering a Compiler, 2nd edition page 499
            var dominanceFrontiers = InitializeDominanceFrontiersToEmpty(function);

            AddCFGDominanceFrontierInfo(function, dominanceAlgorithm, dominanceFrontiers);
            return ConvertResult(dominanceFrontiers);
        }

        private static Dictionary<BlockIR, HashSet<BlockIR>> InitializeDominanceFrontiersToEmpty(FunctionIR function) {
            var result = new Dictionary<BlockIR, HashSet<BlockIR>>();

            foreach (var block in function.Blocks) {
                result[block] = new HashSet<BlockIR>();
            }

            return result;
        }

        private static void AddCFGDominanceFrontierInfo(FunctionIR function, DominatorAlgorithm dominanceAlgorithm, 
                                                        Dictionary<BlockIR, HashSet<BlockIR>> dominanceFrontiers) {
            var blocks = new CFGBlockOrdering(function).PostorderList;

            foreach (var block in blocks) {
                var nextBlocks = dominanceAlgorithm.NextBlocks(block).Where(blocks.Contains).ToList();
            
                if (nextBlocks.Count > 1) {
                    foreach (var nextBlock in nextBlocks) {
                        var runner = nextBlock;
                
                        while (runner != dominanceAlgorithm.GetImmediateDominator(block)) {
                            dominanceFrontiers[runner].Add(block);
                            runner = dominanceAlgorithm.GetImmediateDominator(runner);
                        }
                    }
                }
            }
        }

        private static Dictionary<BlockIR, List<BlockIR>> 
            ConvertResult(Dictionary<BlockIR, HashSet<BlockIR>> hashedResult) {
            var result = new Dictionary<BlockIR, List<BlockIR>>();
            
            foreach (var entry in hashedResult) {
                var list = new List<BlockIR>();
            
                foreach (var value in entry.Value) {
                    list.Add(value);
                }
                
                result[entry.Key] = list;
            }

            return result;
        }
    }
}
