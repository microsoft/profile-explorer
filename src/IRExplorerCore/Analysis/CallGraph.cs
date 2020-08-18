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
        private IRTextSummary summary_;
        private IRSectionReader reader_;
        private ICompilerIRInfo irInfo_;

        public List<CallNode> EntryFunctions;

        public CallGraph(IRTextSummary summary, IRSectionReader reader, ICompilerIRInfo irInfo) {
            summary_ = summary;
            reader_ = reader;
            irInfo_ = irInfo;
            nodes_ = new List<CallNode>(summary.Functions.Count);
            funcToNodeMap_ = new Dictionary<IRTextFunction, CallNode>(summary.Functions.Count);
        }

        public void Execute() {

        }

        public void Execute(IRTextFunction startFunction, string sectionName) {
            var worklist = new Queue<IRTextFunction>();
            var visitedFuncts = new HashSet<IRTextFunction>();
            worklist.Enqueue(startFunction);

            while (worklist.Count > 0) {
                var func = worklist.Dequeue();

            }
        }

        private FunctionIR LoadFunction(IRTextFunction func) {
            reader_.GetSectionText()
        }
    }
}
