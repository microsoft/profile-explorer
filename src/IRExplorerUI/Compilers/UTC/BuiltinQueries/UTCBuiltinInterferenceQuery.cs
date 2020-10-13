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
            query.Data.AddInput("Temporary marking", QueryValueKind.Bool, true);
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
            var element = data.GetInput<IRElement>(0);
            var isTemporary = data.GetInput<bool>(1);
            var func = element.ParentFunction;
            int pas = 0;

            data.ResetResults();
            var interfTag = func.GetTag<InterferenceTag>();

            if (interfTag == null) {
                data.SetOutputWarning("No interference info found", "Use -d2dbINTERF,ITFPAS to output interf logs");
                return false;
            }

            if (!(element is OperandIR op)) {
                data.SetOutputWarning("Invalid IR element", "Selected IR element is not an aliased operand");
                return false;
            }

            if (op.IsIndirection) {
                var pasTag = element.GetTag<PointsAtSetTag>();

                if (pasTag != null) {
                    pas = pasTag.Pas;
                    data.SetOutput("Query PAS", pas);
                }
                else {
                    data.SetOutputWarning("Indirection has no PAS", "Selected Indirection operand has no PAS info");
                    return false;
                }
            }
            else if (op.IsVariable && op.HasName) {
                if (!interfTag.SymToPasMap.TryGetValue(op.Name, out pas)) {
                    data.SetOutputWarning("Unaliased variable", "Selected variable is not an aliased operand");
                    return false;
                }
            }
            else {
                data.SetOutputWarning("Unaliased IR element", "Selected IR element is not an aliased operand");
                return false;
            }


            var refFinder = new ReferenceFinder(func);
            var document = Session.CurrentDocument;
            var highlightingType = isTemporary ? HighlighingType.Selected : HighlighingType.Marked;
            document.BeginMarkElementAppend(highlightingType);
            document.SetRootElement(element);

            if (interfTag.InterferingPasMap.TryGetValue(pas, out var interPasses)) {
                foreach (var interfPas in interPasses) {
                    // Mark all symbols.
                    if (interfTag.PasToSymMap.TryGetValue(interfPas, out var interfSymbols)) {
                        foreach (var interfSymbol in interfSymbols) {
                            MarkAllSymbols(func, interfSymbol, highlightingType);
                        }
                    }

                    // Mark all indirections and calls.
                    MarkAllIndirections(func, interfPas, highlightingType);
                }
            }

            document.EndMarkElementAppend(highlightingType);
            //data.SetOutput("Aliasing values", 0);
            return true;
        }

        private void MarkAllSymbols(FunctionIR func, string interfSymbol, HighlighingType highlightingType) {
            var document = Session.CurrentDocument;
            var style = new HighlightingStyle(Brushes.Transparent, Pens.GetPen(Colors.Silver));

            foreach (var elem in func.AllElements) {
                if (elem is OperandIR op && op.IsVariable && op.HasName &&
                    op.NameValue.ToString() == interfSymbol) {


                    if (op.IsDestinationOperand) {
                        document.MarkElementAppend(op, Colors.Pink, highlightingType, false);
                    }
                    else {
                        document.MarkElementAppend(op, Utils.ColorFromString("#AEA9FC"), highlightingType, false);
                    }

                    document.MarkElementAppend(op.ParentTuple, style, highlightingType, false);
                    document.AddConnectedElement(op);
                }
            }
        }

        private void MarkAllIndirections(FunctionIR func, int interfPas, HighlighingType highlightingType) {
            var document = Session.CurrentDocument;
            var style = new HighlightingStyle(Brushes.Transparent, Pens.GetBoldPen(Colors.Black));

            foreach (var element in func.AllElements) {
                if (!(element is OperandIR op)) {
                    continue;
                }

                var pasTag = op.GetTag<PointsAtSetTag>();

                if (pasTag != null && pasTag.Pas == interfPas) {
                    document.MarkElementAppend(op.ParentTuple, style, highlightingType, false);

                    if (op.IsDestinationOperand) {
                        document.MarkElementAppend(op, Colors.Pink, highlightingType, false);
                    }
                    else {
                        document.MarkElementAppend(op, Utils.ColorFromString("#AEA9FC"), highlightingType, false);
                    }

                    document.MarkElementAppend(op.ParentTuple, style, highlightingType, false);
                    document.AddConnectedElement(op);
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
