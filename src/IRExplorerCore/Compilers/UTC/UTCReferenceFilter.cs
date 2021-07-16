using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.UTC {
    class UTCReferenceFilter : CFGReachabilityReferenceFilter {
        public UTCReferenceFilter(FunctionIR function) : base(function) {
            
        }
    }
}
