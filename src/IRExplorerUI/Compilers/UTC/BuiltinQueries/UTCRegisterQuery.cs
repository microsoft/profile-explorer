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
                                                   "Details about post-lower registers");
            query.Data.AddInput("Operand", QueryValueKind.Element);
            query.Data.AddInput("Consider overlapping registers", QueryValueKind.Bool, true);
            query.Data.AddInput("Use temporary marking", QueryValueKind.Bool, true);
            query.Data.AddInput("Marking color", QueryValueKind.Color, Colors.Pink);
            return query;
        }

        public ISession Session { get; private set; }

        public bool Initialize(ISession session) {
            Session = session;
            return true;
        }

        public bool Execute(QueryData data) {
            data.ResetResults();
            var element = data.GetInput<IRElement>(0);
            var considerOverlapping = data.GetInput<bool>(1);
            var isTemporary = data.GetInput<bool>(2);
            var color = data.GetInput<Color>(3);
            var func = element.ParentFunction;

            // Pick the query register.
            RegisterTag tag = GetRegisterTag(element);

            if (tag == null) {
                data.SetOutputWarning("Value has no register");
                return true;
            }

            int count = 0;
            var document = Session.CurrentDocument;

            var highlightingType = isTemporary ? HighlighingType.Selected : HighlighingType.Marked;
            document.BeginMarkElementAppend(highlightingType);

            foreach (var operand in func.AllElements) {
                var otherTag = GetRegisterTag(operand);

                if (otherTag == null) {
                    continue;
                }

                if (otherTag.Register.Equals(tag.Register) ||
                   (considerOverlapping && otherTag.Register.OverlapsWith(tag.Register))) {
                    document.MarkElementAppend(operand, color, highlightingType, true);
                    count++;
                }
            }

            document.EndMarkElementAppend(highlightingType);
            data.SetOutput("Register instances", count);
            data.ClearButtons();
            return true;
        }

        private static RegisterTag GetRegisterTag(IRElement element) {
            // For indirection, use the base value register.
            if (element is OperandIR op && op.IsIndirection) {
                return op.IndirectionBaseValue.GetTag<RegisterTag>();
            }
            else {
                return element.GetTag<RegisterTag>();
            }
        }
    }
}
