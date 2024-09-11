// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.UI.Query.Builtin;

public class OperandSSAInfoQuery : IElementQuery {
  private ISession session_;
  public ISession Session => session_;

  public bool Initialize(ISession session) {
    session_ = session;
    return true;
  }

  public bool Execute(QueryData data) {
    var element = data.GetInput<IRElement>("Operand");
    data.ResetResults();

    if (element is InstructionIR instr) {
      if (instr.Destinations.Count > 0) {
        element = instr.Destinations[0];
      }
    }

    if (element is OperandIR op) {
      var defOp = ReferenceFinder.GetSSADefinition(op);

      if (defOp == null) {
        data.SetOutputWarning("User Count",
                              $"Definition for {Utils.MakeElementDescription(op)} could not be found!");

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

        data.SetOutputWarning("Definition Dominates",
                              $"Definition {Utils.MakeElementDescription(defOp)} does not dominate!");
      }
    }
    else {
      data.SetOutputWarning("Operand", "Selected element is not an operand!");
    }

    return true;
  }

  public static QueryDefinition GetDefinition() {
    var query = new QueryDefinition(typeof(OperandSSAInfoQuery),
                                    "Operand SSA details",
                                    "Details about values with SSA info");
    query.Data.AddInput("Operand", QueryValueKind.Element);
    query.Data.AddOutput("User Count", QueryValueKind.Number);
    return query;
  }
}