using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis {
    public class CFGReachabilityReferenceFilter : IReachableReferenceFilter {
        protected FunctionIR function_;
        protected CFGReachability cfgReachability_;

        public bool FilterDefinitions { get; set; }
        public bool FilterUses { get; set; }

        public CFGReachabilityReferenceFilter(FunctionIR function) {
            function_ = function;
        }

        public virtual bool AcceptDefinitionReference(IRElement element, IRElement startSourceElement) {
            if (!FilterDefinitions) {
                return false;
            }

            // Accept element if it's a definition that can reach the source element.
            var cfgReachability = GetReachabilityInfo();

            if (!cfgReachability.Reaches(element.ParentBlock, startSourceElement.ParentBlock)) {
                return false;
            }

            //? TODO: Use reaching definitions if available
            // If in the same block, accept it only if dest is found before the use,
            // or the block is found in a loop (value may reach through a backedge).
            if (startSourceElement.ParentBlock == element.ParentBlock) {
                if (element.ParentInstruction == null) {
                    return true; // Parameter dominates everything.
                }
                else if (startSourceElement.ParentInstruction == null) {
                    return false;
                }

                var destIndex = element.ParentInstruction.IndexInBlock;
                var useIndex = startSourceElement.ParentInstruction.IndexInBlock;
                return destIndex < useIndex;
            }


            return true;
        }

        public virtual bool AcceptReference(IRElement element, IRElement startElement) {
            return true;
        }

        public virtual bool AcceptUseReference(IRElement element, IRElement startDestElement) {
            if (!FilterUses) {
                return false;
            }

            // Accept element if it can be reached from the dest. element.
            //? TODO: Use reaching definitions if available
            var cfgReachability = GetReachabilityInfo();

            if (!cfgReachability.Reaches(startDestElement.ParentBlock, element.ParentBlock)) {
                return false;
            }

            // If in the same block, accept it only if dest is found before the use,
            // or the block is found in a loop (value may reach through a backedge).
            if (startDestElement.ParentBlock == element.ParentBlock) {
                if (startDestElement.ParentInstruction == null) {
                    return true; // Use dominated by parameter.
                }
                else if (element.ParentInstruction == null) {
                    return false;
                }

                var destIndex = startDestElement.ParentInstruction.IndexInBlock;
                var useIndex = element.ParentInstruction.IndexInBlock;
                return destIndex < useIndex;
            }

            return true;
        }

        private CFGReachability GetReachabilityInfo() {
            cfgReachability_ ??= FunctionAnalysisCache.Get(function_).GetReachability();
            return cfgReachability_;
        }
    }
}