using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.Analysis {
    public struct CallSite {
        // parent node
        /// target function id,name
        /// element id?
    }

    public class CallNode {
        // list of in callsites
        // list of out callsites
        // list of called functions
        // function id, name
    }

    public class CallGraph {
        private List<CallNode> nodes_;
        private Dictionary<IRTextFunction, CallNode> funcToNodeMap_;
        private HashSet<IRTextFunction> visitedFuncts_;
        private IRTextSummary summary_;

        public List<CallNode> EntryFunctions;

        public CallGraph(IRTextSummary summary) {
            summary_ = summary;
            nodes_ = new List<CallNode>(summary.Functions.Count);
            funcToNodeMap_ = new Dictionary<IRTextFunction, CallNode>(summary.Functions.Count);
        }

        public void Execute() {

        }

        public void Execute(IRTextFunction startFunction) {

        }
    }
}
