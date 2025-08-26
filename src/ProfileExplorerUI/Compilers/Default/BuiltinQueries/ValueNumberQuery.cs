// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Windows.Media;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Query;

namespace ProfileExplorer.UI.Compilers.Default;

public class ValueNumberQuery : IElementQuery {
  private static readonly string ValueNumberPrefix = "vn ";
  public IUISession Session { get; private set; }

  public bool Initialize(IUISession session) {
    Session = session;
    return true;
  }

  public bool Execute(QueryData data) {
    data.ResetResults();
    var element = data.GetInput<IRElement>("Operand");
    string vn = DefaultRemarkParser.ExtractValueNumber(element, ValueNumberPrefix);

    if (vn == null) {
      return true;
    }

    var func = element.ParentFunction;
    var sameVNInstrs = new HashSet<InstructionIR>();

    func.ForEachInstruction(instr => {
      string instrVN = DefaultRemarkParser.ExtractValueNumber(instr, ValueNumberPrefix);

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

  public static QueryDefinition GetDefinition() {
    var query = new QueryDefinition(typeof(ValueNumberQuery), "Value Numbers",
                                    "Details about values with SSA info");
    query.Data.AddInput("Operand", QueryValueKind.Element);
    query.Data.AddInput("Consider only dominated values", QueryValueKind.Bool);
    query.Data.AddInput("Marking color", QueryValueKind.Color);
    query.Data.AddOutput("Value number", QueryValueKind.String);
    return query;
  }
}