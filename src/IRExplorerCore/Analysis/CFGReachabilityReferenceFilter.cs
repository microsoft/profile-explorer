// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis;

public class CFGReachabilityReferenceFilter : IReachableReferenceFilter {
  protected FunctionIR function_;
  protected CFGReachability cfgReachability_;

  public CFGReachabilityReferenceFilter(FunctionIR function) {
    function_ = function;
  }

  public bool FilterDefinitions { get; set; }
  public bool FilterUses { get; set; }

  public virtual bool AcceptDefinitionReference(IRElement element, IRElement startSourceElement) {
    if (!FilterDefinitions) {
      return false;
    }

    // Accept element if it's a definition that can reach the source element.
    var cfgReachability = GetReachabilityInfo();

    if (!cfgReachability.Reaches(element.ParentBlock, startSourceElement.ParentBlock)) {
      return false;
    }

    //? TODO: Use reaching definitions if available
    // If in the same block, accept it only if dest is found before the use,
    // or the block is found in a loop (value may reach through a backedge).
    if (startSourceElement.ParentBlock == element.ParentBlock) {
      if (element.ParentInstruction == null) {
        return true; // Parameter dominates everything.
      }

      if (startSourceElement.ParentInstruction == null) {
        return false;
      }

      int destIndex = element.ParentInstruction.IndexInBlock;
      int useIndex = startSourceElement.ParentInstruction.IndexInBlock;
      return destIndex < useIndex;
    }

    return true;
  }

  public virtual bool AcceptReference(IRElement element, IRElement startElement) {
    return true;
  }

  public virtual bool AcceptUseReference(IRElement element, IRElement startDestElement) {
    if (!FilterUses) {
      return false;
    }

    // Accept element if it can be reached from the dest. element.
    //? TODO: Use reaching definitions if available
    var cfgReachability = GetReachabilityInfo();

    if (!cfgReachability.Reaches(startDestElement.ParentBlock, element.ParentBlock)) {
      return false;
    }

    // If in the same block, accept it only if dest is found before the use,
    // or the block is found in a loop (value may reach through a backedge).
    if (startDestElement.ParentBlock == element.ParentBlock) {
      if (startDestElement.ParentInstruction == null) {
        return true; // Use dominated by parameter.
      }

      if (element.ParentInstruction == null) {
        return false;
      }

      int destIndex = startDestElement.ParentInstruction.IndexInBlock;
      int useIndex = element.ParentInstruction.IndexInBlock;
      return destIndex < useIndex;
    }

    return true;
  }

  private CFGReachability GetReachabilityInfo() {
    cfgReachability_ ??= FunctionAnalysisCache.Get(function_).GetReachability();
    return cfgReachability_;
  }
}
