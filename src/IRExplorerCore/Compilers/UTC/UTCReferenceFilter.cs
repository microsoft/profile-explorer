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
            return cfgReachability.Reaches(startDestElement.ParentBlock, element.ParentBlock);
        }

        public bool AcceptDefinitionReference(IRElement element, IRElement startSourceElement) {
            if(!FilterDefinitions) {
                return false;
            }

            // Accept element if it's a definition that can reach the source element.
            var cfgReachability = GetReachabilityInfo();
            return cfgReachability.Reaches(element.ParentBlock, startSourceElement.ParentBlock);
        }
    }
}
