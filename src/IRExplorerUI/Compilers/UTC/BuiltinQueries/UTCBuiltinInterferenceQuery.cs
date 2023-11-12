// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;
using IRExplorerUI.Query;

namespace IRExplorerUI.Compilers.UTC;

class UTCBuiltinInterferenceQuery : IElementQuery {
  private int aliasedValues_;
  private int aliasedIndirectValues_;

  private enum MarkingScope {
    All,
    Block,
    Loop,
    LoopNest
  }

  public ISession Session { get; private set; }

  public static void CreateInterferenceTag(FunctionIR function, IRTextSection section,
                                           ISession session) {
    //? TODO: Reuse tags for the same IRTextFunction, they don't reference elements
    if (function.HasTag<InterferenceTag>()) {
      return;
    }

    var interfSections = section.ParentFunction.
      FindAllSections("Tuples after Build Interferences");

    if (interfSections.Count == 0) {
      return;
    }

    var interfSection = interfSections[0];
    var textLines =
      session.GetSectionOutputTextLinesAsync(interfSection.OutputBefore, interfSection).Result; //? TODO: await

    var tag = function.GetOrAddTag<InterferenceTag>();
    bool seenInterferingPas = false;

    foreach (string line in textLines) {
      var symPasMatch = Regex.Match(line, @"(\d+):(.*)");

      if (symPasMatch.Success && !seenInterferingPas) {
        int pas = int.Parse(symPasMatch.Groups[1].Value);
        var interferingSyms = new List<string>();
        string other = symPasMatch.Groups[2].Value;

        other = other.Replace("<Unknown Mem>", "");
        other = other.Replace("<Untrackable locals:", "");
        other = other.Replace(">", "");

        string[] symbols = other.Split(" ", StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < symbols.Length; i++) {
          string symbolName = symbols[i];
          interferingSyms.Add(symbolName);

          if (!tag.SymToPasMap.ContainsKey(symbolName)) {
            tag.SymToPasMap[symbolName] = pas;
          }
        }

        tag.PasToSymMap[pas] = interferingSyms;
      }
      else {
        var interferingIndicesMatch = Regex.Match(line, @"(\d+) interferes with: \{ ((\d+)\s)+");

        if (interferingIndicesMatch.Success) {
          seenInterferingPas = true;
          var interferingPAS = new HashSet<int>();
          int basePAS = int.Parse(interferingIndicesMatch.Groups[1].Value);

          foreach (Capture capture in interferingIndicesMatch.Groups[2].Captures) {
            interferingPAS.Add(int.Parse(capture.Value));
          }

          if (interferingPAS.Count > 0) {
            tag.InterferingPasMap[basePAS] = interferingPAS;
          }
        }
      }
    }
  }

  //? TODO: Change definition to use an options struct with reflection
  //? like UnusedInstructionsTaskOptions
  public static QueryDefinition GetDefinition() {
    var query = new QueryDefinition(typeof(UTCBuiltinInterferenceQuery),
                                    "Alias marking",
                                    "Alias query results for two values");
    query.Data.AddInput("Operand", QueryValueKind.Element);
    //? TODO: Implement reacheability
    // query.Data.AddInput("Mark only reaching", QueryValueKind.Bool, false,
    //     "Mark only aliasing values that can reach the query block");
    // query.Data.AddInput("Mark only reachable", QueryValueKind.Bool, false,
    //     "Mark only aliasing values that are reachable from the query block");
    query.Data.AddInput("Temporary marking", QueryValueKind.Bool, true);
    query.Data.AddInput("Show arrows", QueryValueKind.Bool, false);

    var a = query.Data.AddButton("All");
    a.HasMediumText = true;
    a.Action = (sender, data) =>
      ((UTCBuiltinInterferenceQuery)query.Data.Instance).Execute(query.Data, MarkingScope.All);

    var b = query.Data.AddButton("Block");
    b.Action = (sender, data) =>
      ((UTCBuiltinInterferenceQuery)query.Data.Instance).Execute(query.Data, MarkingScope.Block);

    var c = query.Data.AddButton("Loop");
    c.Action = (sender, data) =>
      ((UTCBuiltinInterferenceQuery)query.Data.Instance).Execute(query.Data, MarkingScope.Loop);

    var d = query.Data.AddButton("Loop nest");
    d.Action = (sender, data) =>
      ((UTCBuiltinInterferenceQuery)query.Data.Instance).Execute(query.Data, MarkingScope.LoopNest);

    return query;
  }

  public bool Initialize(ISession session) {
    Session = session;
    return true;
  }

  public bool Execute(QueryData data) {
    return Execute(data, MarkingScope.All);
  }

  private bool Execute(QueryData data, MarkingScope markingScope) {
    var element = data.GetInput<IRElement>(0);
    bool onlyReaching = data.GetInput<bool>(1);
    bool onlyReachable = data.GetInput<bool>(2);
    bool isTemporary = data.GetInput<bool>(1);
    bool showArrows = data.GetInput<bool>(2);
    var func = element.ParentFunction;
    int pas = -1;

    data.ResetResults();
    var interfTag = func.GetTag<InterferenceTag>();

    if (interfTag == null) {
      data.SetOutputWarning("No interference info found", "Use -d2dbINTERF,ITFPAS to output interf logs");
      return false;
    }

    if (!(element is OperandIR op)) {
      // For calls, PAS is on the instr. itself.
      if (element is InstructionIR instr) {
        var pasTag = instr.GetTag<PointsAtSetTag>();

        if (pasTag != null) {
          pas = pasTag.Pas;
          data.SetOutput("Query PAS", pas);
        }
      }

      if (pas == -1) {
        data.SetOutputWarning("Invalid IR element", "Selected IR element is not an aliased operand");
        return false;
      }
    }
    else if (op.IsIndirection) {
      var pasTag = element.GetTag<PointsAtSetTag>();

      if (pasTag != null) {
        pas = pasTag.Pas;
        data.SetOutput("Query PAS", pas);
      }
      else {
        data.SetOutputWarning("Indirection has no PAS", "Selected Indirection operand has no PAS info");
        return false;
      }
    }
    else if ((op.IsVariable || op.IsAddress) && op.HasName) {
      if (!interfTag.SymToPasMap.TryGetValue(op.Name, out pas)) {
        data.SetOutputWarning("Unaliased variable", "Selected variable is not an aliased operand");
        return false;
      }

      data.SetOutput("Query PAS", pas);
    }
    else {
      data.SetOutputWarning("Unaliased IR element", "Selected IR element is not an aliased operand");
      return false;
    }

    aliasedValues_ = 0;
    aliasedIndirectValues_ = 0;
    var block = element.ParentBlock;
    var document = Session.CurrentDocument;

    var highlightingType = isTemporary ? HighlighingType.Selected : HighlighingType.Marked;
    document.BeginMarkElementAppend(highlightingType);
    document.ClearConnectedElements();

    if (showArrows) {
      document.SetRootConnectedElement(element, new HighlightingStyle(Colors.Blue), isTemporary);
    }

    if (interfTag.InterferingPasMap.TryGetValue(pas, out var interPasses)) {
      foreach (int interfPas in interPasses) {
        // Mark all symbols.
        if (interfTag.PasToSymMap.TryGetValue(interfPas, out var interfSymbols)) {
          foreach (string interfSymbol in interfSymbols) {
            MarkAllSymbols(func, interfSymbol, block,
                           markingScope, highlightingType, showArrows);
          }
        }

        // Mark all indirections and calls.
        MarkAllIndirections(func, interfPas, block,
                            markingScope, highlightingType, showArrows);
      }
    }

    document.EndMarkElementAppend(highlightingType);
    data.SetOutput("Aliasing values", aliasedValues_);

    if (aliasedValues_ > 0) {
      data.SetOutput("Aliasing indirect values", aliasedIndirectValues_);
    }

    return true;
  }

  private void MarkAllSymbols(FunctionIR func, string interfSymbol, BlockIR queryBlock,
                              MarkingScope markingScope, HighlighingType highlightingType, bool showArrows) {
    var instrStyle = new HighlightingStyle(Brushes.Transparent, ColorPens.GetPen(Colors.Gray));

    foreach (var elem in func.AllElements) {
      if (elem is OperandIR op &&
          op.IsVariable && op.HasName &&
          op.Name == interfSymbol) {
        if (ShouldMarkElement(op, markingScope, queryBlock)) {
          MarkElement(op, instrStyle, highlightingType, showArrows);
          aliasedValues_++;
        }
      }
    }
  }

  private void MarkAllIndirections(FunctionIR func, int interfPas, BlockIR queryBlock,
                                   MarkingScope markingScope, HighlighingType highlightingType, bool showArrows) {
    var document = Session.CurrentDocument;
    var instrStyle = new HighlightingStyle(Brushes.Transparent, ColorPens.GetBoldPen(Colors.Gray));

    foreach (var element in func.AllElements) {
      if (!(element is OperandIR op)) {
        continue;
      }

      var pasTag = op.GetTag<PointsAtSetTag>();

      if (pasTag != null && pasTag.Pas == interfPas) {
        if (ShouldMarkElement(op, markingScope, queryBlock)) {
          MarkElement(op, instrStyle, highlightingType, showArrows);
          aliasedValues_++;
          aliasedIndirectValues_++;
        }
      }
    }
  }

  private void MarkElement(OperandIR op, HighlightingStyle instrStyle, HighlighingType highlightingType,
                           bool showArrows) {
    var document = Session.CurrentDocument;

    if (op.IsDestinationOperand) {
      document.MarkElementAppend(op, Colors.Pink, highlightingType);
    }
    else {
      //? TODO: Customize color, and at least don't re-parse a string each time
      document.MarkElementAppend(op, Utils.ColorFromString("#AEA9FC"), highlightingType);
    }

    document.MarkElementAppend(op.ParentTuple, instrStyle, highlightingType, false);

    if (showArrows) {
      var style = op.IsDestinationOperand
        ? new HighlightingStyle(Colors.DarkRed, ColorPens.GetDashedPen(Colors.DarkRed, DashStyles.Dash, 1.5)) :
        new HighlightingStyle(Colors.DarkBlue, ColorPens.GetDashedPen(Colors.DarkBlue, DashStyles.Dash, 1.5));
      document.AddConnectedElement(op, style);
    }
  }

  private bool ShouldMarkElement(OperandIR op, MarkingScope markingScope, BlockIR queryBlock) {
    switch (markingScope) {
      case MarkingScope.All: {
        return true;
      }
      case MarkingScope.Block: {
        return op.ParentBlock == queryBlock;
      }
      case MarkingScope.Loop: {
        return AreBlocksInSameLoop(op.ParentBlock, queryBlock, false);
      }
      case MarkingScope.LoopNest: {
        return AreBlocksInSameLoop(op.ParentBlock, queryBlock, true);
      }
    }

    return false;
  }

  private bool AreBlocksInSameLoop(BlockIR blockA, BlockIR blockB, bool checkLoopNest) {
    var tagA = blockA.GetTag<LoopBlockTag>();
    var tagB = blockB.GetTag<LoopBlockTag>();

    if (tagA != null && tagB != null) {
      if (checkLoopNest) {
        return tagA.Loop.LoopNestRoot == tagB.Loop.LoopNestRoot;
      }

      return tagA.Loop == tagB.Loop;
    }

    return false;
  }
}
