using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerUI.Query;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.UTC {
    class UTCBuiltinInterferenceQuery : IElementQuery {
        public static ElementQueryDefinition GetDefinition() {
            var query = new ElementQueryDefinition(typeof(UTCBuiltinInterferenceQuery),
                                                   "Interference",
                                                   "Alias query results for two values");
            query.Data.AddInput("Operand 1", QueryValueKind.Element);
            query.Data.AddInput("Operand 2", QueryValueKind.Element);
            query.Data.AddInput("Mark all aliasing values", QueryValueKind.Bool);
            query.Data.AddOutput("May Alias", QueryValueKind.Bool);
            return query;
        }

        private ISessionManager session_;
        public ISessionManager Session => session_;

        public bool Initialize(ISessionManager session) {
            session_ = session;
            return true;
        }

        public bool Execute(QueryData data) {
            var elementA = data.GetInput<IRElement>("Operand 1");
            var elementB = data.GetInput<IRElement>("Operand 2");
            var markAliasing = data.GetInput<bool>("Mark all aliasing values");
            var func = elementA.ParentFunction;

            data.ResetResults();
            data.SetOutputWarning("May Alias", "Not yet implemented!");
            return true;
        }
    }
}
