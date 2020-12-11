using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using IRExplorerUI.Query;
using IRExplorerUI.UTC;
using IRExplorerCore.IR;

namespace IRExplorerUI.Compilers.UTC {
    public class UTCRegisterQuery : IElementQuery {
        public static QueryDefinition GetDefinition() {
            var query = new QueryDefinition(typeof(UTCRegisterQuery), "Registers",
                                                   "Details about post-lower register info");
            query.Data.AddInput("Operand", QueryValueKind.Element);
            query.Data.AddInput("Consider overlapping registers", QueryValueKind.Bool);
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
            var func = element.ParentFunction;

            var tag = element.GetTag<RegisterTag>();

            if(tag == null) {
                data.SetOutputWarning("Value has no register");
                return true;
            }

            int count = 0;

            foreach(var operand in func.AllElements) {
                var otherTag = operand.GetTag<RegisterTag>();
                if(otherTag != null && otherTag.Register.OverlapsWith(tag.Register)) {
                    Session.CurrentDocument.MarkElement(operand, Colors.YellowGreen);
                    count++;
                }
            }

            data.SetOutput("Register instances", count);
            data.ClearButtons();

            //if (sameVNInstrs.Count > 0) {
            //    data.AddButton("Mark same value number instrs.", (sender, data) => {
            //        //? TODO: Check for document/function still being the same
            //        var document = Session.CurrentDocument;

            //        foreach (var instr in sameVNInstrs) {
            //            document.MarkElement(instr, Colors.YellowGreen);
            //        }
            //    });
            //}

            return true;
        }
    }
}
