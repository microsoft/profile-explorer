using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.UTC {
    class UTCReferenceFilter : IReachableReferenceFilter {
        private FunctionIR function_;
        private CFGReachability cfgReachability_;

        public UTCReferenceFilter(FunctionIR function) {
            function_ = function;
        }

        public bool FilterDefinitions { get; set; }
        public bool FilterUses { get; set; }

        private CFGReachability GetReachabilityInfo() {
            if(cfgReachability_ == null) {
                cfgReachability_ = FunctionAnalysisCache.Get(function_).GetReachability();
            }

            return cfgReachability_;
        }

        public bool AcceptReference(IRElement element, IRElement startElement) {
            return true;
        }

        public bool AcceptUseReference(IRElement element, IRElement startDestElement) {
            if(!FilterUses) {
                return false;
            }

            // Accept element if it can be reached from the dest. element.
            //? TODO: Use reaching definitions if available
            var cfgReachability = GetReachabilityInfo();
            
            if(!cfgReachability.Reaches(startDestElement.ParentBlock, element.ParentBlock)) {
                return false;
            }

            // If in the same block, accept it only if dest is found before the use,
            // or the block is found in a loop (value may reach through a backedge).
            if(startDestElement.ParentBlock == element.ParentBlock) {
                var destIndex = startDestElement.ParentInstruction.IndexInBlock;
                var useIndex = element.ParentInstruction.IndexInBlock;
                return destIndex < useIndex;
            }

            return true;
        }

        public bool AcceptDefinitionReference(IRElement element, IRElement startSourceElement) {
            if(!FilterDefinitions) {
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
                var destIndex = element.ParentInstruction.IndexInBlock;
                var useIndex = startSourceElement.ParentInstruction.IndexInBlock;
                return destIndex < useIndex;
            }


            return true;
        }
    }
}
