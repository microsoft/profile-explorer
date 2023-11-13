// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows.Media;
using IRExplorerCore.IR;
using IRExplorerUI.Query;

namespace IRExplorerUI.Compilers.Default;

public class RegisterQuery : IElementQuery {
  public ISession Session { get; private set; }

  public static QueryDefinition GetDefinition() {
    var query = new QueryDefinition(typeof(RegisterQuery), "Registers",
                                    "Details about post-lower registers");
    query.Data.AddInput("Operand", QueryValueKind.Element);
    query.Data.AddInput("Consider overlapping registers", QueryValueKind.Bool, true);
    query.Data.AddInput("Use temporary marking", QueryValueKind.Bool, true);
    query.Data.AddInput("Marking color", QueryValueKind.Color, Colors.Pink);
    return query;
  }

  private static RegisterTag GetRegisterTag(IRElement element) {
    // For indirection, use the base value register.
    if (element is OperandIR op && op.IsIndirection) {
      return op.IndirectionBaseValue.GetTag<RegisterTag>();
    }

    return element.GetTag<RegisterTag>();
  }

  public bool Initialize(ISession session) {
    Session = session;
    return true;
  }

  public bool Execute(QueryData data) {
    data.ResetResults();
    var element = data.GetInput<IRElement>(0);
    bool considerOverlapping = data.GetInput<bool>(1);
    bool isTemporary = data.GetInput<bool>(2);
    var color = data.GetInput<Color>(3);
    var func = element.ParentFunction;

    // Pick the query register.
    var tag = GetRegisterTag(element);

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
          considerOverlapping && otherTag.Register.OverlapsWith(tag.Register)) {
        document.MarkElementAppend(operand, color, highlightingType);
        count++;
      }
    }

    document.EndMarkElementAppend(highlightingType);
    data.SetOutput("Register instances", count);
    data.ClearButtons();
    return true;
  }
}
