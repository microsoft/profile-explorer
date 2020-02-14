using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Client.Scripting;
using Core.IR;
using Core.Analysis;
using System.ServiceModel.Channels;

namespace Client.Query {
    /// <summary>
    /// Interaction logic for QueryPanelPreview.xaml
    /// </summary>
    public partial class QueryPanelPreview : Window {
        public QueryPanelPreview() {
            InitializeComponent();

            var builtinQuery = new BuiltinElementQuery();
            //var query = new ElementQueryInfo(builtinQuery, "Test query", "Some description");
            //query.Data.AddInput("First", QueryValueKind.Element);
            //query.Data.AddInput("Second", QueryValueKind.Bool);
            //query.Data.AddInput("Third", QueryValueKind.Number);
            //query.Data.SetOutput("Output A", true, "Description A");
            //query.Data.SetOutput("Output B", false, "Description B");
            //query.Data.SetOutput("Output C", 123, "Description C");

            QPanel.AddQuery(new OperandSSAInfoQuery().GetDefinition());
            QPanel.AddQuery(new InstructionSSAInfoQuery().GetDefinition());
            QPanel.AddQuery(new ValueNumberQuery().GetDefinition());
            //var builtinQuery = new BuiltinElementQuery();
            //var query = new ElementQueryInfo(builtinQuery, "Test query", "Some description");
        }
    }

    public class ValueNumberQuery : IElementQuery {
        public ElementQueryInfo GetDefinition() {
            var query = new ElementQueryInfo(this, "Value Numbers", "Details about values with SSA info");
            query.Data.AddInput("Operand", QueryValueKind.Element);
            query.Data.AddInput("Mark same value number", QueryValueKind.Bool);
            query.Data.AddOutput("Value number", QueryValueKind.String);
            query.Data.AddOutput("Same value number", QueryValueKind.Number);
            return query;
        }

        public bool Execute(QueryData data) {
            data.ResetResults();
            var element = data.GetInput<IRElement>("Operand");
            var markSameVN = data.GetInput<bool>("Mark same value number");
            var vn = UTCRemarkParser.ExtractVN(element);

            if (vn == null) {
                return true;
            }

            var func = element.ParentFunction;
            var sameVNInstrs = new HashSet<InstructionIR>();

            func.ForEachInstruction((instr) => {
                var instrVN = UTCRemarkParser.ExtractVN(instr);

                if (instrVN == vn) {
                    sameVNInstrs.Add(instr);
                }
                return true;
            });

            data.SetOutput("Value number", vn);
            data.SetOutput("Same value number", sameVNInstrs.Count);

            var session = App.Current.MainWindow as ISessionManager;
            var document = session.CurrentDocument;

            if (markSameVN) {
                foreach (var instr in sameVNInstrs) {
                    document.MarkElement(instr, Colors.YellowGreen);
                }
            }

            return true;
        }

        
    }

    public class OperandSSAInfoQuery : IElementQuery {
        public ElementQueryInfo GetDefinition() {
            var query = new ElementQueryInfo(this, "Operand SSA details", "Details about values with SSA info");
            query.Data.AddInput("Operand", QueryValueKind.Element);
            query.Data.AddOutput("User Count", QueryValueKind.Number);
            //query.Data.AddOutput("Definition", QueryValueKind.Element);
            //query.Data.AddOutput("Definition Block", QueryValueKind.Element);
            //query.Data.AddOutput("Definition Dominates", QueryValueKind.Bool);
            return query;
        }

        public bool Execute(QueryData data) {
            var element = data.GetInput<IRElement>("Operand");
            data.ResetResults();

            if (element is OperandIR op) {
                var defOp = ReferenceFinder.GetSSADefinition(op);

                if (defOp == null) {
                    data.SetOutputWarning("User Count", $"Definition for {Utils.MakeElementDescription(op)} could not be found!");
                    return true;
                }

                var defTag = defOp.GetTag<SSADefinitionTag>();
                data.SetOutput("User Count", defTag.Users.Count);
                data.SetOutput("Definition", defOp);
                data.SetOutput("Definition Block", defOp.ParentBlock);

                var cache = FunctionAnalysisCache.Get(element.ParentFunction);
                var dominatorAlgo = cache.GetDominators();

                //? TODO: Dom inside a block by checking instr order
                if (dominatorAlgo.Dominates(defOp.ParentBlock, op.ParentBlock)) {
                    data.SetOutput("Definition Dominates", true);
                }
                else {
                    data.SetOutput("Definition Dominates", false);
                    data.SetOutputWarning("Definition Dominates", $"Definition {Utils.MakeElementDescription(defOp)} does not dominate!");
                }
            }
            else {
                data.SetOutputWarning("User Count", $"Selected element is not an operand!");
            }

            return true;
        }
    }

    public class InstructionSSAInfoQuery : IElementQuery {
        public ElementQueryInfo GetDefinition() {
            var query = new ElementQueryInfo(this, "Instruction SSA details", "Details about values with SSA info");
            query.Data.AddInput("Instruction", QueryValueKind.Element);
            query.Data.AddOutput("User Count", QueryValueKind.Number);
            query.Data.AddOutput("Source Definitions Dominate", QueryValueKind.Bool);
            return query;
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
                    if (defOp == null) continue;

                    var cache = FunctionAnalysisCache.Get(element.ParentFunction);
                    var dominatorAlgo = cache.GetDominators();

                    if (!dominatorAlgo.Dominates(defOp.ParentBlock, instr.ParentBlock)) {
                        allSourcesDominate = false;
                        data.SetOutput("Source Definitions Dominate", false);
                        data.SetOutputWarning("Source Definitions Dominate", $"Definition {Utils.MakeElementDescription(defOp)} of source {Utils.MakeElementDescription(sourceOp)} does not dominate!");
                        break;
                    }
                }

                if (allSourcesDominate) {
                    data.SetOutput("Source Definitions Dominate", true);
                }
            }
            else {
                data.SetInputWarning("Instruction", $"Selected element is not an instruction!");
            }

            return true;
        }
    }
}
