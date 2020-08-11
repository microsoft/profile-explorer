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
            query.Data.AddInput("Operand A", QueryValueKind.Element);
            query.Data.AddInput("Operand B", QueryValueKind.Element);
            query.Data.AddInput("Mark all aliasing values", QueryValueKind.Bool);
            query.Data.AddOutput("May Alias", QueryValueKind.Bool);
            return query;
        }

        public bool Execute(QueryData data) {
            var elementA = data.GetInput<IRElement>("Operand A");
            var elementB = data.GetInput<IRElement>("Operand B");
            var markAliasing = data.GetInput<bool>("Mark all aliasing values");
            data.ResetResults();

            data.SetOutputWarning("May Alias", "Not yet implemented!");
            return true;
        }
    }
}
