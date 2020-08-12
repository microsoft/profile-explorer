using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerUI.Query;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using System.Windows;

namespace IRExplorerUI.Compilers.UTC {
    class UTCBuiltinInterferenceActions : IElementQuery {
        public static ElementQueryDefinition GetDefinition() {
            var query = new ElementQueryDefinition(typeof(UTCBuiltinInterferenceActions),
                                                   "Alias marking",
                                                   "Alias query results for two values");
            query.Data.AddInput("Operand", QueryValueKind.Element);
            query.Data.AddInput("Temporary marking", QueryValueKind.Bool);
            query.Data.SetOutput("Aliasing values", 0);
            query.Data.SetOutput("Mark aliasing values", "");

            var a = query.Data.AddButton("All");
            a.HasDemiBoldText = true;
            a.Action = (sender, data) => MessageBox.Show("Test");

            var b = query.Data.AddButton("Block");
            b.Action = (sender, data) => MessageBox.Show("Test");

            var c = query.Data.AddButton("Loop");
            c.Action = (sender, data) => MessageBox.Show("Test");

            var d = query.Data.AddButton("Loop nest");
            d.Action = (sender, data) => MessageBox.Show("Test");

            return query;
        }

        public ISessionManager Session { get; private set; }

        public bool Initialize(ISessionManager session) {
            Session = session;
            return true;
        }

        public bool Execute(QueryData data) {
            var elementA = data.GetInput<IRElement>(0);
            var func = elementA.ParentFunction;

            data.ResetResults();
            data.SetOutput("Aliasing values", 0);
            return true;
        }
    }

    class UTCBuiltinInterferenceQuery : IElementQuery {
        public static ElementQueryDefinition GetDefinition() {
            var query = new ElementQueryDefinition(typeof(UTCBuiltinInterferenceQuery),
                                                   "Alias query",
                                                   "Alias query results for two values");
            query.Data.AddInput("Operand 1", QueryValueKind.Element);
            query.Data.AddInput("Operand 2", QueryValueKind.Element);
            query.Data.AddOutput("May Alias", QueryValueKind.Bool);
            return query;
        }

        public ISessionManager Session { get; private set; }

        public bool Initialize(ISessionManager session) {
            Session = session;
            return true;
        }

        public bool Execute(QueryData data) {
            var elementA = data.GetInput<IRElement>("Operand 1");
            var elementB = data.GetInput<IRElement>("Operand 2");
            var func = elementA.ParentFunction;

            data.ResetResults();
            data.SetOutputWarning("May Alias", "Not yet implemented!");
            return true;
        }
    }
}
