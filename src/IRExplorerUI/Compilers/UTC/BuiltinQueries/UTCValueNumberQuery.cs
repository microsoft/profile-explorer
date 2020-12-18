using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using IRExplorerUI.Query;
using IRExplorerUI.UTC;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.UTC {
    public class UTCValueNumberQuery : IElementQuery {
        public static QueryDefinition GetDefinition() {
            var query = new QueryDefinition(typeof(UTCValueNumberQuery), "Value Numbers",
                                                  "Details about values with SSA info");
            query.Data.AddInput("Operand", QueryValueKind.Element);
            query.Data.AddInput("Consider only dominated values", QueryValueKind.Bool);
            query.Data.AddInput("Marking color", QueryValueKind.Color);
            query.Data.AddOutput("Value number", QueryValueKind.String);
            return query;
        }

        public ISession Session { get; private set; }

        public bool Initialize(ISession session) {
            Session = session;
            return true;
        }

        public bool Execute(QueryData data) {
            data.ResetResults();
            var element = data.GetInput<IRElement>("Operand");

            if (element == null) {
                data.SetOutputWarning("No IR element selected", "Select an IR element on which to run the query");
                return false;
            }

            string vn = UTCRemarkParser.ExtractVN(element);

            if (vn == null) {
                return true;
            }

            var func = element.ParentFunction;
            var sameVNInstrs = new HashSet<InstructionIR>();

            func.ForEachInstruction(instr => {
                string instrVN = UTCRemarkParser.ExtractVN(instr);

                if (instrVN == vn) {
                    sameVNInstrs.Add(instr);
                }

                return true;
            });

            data.SetOutput("Value number", vn);
            data.SetOutput("Instrs. with same value number", sameVNInstrs.Count);
            data.ClearButtons();

            if (sameVNInstrs.Count > 0) {
                data.AddButton("Mark same value number instrs.", (sender, data) => {
                    //? TODO: Check for document/function still being the same
                    var document = Session.CurrentDocument;

                    foreach (var instr in sameVNInstrs) {
                        document.MarkElement(instr, Colors.YellowGreen);
                    }
                });
            }

            return true;
        }
    }
}
