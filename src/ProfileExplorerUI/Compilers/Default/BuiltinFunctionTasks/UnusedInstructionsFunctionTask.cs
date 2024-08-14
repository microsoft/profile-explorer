// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Query;

namespace ProfileExplorer.UI.Compilers.Default;

class UnusedInstructionsTaskOptions : IFunctionTaskOptions {
  public UnusedInstructionsTaskOptions() {
    Reset();
  }

  [DisplayName("Consider only SSA values")]
  [Description("Consider only instructions that have a destination operand in SSA form")]
  public bool HandleOnlySSA { get; set; }
  [DisplayName("Marker color")]
  [Description("Color to be used for marking unused instructions")]
  public Color MarkerColor { get; set; }

  public void Reset() {
    HandleOnlySSA = true;
    MarkerColor = Colors.Pink;
  }
}

class UnusedInstructionsFunctionTask {
  public static bool MarkUnusedInstructions(FunctionIR function, IRDocument document, IFunctionTaskOptions options,
                                            ISession session, CancelableTask cancelableTask) {
    var taskOptions = options as UnusedInstructionsTaskOptions;
    var unusedInstr = new HashSet<InstructionIR>();
    var walker = new CFGBlockOrdering(function);

    walker.PostorderWalk((block, index) => {
      foreach (var instr in block.InstructionsBack) {
        if (IsUnusedInstruction(GetSSADefinitionTag(instr), unusedInstr)) {
          document.Dispatcher.BeginInvoke((Action)(() => {
            document.MarkElement(instr, taskOptions.MarkerColor);
          }));

          unusedInstr.Add(instr);
        }
      }

      return !cancelableTask.IsCanceled;
    });

    return true;
  }

  //? TODO: Extract this into it's own class
  //? Add hooks for quierying IR if an instr is DCE candidate (reject calls for ex)
  private static SSADefinitionTag GetSSADefinitionTag(InstructionIR instr) {
    if (instr.Destinations.Count == 0) {
      return null;
    }

    var destOp = instr.Destinations[0];

    if (destOp.IsTemporary) {
      return destOp.GetTag<SSADefinitionTag>();
    }

    return null;
  }

  private static bool IsUnusedInstruction(SSADefinitionTag ssaDefTag, HashSet<InstructionIR> unusedInstrs) {
    if (ssaDefTag == null) {
      return false;
    }

    if (!ssaDefTag.HasUsers) {
      return true;
    }

    foreach (var user in ssaDefTag.Users) {
      if (!unusedInstrs.Contains(user.OwnerInstruction)) {
        return false;
      }
    }

    return true;
  }
}