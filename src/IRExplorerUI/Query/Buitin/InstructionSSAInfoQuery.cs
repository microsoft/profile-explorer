using IRExplorerUI.Scripting;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerUI.Query.Builtin {
    public class InstructionSSAInfoQuery : IElementQuery {
        public static QueryDefinition GetDefinition() {
            var query = new QueryDefinition(typeof(InstructionSSAInfoQuery),
                                                   "Instruction SSA details",
                                                   "Details about values with SSA info");
            query.Data.AddInput("Instruction", QueryValueKind.Element);
            query.Data.AddOutput("User Count", QueryValueKind.Number);
            query.Data.AddOutput("Source Definitions Dominate", QueryValueKind.Bool);
            return query;
        }

        private ISessionManager session_;
        public ISessionManager Session => session_;

        public bool Initialize(ISessionManager session) {
            session_ = session;
            return true;
        }

        public bool Execute(QueryData data) {
            var element = data.GetInput<IRElement>("Instruction");
            data.ResetResults();

            if (element is InstructionIR instr) {
                if (instr.Destinations.Count > 0) {
                    var defTag = instr.Destinations[0].GetTag<SSADefinitionTag>();

                    if (defTag != null) {
                        data.SetOutput("User Count", defTag.Users.Count);
                    }
                }

                bool allSourcesDominate = true;

                foreach (var sourceOp in instr.Sources) {
                    var defOp = ReferenceFinder.GetSSADefinition(sourceOp);

                    if (defOp == null) {
                        continue;
                    }

                    var cache = FunctionAnalysisCache.Get(element.ParentFunction);
                    var dominatorAlgo = cache.GetDominators();

                    if (!dominatorAlgo.Dominates(defOp.ParentBlock, instr.ParentBlock)) {
                        allSourcesDominate = false;
                        data.SetOutput("Source Definitions Dominate", false);

                        data.SetOutputWarning("Source Definitions Dominate",
                                              $"Definition {Utils.MakeElementDescription(defOp)} of source {Utils.MakeElementDescription(sourceOp)} does not dominate!");

                        break;
                    }
                }

                if (allSourcesDominate) {
                    data.SetOutput("Source Definitions Dominate", true);
                }
            }
            else {
                data.SetInputWarning("Instruction", "Selected element is not an instruction!");
            }

            return true;
        }
    }
}
