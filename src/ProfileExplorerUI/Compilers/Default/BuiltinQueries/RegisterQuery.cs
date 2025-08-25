// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows.Media;
using ProfileExplorerCore2.IR;
using ProfileExplorer.UI.Query;
using ProfileExplorerCore2.IR.Tags;
using ProfileExplorerCore2.Session;

namespace ProfileExplorer.UI.Compilers.Default;

public class RegisterQuery : IElementQuery {
  public IUISession Session { get; private set; }

  public bool Initialize(IUISession session) {
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
}