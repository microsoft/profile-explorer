using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerUI.Query;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using System.Windows;
using IRExplorerCore.UTC;
using System.Windows.Media;

namespace IRExplorerUI.Compilers.UTC {
    class UTCBuiltinInterferenceActions : IElementQuery {
        public static QueryDefinition GetDefinition() {
            var query = new QueryDefinition(typeof(UTCBuiltinInterferenceActions),
                                                   "Alias marking",
                                                   "Alias query results for two values");
            query.Data.AddInput("Operand", QueryValueKind.Element);
            query.Data.AddInput("PAS", QueryValueKind.Number);
            query.Data.AddInput("Temporary marking", QueryValueKind.Bool);
            query.Data.SetOutput("Aliasing values", 0);
            query.Data.SetOutput("Mark aliasing values", "");

            var a = query.Data.AddButton("All");
            a.HasDemiBoldText = true;
            a.Action = (sender, data) => query.Data.Instance.Execute(query.Data);

            var b = query.Data.AddButton("Block");
            b.Action = (sender, data) => MessageBox.Show("Test");

            var c = query.Data.AddButton("Loop");
            c.Action = (sender, data) => MessageBox.Show("Test");

            var d = query.Data.AddButton("Loop nest");
            d.Action = (sender, data) => MessageBox.Show("Test");

            return query;
        }

        public ISession Session { get; private set; }

        public bool Initialize(ISession session) {
            Session = session;
            return true;
        }

        public bool Execute(QueryData data) {
            var elementA = data.GetInput<IRElement>(0);
            var func = elementA.ParentFunction;
            int pas = data.GetInput<int>(1);

            var interfTag = func.GetTag<InterferenceTag>();
            var refFinder = new ReferenceFinder(func);

            if (interfTag != null) {
                if (interfTag.InterferingPasMap.TryGetValue(pas, out var interPasses)) {
                    foreach (var interfPas in interPasses) {
                        if (interfTag.PasToSymMap.TryGetValue(interfPas, out var interfSymbols)) {
                            foreach (var interfSymbol in interfSymbols) {
                                //? TODO: Implement basic SymbolTable
                                MarkAllSymbols(func, interfSymbol);
                            }
                        }

                        //? TODO: A pass can also mark indirs
                    }
                }
            }

            data.ResetResults();
            data.SetOutput("Aliasing values", 0);
            return true;
        }

        private void MarkAllSymbols(FunctionIR func, string interfSymbol) {
            foreach (var elem in func.AllElements) {
                if (elem is OperandIR op && op.IsVariable && op.HasName &&
                    op.NameValue.ToString() == interfSymbol) {
                    var document = Session.CurrentDocument;
                    document.MarkElement(elem, Colors.YellowGreen);
                }
            }
        }
    }

    class UTCBuiltinInterferenceQuery : IElementQuery {
        public static QueryDefinition GetDefinition() {
            var query = new QueryDefinition(typeof(UTCBuiltinInterferenceQuery),
                                                   "Alias query",
                                                   "Alias query results for two values");
            query.Data.AddInput("Operand 1", QueryValueKind.Element);
            query.Data.AddInput("Operand 2", QueryValueKind.Element);
            query.Data.AddOutput("May Alias", QueryValueKind.Bool);
            return query;
        }

        public ISession Session { get; private set; }

        public bool Initialize(ISession session) {
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
