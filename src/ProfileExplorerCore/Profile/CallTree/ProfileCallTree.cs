// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ProfileExplorerCore.Binary;
using ProfileExplorerCore.Profile.Data;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorerCore.Profile.CallTree;

public enum ProfileCallTreeNodeKind {
  Unset = 0,
  NativeUser = 1,
  NativeKernel = 2,
  Managed = 3
}

public sealed class ProfileCallTree {
  private ConcurrentDictionary<IRTextFunction, ProfileCallTreeNode> rootNodes_;
  private Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcToNodesMap_;
  private Dictionary<long, ProfileCallTreeNode> nodeIdMap_;
  private int nextNodeId_;

  public ProfileCallTree(int startId = 0) {
    nextNodeId_ = startId;
    InitializeReferenceMembers();
  }

  public List<ProfileCallTreeNode> RootNodes => rootNodes_.ToValueList();

  public TimeSpan TotalRootNodesWeight {
    get {
      var sum = TimeSpan.Zero;

      foreach (var node in rootNodes_) {
        sum += node.Value.Weight;
      }

      return sum;
    }
  }

  public void UpdateCallTree(ref ProfileSample sample, ResolvedProfileStack resolvedStack) {
    // Build call tree. Note that the call tree methods themselves are thread-safe.
    bool isRootFrame = true;
    ProfileCallTreeNode prevNode = null;
    ResolvedProfileStackFrame prevFrame = null;
    var sampleWeight = sample.Weight;

    for (int k = resolvedStack.FrameCount - 1; k >= 0; k--) {
      var resolvedFrame = resolvedStack.StackFrames[k];

      if (resolvedFrame.FrameRVA == 0 && resolvedFrame.FrameDetails.DebugInfo == null) {
        continue;
      }

      ProfileCallTreeNode node = null;

      if (isRootFrame) {
        node = AddRootNode(resolvedFrame.FrameDetails.DebugInfo, resolvedFrame.FrameDetails.Function);
        isRootFrame = false;
      }
      else {
        node = AddChildNode(prevNode, resolvedFrame.FrameDetails.DebugInfo, resolvedFrame.FrameDetails.Function);
        prevNode.AddCallSite(node, prevFrame.FrameRVA, sampleWeight);
      }

      node.AccumulateWeight(sampleWeight);
      node.AccumulateWeight(sampleWeight, TimeSpan.Zero, resolvedStack.Context.ThreadId);

      // Set the user/kernel-mode context of the function.
      if (node.Kind == ProfileCallTreeNodeKind.Unset) {
        if (resolvedFrame.FrameDetails.IsKernelCode) {
          node.Kind = ProfileCallTreeNodeKind.NativeKernel;
        }
        else if (resolvedFrame.FrameDetails.IsManagedCode) {
          node.Kind = ProfileCallTreeNodeKind.Managed;
        }
        else {
          node.Kind = ProfileCallTreeNodeKind.NativeUser;
        }
      }

      //node.RecordSample(sample, resolvedFrame); //? Remove
      prevNode = node;
      prevFrame = resolvedFrame;
    }

    // Last function on the stack gets the exclusive weight.
    if (prevNode != null) {
      prevNode.AccumulateExclusiveWeight(sampleWeight);
      prevNode.AccumulateWeight(TimeSpan.Zero, sampleWeight, resolvedStack.Context.ThreadId);
    }
  }

  private ProfileCallTreeNode AddRootNode(FunctionDebugInfo funcInfo, IRTextFunction function) {
    if (rootNodes_.TryGetValue(function, out var existingNode)) {
      return existingNode;
    }

    var node = rootNodes_.GetOrAdd(function, static (func, info) => new ProfileCallTreeNode(info, func), funcInfo);
    RegisterFunctionTreeNode(node);
    return node;
  }

  private ProfileCallTreeNode AddChildNode(ProfileCallTreeNode node, FunctionDebugInfo funcInfo,
                                           IRTextFunction function) {
    (var childNode, bool isNewNode) = node.AddChild(funcInfo, function);

    if (isNewNode) {
      RegisterFunctionTreeNode(childNode);
    }

    return childNode;
  }

  private void RegisterFunctionTreeNode(ProfileCallTreeNode node) {
    // Add an unique instance of the node for a function.
    node.Id = Interlocked.Increment(ref nextNodeId_);
    ref var nodeList = ref CollectionsMarshal.GetValueRefOrAddDefault(funcToNodesMap_, node.Function, out bool exists);

    if (!exists) {
      nodeList = new List<ProfileCallTreeNode>();
    }

    nodeList.Add(node);
  }

  public ProfileCallTreeNode FindNode(long nodeId) {
    // Build mapping on-demand.
    if (nodeIdMap_ == null) {
      if (nodeIdMap_ == null) {
        nodeIdMap_ = new Dictionary<long, ProfileCallTreeNode>(funcToNodesMap_.Count);

        foreach (var list in funcToNodesMap_.Values) {
          foreach (var node in list) {
            nodeIdMap_[node.Id] = node;
          }
        }
      }
    }

    return nodeIdMap_.GetValueOrNull(nodeId);
  }

  public ProfileCallTreeNode FindMatchingNode(ProfileCallTreeNode queryNode) {
    // Find in the call tree node that corresponds to
    // a node from another instance of a call tree.
    if (queryNode.IsGroup) {
      return null;
    }

    if (!funcToNodesMap_.TryGetValue(queryNode.Function, out var nodeList)) {
      return null;
    }

    foreach (var node in nodeList) {
      if (ReferenceEquals(node, queryNode)) {
        return node; // Shortcut for same call tree instance.
      }

      // Since the IRTextFunctions remain stable across a session,
      // check the equivalence of the nodes by looking at the
      // function in each stack frame (parent) up to the root.
      var nodeA = node;
      var nodeB = queryNode;

      while (nodeA != null && nodeB != null) {
        if (!nodeA.Function.Equals(nodeB.Function)) {
          break;
        }

        nodeA = nodeA.Caller;
        nodeB = nodeB.Caller;
      }

      if (nodeA == null && nodeB == null) {
        return node; // Reached root from both nodes.
      }
    }

    return null;
  }

  public List<ProfileCallTreeNode> GetCallTreeNodes(IRTextFunction function) {
    if (funcToNodesMap_.TryGetValue(function, out var nodeList)) {
      return nodeList;
    }

    return new List<ProfileCallTreeNode>();
  }

  public List<ProfileCallTreeNode> GetSortedCallTreeNodes(IRTextFunction function) {
    var nodeList = GetCallTreeNodes(function);

    if (nodeList.Count < 2) {
      return nodeList;
    }

    // Make a copy of the list since it's shared with all other instances
    // of the node and it may be iterated on another thread, sorting may
    // modify the list which invalidates iteration and throws.
    var nodeListCopy = new List<ProfileCallTreeNode>(nodeList);
    nodeListCopy.Sort((a, b) => b.Weight.CompareTo(a.Weight));
    return nodeListCopy;
  }

  public ProfileCallTreeNode GetCombinedCallTreeNode(IRTextFunction function, ProfileCallTreeNode parentNode = null) {
    var nodes = GetSortedCallTreeNodes(function);
    return CombinedCallTreeNodesImpl(nodes, true, parentNode);
  }

  public static ProfileCallTreeNode CombinedCallTreeNodes(List<ProfileCallTreeNode> nodes,
                                                          bool combineLists = true) {
    var nodeListCopy = new List<ProfileCallTreeNode>(nodes);
    nodeListCopy.Sort((a, b) => b.Weight.CompareTo(a.Weight));
    return CombinedCallTreeNodesImpl(nodeListCopy, combineLists);
  }

  public static TimeSpan CombinedCallTreeNodesWeight(List<ProfileCallTreeNode> nodes) {
    if (nodes.Count == 0) {
      return TimeSpan.Zero;
    }

    var combinedNode = CombinedCallTreeNodes(nodes, false);
    return combinedNode.Weight;
  }

  private static ProfileCallTreeNode CombinedCallTreeNodesImpl(List<ProfileCallTreeNode> nodes,
                                                               bool combineLists = true,
                                                               ProfileCallTreeNode parentNode = null) {
    if (nodes == null || nodes.Count == 0) {
      return new ProfileCallTreeGroupNode();
    }

    if (nodes.Count == 1) {
      return nodes[0];
    }

    // Sort by weight so that parent nodes (more inclusive time)
    // get processed first and have the recursive instances ignored.
    var handledNodes = new HashSet<ProfileCallTreeNode>();
    var comparer = new ProfileCallTreeNodeComparer();
    var childrenSet = new HashSet<ProfileCallTreeNode>(comparer);
    var callersSet = new HashSet<ProfileCallTreeNode>(comparer);
    var callSiteMap = new Dictionary<long, ProfileCallSite>();
    var threadsMap = new Dictionary<int, (TimeSpan, TimeSpan)>();
    var weight = TimeSpan.Zero;
    var excWeight = TimeSpan.Zero;
    var kind = ProfileCallTreeNodeKind.Unset;

    foreach (var node in nodes) {
      // In case of recursive functions, the total time
      // should not be counted again for the recursive calls.
      // When the function is a callee, consider only the nodes that are actually being called
      // by the parent node - by default the list contains every node representing the function,
      // on all paths through the call tree.
      if (parentNode != null && !node.HasParent(parentNode, comparer)) {
        continue;
      }

      // If the node is being called by another
      // instance recursively which has its total time counted,
      // don't count the total time of this instance.
      bool countWeight = !NodeParentWasHandled(node, handledNodes);

      if (countWeight) {
        weight += node.Weight;
        handledNodes.Add(node);
      }

      excWeight += node.ExclusiveWeight;
      kind = node.Kind;

      if (!combineLists) {
        continue;
      }

      // Sum up per-thread weights.
      if (node.HasThreadWeights) {
        foreach (var pair in node.ThreadWeights) {
          threadsMap.AccumulateValue(pair.Key,
                                     countWeight ? pair.Value.Weight : TimeSpan.Zero,
                                     pair.Value.ExclusiveWeight);
        }
      }

      if (node.HasChildren) {
        foreach (var childNode in node.Children) {
          if (!childrenSet.TryGetValue(childNode, out var existingNode)) {
            existingNode = new ProfileCallTreeNode(childNode.FunctionDebugInfo, childNode.Function);
            existingNode.Id = childNode.Id;
            childrenSet.Add(existingNode);
          }

          existingNode.AccumulateWeight(childNode.Weight);
          existingNode.AccumulateExclusiveWeight(childNode.ExclusiveWeight);
        }
      }

      if (node.HasCallers) {
        void HandleCaller(ProfileCallTreeNode caller) {
          if (!callersSet.TryGetValue(caller, out var existingNode)) {
            existingNode = new ProfileCallTreeNode(caller.FunctionDebugInfo, caller.Function);
            existingNode.Id = caller.Id;
            callersSet.Add(existingNode);
          }

          existingNode.AccumulateWeight(caller.Weight);
          existingNode.AccumulateExclusiveWeight(caller.ExclusiveWeight);
        }

        if (node is ProfileCallTreeGroupNode groupNode) {
          foreach (var caller in groupNode.Callers) {
            HandleCaller(caller);
          }
        }
        else {
          HandleCaller(node.Caller);
        }
      }

      if (node.HasCallSites) {
        foreach (var pair in node.CallSites) {
          ref var callsite = ref CollectionsMarshal.GetValueRefOrAddDefault(callSiteMap, pair.Key, out bool exists);

          if (!exists) {
            callsite = new ProfileCallSite(pair.Key);
          }

          foreach (var target in pair.Value.Targets) {
            callsite.AddTarget(target.Node, target.Weight);
          }
        }
      }
    }

    return new ProfileCallTreeGroupNode(nodes[0].FunctionDebugInfo, nodes[0].Function, nodes,
                                        childrenSet.ToList(), callersSet.ToList(),
                                        callSiteMap, threadsMap) {
      Weight = weight, ExclusiveWeight = excWeight,
      Kind = kind
    };
  }

  private static bool NodeParentWasHandled(ProfileCallTreeNode node, HashSet<ProfileCallTreeNode> handledNodes) {
    if (!node.IsGroup) {
      var callerNode = node.Caller;

      while (callerNode != null) {
        if (handledNodes.Contains(callerNode)) {
          return true;
        }

        callerNode = callerNode.Caller;
      }
    }

    return false;
  }

  public TimeSpan GetCombinedCallTreeNodeWeight(IRTextFunction function) {
    var nodes = GetCallTreeNodes(function);

    if (nodes == null) {
      return TimeSpan.Zero;
    }

    var combinedNode = CombinedCallTreeNodesImpl(nodes, false);
    return combinedNode.Weight;
  }

  public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
    var list = new List<ProfileCallTreeNode>();

    // For multiple node groups there is no proper backtrace.
    if (node is ProfileCallTreeGroupNode groupNode &&
        groupNode.Nodes.Count > 1) {
      return list;
    }

    while (node.HasCallers) {
      list.Add(node.Callers[0]);
      node = node.Callers[0];
    }

    return list;
  }

  public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node) {
    return GetTopFunctionsAndModules(node).Functions;
  }

  public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
    return GetTopFunctionsAndModules(node).Modules;
  }

  public (List<ProfileCallTreeNode> Functions,
    List<ModuleProfileInfo> Modules) GetTopFunctionsAndModules(ProfileCallTreeNode node) {
    var moduleMap = new Dictionary<string, ModuleProfileInfo>();
    var funcMap = new Dictionary<IRTextFunction, ProfileCallTreeNode>();

    if (node is ProfileCallTreeGroupNode groupNode) {
      foreach (var nestedNode in groupNode.Nodes) {
        CollectFunctionsAndModules(nestedNode, funcMap, moduleMap);
      }
    }
    else {
      CollectFunctionsAndModules(node, funcMap, moduleMap);
    }

    // In case of recursive functions, the total time
    // should not be counted again for the recursive calls.
    var handledNodes = new HashSet<ProfileCallTreeNode>();

    foreach (var collectedNode in funcMap.Values) {
      var collectedGroupNode = collectedNode as ProfileCallTreeGroupNode;

      if (collectedGroupNode.Nodes.Count == 1) {
        collectedGroupNode.Weight = collectedGroupNode.Nodes[0].Weight;
        collectedGroupNode.ExclusiveWeight = collectedGroupNode.Nodes[0].ExclusiveWeight;
      }
      else if (collectedGroupNode.Nodes.Count > 1) {
        collectedGroupNode.Nodes.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        handledNodes.Clear();

        foreach (var instanceNode in collectedGroupNode.Nodes) {
          // If the node is being called by another
          // instance recursively which has its total time counted,
          // don't count the total time of this instance.
          bool countWeight = !NodeParentWasHandled(node, handledNodes);
          collectedGroupNode.ExclusiveWeight += instanceNode.ExclusiveWeight;

          if (countWeight) {
            collectedGroupNode.Weight += instanceNode.Weight;
            handledNodes.Add(instanceNode);
          }
        }
      }
    }

    // Compute time percentage per module.
    var moduleList = new List<ModuleProfileInfo>(moduleMap.Count);

    foreach (var module in moduleMap.Values) {
      module.Percentage = node.ScaleWeight(module.Weight);
      moduleList.Add(module);
    }

    moduleList.Sort((a, b) => b.Weight.CompareTo(a.Weight));
    var funcList = funcMap.ToValueList();
    funcList.Sort((a, b) => b.ExclusiveWeight.CompareTo(a.ExclusiveWeight));
    return (funcList, moduleList);
  }

  private void CollectFunctionsAndModules(ProfileCallTreeNode node,
                                          Dictionary<IRTextFunction, ProfileCallTreeNode> funcMap,
                                          Dictionary<string, ModuleProfileInfo> moduleMap) {
    // Combine all instances of a function under the node.
    ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(funcMap, node.Function, out bool exists);

    if (!exists) {
      entry = new ProfileCallTreeGroupNode(node.FunctionDebugInfo, node.Function, node.Kind);
    }

    var groupEntry = (ProfileCallTreeGroupNode)entry;
    groupEntry.Nodes.Add(node);
    //groupEntry.AccumulateWeight(node.Weight);
    //groupEntry.AccumulateExclusiveWeight(node.ExclusiveWeight);

    // Collect time and functions per module.
    ref var moduleEntry =
      ref CollectionsMarshal.GetValueRefOrAddDefault(moduleMap, node.ModuleName, out bool moduleExists);

    if (!moduleExists) {
      moduleEntry = new ModuleProfileInfo(node.ModuleName);
    }

    moduleEntry.Weight += node.ExclusiveWeight;
    moduleEntry.Functions.Add(groupEntry);

    if (node.HasChildren) {
      foreach (var childNode in node.Children) {
        CollectFunctionsAndModules(childNode, funcMap, moduleMap);
      }
    }
  }

  public void MergeWith(ProfileCallTree otherTree) {
    // Recursively merge the common root nodes
    // and copy over any new root nodes.
    foreach (var rootNode in otherTree.rootNodes_) {
      if (rootNodes_.TryGetValue(rootNode.Key, out var existingRootNode)) {
        existingRootNode.MergeWith(rootNode.Value);
      }
      else {
        rootNodes_[rootNode.Key] = rootNode.Value;
      }
    }

    // Merge the other data structures.
    if (otherTree.funcToNodesMap_ != null) {
      funcToNodesMap_ ??= new Dictionary<IRTextFunction, List<ProfileCallTreeNode>>();
      var existingNodesSet = new HashSet<ProfileCallTreeNode>();

      foreach (var list in funcToNodesMap_.Values) {
        foreach (var node in list) {
          existingNodesSet.Add(node);
        }
      }

      foreach (var pair in otherTree.funcToNodesMap_) {
        ref var existingList =
          ref CollectionsMarshal.GetValueRefOrAddDefault(funcToNodesMap_, pair.Key, out bool exists);

        if (exists) {
          // A function present in both tree, add the nodes that are missing.
          foreach (var node in pair.Value) {
            if (!node.IsMergeNode() && !existingNodesSet.Contains(node)) {
              existingList.Add(node);
            }

            node.ClearIsMergedNode();
          }
        }
        else {
          // A function present only in the other tree.
          existingList = pair.Value;
        }
      }
    }

    if (otherTree.nodeIdMap_ != null) {
      nodeIdMap_ ??= new Dictionary<long, ProfileCallTreeNode>();

      foreach (var pair in otherTree.nodeIdMap_) {
        nodeIdMap_[pair.Key] = pair.Value;
      }
    }
  }

  public string Print() {
    var builder = new StringBuilder();

    foreach (var node in rootNodes_) {
      builder.AppendLine("Call tree root node");
      builder.AppendLine("-----------------------");
      node.Value.Print(builder);
    }

    return builder.ToString();
  }

  public void VerifyCycles() {
    var nodeMap = new HashSet<ProfileCallTreeNode>();

    foreach (var node in rootNodes_) {
      nodeMap.Clear();
      VerifyCycles(node.Value, nodeMap);
    }
  }

  private void VerifyCycles(ProfileCallTreeNode node,
                            HashSet<ProfileCallTreeNode> nodeMap) {
    if (!nodeMap.Add(node)) {
      Trace.WriteLine($"Found cycle in CallTree for node {node}");
      Debug.Assert(false);
      return;
    }

    if (node.HasChildren) {
      foreach (var childNode in node.Children) {
        VerifyCycles(childNode, nodeMap);
      }
    }
  }

  public string PrintNodeInstances(string funcName, bool printStack = false) {
    var list = new List<ProfileCallTreeNode>();

    foreach (var pair in rootNodes_) {
      CollectNodeInstances(pair.Value, funcName, list);
    }

    list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
    var weight = TimeSpan.Zero;
    var excWeight = TimeSpan.Zero;

    foreach (var node in list) {
      weight += node.Weight;
      excWeight += node.ExclusiveWeight;
    }

    var sb = new StringBuilder();
    sb.AppendLine($"Instances for {funcName}: {list.Count}");
    sb.AppendLine(
      $" - Total weight: {weight} ({weight.TotalMilliseconds} ms), excl weight: {excWeight} ({excWeight.TotalMilliseconds} ms)");

    foreach (var node in list) {
      sb.AppendLine(
        $" - Weight: {node.Weight} ({node.Weight.TotalMilliseconds} ms), excl weight: {node.ExclusiveWeight} ({node.ExclusiveWeight.TotalMilliseconds} ms), children: {(node.HasChildren ? node.Children.Count : 0)}");
      weight += node.Weight;
      excWeight += node.ExclusiveWeight;

      if (printStack) {
        sb.AppendLine("  - Stack:");
        var stackNode = node;
        int index = 0;

        while (stackNode != null) {
          sb.AppendLine($"     {index}: {stackNode.FunctionName}");
          stackNode = stackNode.Caller;
          index++;
        }

        sb.AppendLine($"  ------------------------------");
      }
    }

    return sb.ToString();
  }

  public void CollectNodeInstances(ProfileCallTreeNode node, string funcName, List<ProfileCallTreeNode> list) {
    if (node.FunctionName.Equals(funcName, StringComparison.Ordinal)) {
      list.Add(node);
    }

    if (node.HasChildren) {
      foreach (var child in node.Children) {
        CollectNodeInstances(child, funcName, list);
      }
    }
  }

  public override string ToString() {
    return $"Root nodes: {rootNodes_.Count}, Weight: {TotalRootNodesWeight}";
  }

  private void InitializeReferenceMembers() {
    rootNodes_ ??= new ConcurrentDictionary<IRTextFunction, ProfileCallTreeNode>();
    funcToNodesMap_ ??= new Dictionary<IRTextFunction, List<ProfileCallTreeNode>>();
  }

  public void ResetTags() {
    foreach (var list in funcToNodesMap_.Values) {
      foreach (var node in list) {
        node.Tag = null;
      }
    }
  }

  public ProfileCallTreeNode FindRootNode(IRTextFunction func) {
    if (rootNodes_.TryGetValue(func, out var node)) {
      return node;
    }

    return null;
  }
}