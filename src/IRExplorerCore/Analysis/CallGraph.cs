// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis;

[Flags]
public enum CallGraphNodeFlags {
  None = 0,
  Internal = 1 << 0,
  External = 1 << 1,
  AddressTaken = 1 << 2
}

public class CallSite : TaggedObject {
  public CallSite(ulong callInstrId, CallGraphNode target, CallGraphNode source) {
    CallInstructionId = callInstrId;
    Target = target;
    Source = source;
  }

  public CallSite(InstructionIR callInstr, CallGraphNode target, CallGraphNode parent) :
    this(callInstr.Id, target, parent) {
  }

  public ulong CallInstructionId { get; set; }
  public CallGraphNode Target { get; set; }
  public CallGraphNode Source { get; set; }

  public static bool operator ==(CallSite left, CallSite right) {
    return EqualityComparer<CallSite>.Default.Equals(left, right);
  }

  public static bool operator !=(CallSite left, CallSite right) {
    return !(left == right);
  }

  public override bool Equals(object obj) {
    return obj is CallSite site &&
           CallInstructionId == site.CallInstructionId &&
           EqualityComparer<CallGraphNode>.Default.Equals(Target, site.Target) &&
           EqualityComparer<CallGraphNode>.Default.Equals(Source, site.Source);
  }

  public override int GetHashCode() {
    return HashCode.Combine(CallInstructionId, Target, Source);
  }
}

public class CallGraphNode : TaggedObject {
  public CallGraphNode(IRTextFunction function, string funcName, int number, CallGraphNodeFlags flags) {
    Function = function;
    FunctionName = funcName;
    Number = number;
    Flags = flags;
  }

  public IRTextFunction Function { get; set; }
  public string FunctionName { get; set; }
  public int Number { get; set; }
  public List<CallSite> Callers { get; set; }
  public List<CallSite> Callees { get; set; }
  public CallGraphNodeFlags Flags { get; set; }
  public bool HasKnownTarget => Function != null;
  public bool HasCallers => Callers != null && Callers.Count > 0;
  public bool HasCallees => Callees != null && Callees.Count > 0;
  public bool IsLeafFunction => !HasCallees;
  public bool IsInternal => Flags.HasFlag(CallGraphNodeFlags.Internal);
  public bool IsExternal => Flags.HasFlag(CallGraphNodeFlags.External);
  public bool IsAddressTaken => Flags.HasFlag(CallGraphNodeFlags.AddressTaken);

  public IEnumerable<CallGraphNode> UniqueCallers {
    get {
      if (Callers != null) {
        var nodeSet = new HashSet<CallGraphNode>();

        foreach (var callsite in Callers) {
          if (nodeSet.Add(callsite.Source)) {
            yield return callsite.Source;
          }
        }
      }
    }
  }

  public IEnumerable<CallGraphNode> UniqueCallees {
    get {
      if (Callees != null) {
        var nodeSet = new HashSet<CallGraphNode>();

        foreach (var callsite in Callees) {
          if (nodeSet.Add(callsite.Target)) {
            yield return callsite.Target;
          }
        }
      }
    }
  }

  public int UniqueCallerCount {
    get {
      int count = 0;

      foreach (var node in UniqueCallers) {
        count++;
      }

      return count;
    }
  }

  public int UniqueCalleeCount {
    get {
      int count = 0;

      foreach (var node in UniqueCallees) {
        count++;
      }

      return count;
    }
  }

  public static bool operator ==(CallGraphNode left, CallGraphNode right) {
    return EqualityComparer<CallGraphNode>.Default.Equals(left, right);
  }

  public static bool operator !=(CallGraphNode left, CallGraphNode right) {
    return !(left == right);
  }

  public void AddCallee(CallSite callsite) {
    Callees ??= new List<CallSite>();
    Callees.Add(callsite);
  }

  public void AddCallee(CallGraphNode node) {
    AddCallee(new CallSite(0, node, this));
  }

  public void AddCaller(CallSite callsite) {
    Callers ??= new List<CallSite>();
    Callers.Add(callsite);
  }

  public CallGraphNode FindCallee(string name) {
    if (Callees == null) {
      return null;
    }

    var result = Callees.Find(c => c.Target != null && c.Target.FunctionName.Equals(name, StringComparison.Ordinal));
    return result?.Target;
  }

  public CallGraphNode FindCallee(IRTextFunction function) {
    if (Callees == null) {
      return null;
    }

    var result = Callees.Find(c => c.Target != null && c.Target.Function == function);
    return result?.Target;
  }

  public CallGraphNode FindCallee(CallGraphNode node) {
    return FindCallee(node.FunctionName);
  }

  public CallGraphNode FindCaller(IRTextFunction function) {
    if (Callers == null) {
      return null;
    }

    var result = Callers.Find(c => c.Source != null && c.Source.Function == function);
    return result?.Target;
  }

  public override bool Equals(object obj) {
    return obj is CallGraphNode node &&
           Number == node.Number &&
           FunctionName == node.FunctionName;
  }

  public override int GetHashCode() {
    return HashCode.Combine(Number);
  }
}

public class CallGraphEventArgs : EventArgs {
  public CallGraphEventArgs(IRTextFunction textFunction, FunctionIR function, CallGraphNode functionNode) {
    TextFunction = textFunction;
    Function = function;
    FunctionNode = functionNode;
  }

  public IRTextFunction TextFunction { get; set; }
  public FunctionIR Function { get; set; }
  public CallGraphNode FunctionNode { get; set; }
}

public class CallGraph {
  private List<CallGraphNode> nodes_;
  private List<CallGraphNode> entryNodes_;
  private Dictionary<IRTextFunction, CallGraphNode> funcToNodeMap_;
  private Dictionary<string, CallGraphNode> externalFuncToNodeMap_;
  private HashSet<IRTextFunction> visitedFuncts_;
  private IRTextSummary summary_;
  private IRTextSectionLoader loader_;
  private ICompilerIRInfo irInfo_;
  private int nextCallNodeId_;

  public CallGraph(IRTextSummary summary, IRTextSectionLoader loader, ICompilerIRInfo irInfo) {
    summary_ = summary;
    loader_ = loader;
    irInfo_ = irInfo;
    nodes_ = new List<CallGraphNode>(summary.Functions.Count);
    funcToNodeMap_ = new Dictionary<IRTextFunction, CallGraphNode>(summary.Functions.Count);
    externalFuncToNodeMap_ = new Dictionary<string, CallGraphNode>();
    visitedFuncts_ = new HashSet<IRTextFunction>();
    entryNodes_ = new List<CallGraphNode>();
  }

  public delegate bool CallNodeCallback(CallGraphNode node, CallGraphNode parentNode,
                                        CallGraph callGraph, List<IRTextFunction> targetFuncts);

  public event EventHandler<CallGraphEventArgs> CallGraphNodeCreated;
  public List<CallGraphNode> EntryFunctionNodes => entryNodes_;
  public List<CallGraphNode> FunctionNodes => nodes_;

  public void Execute(IRTextSection section = null, CancelableTask cancelableTask = null) {
    //? TODO: Can be multithreaded, most time spent parsing the functs
    foreach (var func in summary_.Functions) {
      if (visitedFuncts_.Contains(func)) {
        continue;
      }

      if (cancelableTask is {IsCanceled: true}) {
        return;
      }

      BuildCallSubgraph(func, section?.Name, cancelableTask);
    }

    // Find entry functions.
    foreach (var node in nodes_) {
      if (!node.HasCallers) {
        entryNodes_.Add(node);
      }
    }
  }

  public CallGraphNode Execute(IRTextFunction startFunction, IRTextSection section = null,
                               CancelableTask cancelableTask = null) {
    BuildCallSubgraph(startFunction, section?.Name, cancelableTask);
    return GetOrCreateNode(startFunction);
  }

  public CallGraphNode FindNode(IRTextFunction function) {
    if (funcToNodeMap_.TryGetValue(function, out var node)) {
      return node;
    }

    return null;
  }

  public CallGraphNode GetOrCreateNode(IRTextFunction func) {
    return GetOrCreateNode(func.Name);
  }

  public void AugmentGraph(CallNodeCallback augmentAction) {
    // Walk over a clone of the node list, since new nodes may be added
    // and would invalidate the iterator and assert.
    var listClone = new List<CallGraphNode>(nodes_.Count);
    listClone.AddRange(nodes_);

    foreach (var node in listClone) {
      if (node.Function != null) {
        augmentAction(node, node, this, null);
      }
    }
  }

  public void TrimGraph(List<IRTextFunction> targetFuncts,
                        CallNodeCallback callerFilter = null,
                        CallNodeCallback calleeFilter = null) {
    // Remove all nodes that don't lead to one of the target functions.
    var visitedNodes = new HashSet<CallGraphNode>();
    var worklist = new Queue<CallGraphNode>();

    foreach (var func in targetFuncts) {
      var node = FindNode(func);

      if (node != null) {
        worklist.Enqueue(node);
      }
    }

    while (worklist.Count != 0) {
      var node = worklist.Dequeue();
      visitedNodes.Add(node);

      if (node.HasCallers) {
        foreach (var caller in node.Callers) {
          if (callerFilter == null || callerFilter(caller.Source, node, this, targetFuncts)) {
            if (!visitedNodes.Contains(caller.Source)) {
              worklist.Enqueue(caller.Source);
            }
          }
        }
      }

      if (node.HasCallees) {
        foreach (var calee in node.Callees) {
          if (calleeFilter == null || calleeFilter(calee.Target, node, this, targetFuncts)) {
            visitedNodes.Add(calee.Target);
          }
        }
      }
    }

    nodes_.Clear();
    nodes_.AddRange(visitedNodes);
    var cleanedNodes = new HashSet<CallGraphNode>();

    foreach (var node in nodes_) {
      TrimNodes(node, visitedNodes, cleanedNodes);
    }

    entryNodes_.Clear();

    foreach (var node in nodes_) {
      if (!node.HasCallers) {
        entryNodes_.Add(node);
      }
    }
  }

  private void BuildCallSubgraph(IRTextFunction startFunction, string sectionName, CancelableTask cancelableTask) {
    var worklist = new Queue<IRTextFunction>();
    worklist.Enqueue(startFunction);
    visitedFuncts_.Add(startFunction);

    while (worklist.Count > 0) {
      if (cancelableTask is {IsCanceled: true}) {
        break;
      }

      var func = worklist.Dequeue();
      var funcNode = GetOrCreateNode(func);

      if (funcNode == null) {
        continue; // An unknown/external function, ignore.
      }

      var funcIR = LoadSection(func, sectionName);

      if (funcIR == null) {
        continue; // Failed to parse function, ignore.
      }

      // Notify client about the node being created and function IR being available,
      // can be used to add extra annotation tags on the node without having
      // to reparse the functions later.
      CallGraphNodeCreated?.Invoke(this, new CallGraphEventArgs(func, funcIR, funcNode));

      foreach (var instr in funcIR.AllInstructions) {
        if (irInfo_.IsCallInstruction(instr)) {
          var callTarget = irInfo_.GetCallTarget(instr);

          if (callTarget != null) {
            // Extract target name.
            string calleeFuncName = "[INDIRECT]";

            if (callTarget.HasName) {
              calleeFuncName = callTarget.Name;
            }
            else if (callTarget.IsIntConstant) {
              calleeFuncName = $"0x{callTarget.IntValue:X}";
            }

            // Make node and enqueue it for the recursive processing.
            var calleeNode = GetOrCreateNode(calleeFuncName);

            var callsite = new CallSite(instr, calleeNode, funcNode);
            funcNode.AddCallee(callsite);
            calleeNode.AddCaller(callsite);

            // If the function has a definition, add it to the worklist.
            var calleeFunc = summary_.FindFunction(calleeFuncName);

            if (calleeFunc != null && visitedFuncts_.Add(calleeFunc)) {
              worklist.Enqueue(calleeFunc);
            }
          }
          //? TODO: Add a "Unknown target" node
        }
      }
    }
  }

  private void TrimNodes(CallGraphNode node, HashSet<CallGraphNode> visitedNodes,
                         HashSet<CallGraphNode> cleanedNodes) {
    cleanedNodes.Add(node);

    if (!node.HasCallees) {
      return;
    }

    for (int i = 0; i < node.Callees.Count; i++) {
      var calleeNode = node.Callees[i].Target;

      if (!visitedNodes.Contains(calleeNode)) {
        node.Callees.RemoveAt(i);
        i--;
      }
      else if (!cleanedNodes.Contains(calleeNode)) {
        TrimNodes(calleeNode, visitedNodes, cleanedNodes);
      }
    }
  }

  private CallGraphNode GetOrCreateNode(string funcName) {
    var func = summary_.FindFunction(funcName);

    if (func == null) {
      // Consider that it's an external function without definition.
      if (externalFuncToNodeMap_.TryGetValue(funcName, out var externalNode)) {
        return externalNode;
      }

      externalNode = new CallGraphNode(null, funcName, GetNextCallNodeId(),
                                       CallGraphNodeFlags.External);
      externalFuncToNodeMap_[funcName] = externalNode;
      nodes_.Add(externalNode);
      return externalNode;
    }

    if (funcToNodeMap_.TryGetValue(func, out var node)) {
      return node;
    }

    node = new CallGraphNode(func, funcName, GetNextCallNodeId(),
                             CallGraphNodeFlags.Internal);
    funcToNodeMap_[func] = node;
    nodes_.Add(node);
    return node;
  }

  private int GetNextCallNodeId() {
    return nextCallNodeId_++;
  }

  private FunctionIR LoadSection(IRTextFunction func, string sectionName) {
    var section = func.FindSection(sectionName);

    if (section == null) {
      if (func.SectionCount == 0) {
        return null;
      }

      section = func.Sections[0];
    }

    var loadedDoc = loader_.LoadSection(section);
    return loadedDoc?.Function;
  }
}
