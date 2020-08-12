﻿using System.Collections.Generic;
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
            query.Data.AddInput("Temporary marking", QueryValueKind.Bool);
            query.Data.AddOutput("Value number", QueryValueKind.String);
            query.Data.AddOutput("Same value number", QueryValueKind.Number);
            return query;
        }

        public ISessionManager Session { get; private set; }

        public bool Initialize(ISessionManager session) {
            Session = session;
            return true;
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
