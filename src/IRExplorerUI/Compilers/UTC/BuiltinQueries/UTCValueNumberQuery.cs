using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using IRExplorerUI.Query;
using IRExplorerUI.UTC;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.UTC {
    public class UTCValueNumberQuery : IElementQuery {
        public static ElementQueryDefinition GetDefinition() {
            var query = new ElementQueryDefinition(typeof(UTCValueNumberQuery), "Value Numbers",
                                                   "Details about values with SSA info");
            query.Data.AddInput("Operand", QueryValueKind.Element);
            query.Data.AddInput("Mark same value number", QueryValueKind.Bool);
            query.Data.AddOutput("Value number", QueryValueKind.String);
            query.Data.AddOutput("Same value number", QueryValueKind.Number);
            return query;
        }

        public bool Execute(QueryData data) {
            data.ResetResults();
            var element = data.GetInput<IRElement>("Operand");
            bool markSameVN = data.GetInput<bool>("Mark same value number");
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
            data.SetOutput("Same value number", sameVNInstrs.Count);
            var session = Application.Current.MainWindow as ISessionManager;
            var document = session.CurrentDocument;

            if (markSameVN) {
                foreach (var instr in sameVNInstrs) {
                    document.MarkElement(instr, Colors.YellowGreen);
                }
            }

            return true;
        }
    }
}
