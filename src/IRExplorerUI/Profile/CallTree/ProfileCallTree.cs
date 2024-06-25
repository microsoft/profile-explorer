// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile;

public enum ProfileCallTreeNodeKind {
  Unset,
  NativeUser,
  NativeKernel,
  Managed
}

public sealed class ProfileCallTree {
  private ConcurrentDictionary<IRTextFunction, ProfileCallTreeNode> rootNodes_;
  private Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcToNodesMap_;
  private Dictionary<long, ProfileCallTreeNode> nodeIdMap_;
  private int nextNodeId_;
  //private ReaderWriterLockSlim lock_;
  //private ReaderWriterLockSlim funcLock_;

  public ProfileCallTree() {
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

  public static ProfileCallTree Deserialize(byte[] data, Dictionary<Guid, IRTextSummary> summaryMap) {
    var state = StateSerializer.Deserialize<ProfileCallTreeState>(data);
    var callTree = new ProfileCallTree();

    callTree.nextNodeId_ = state.NextNodeId;
    callTree.nodeIdMap_ = new Dictionary<long, ProfileCallTreeNode>(state.FunctionNodeIdMap.Count);

    foreach (var pair in state.FunctionNodeIdMap) {
      var summary = summaryMap[pair.Key.SummaryId];
      var function = summary.GetFunctionWithId(pair.Key.FunctionNumber);

      if (function == null) {
        Debug.Assert(false, "Could not find node for func");
        continue;
      }

      var nodeList = new List<ProfileCallTreeNode>(pair.Value.Length);

      foreach (long nodeId in pair.Value) {
        var node = state.NodeIdMap[nodeId];
        //node.InitializeFunction(summaryMap);
        nodeList.Add(node);
        callTree.nodeIdMap_[nodeId] = node;

        if (state.ChildrenIds.TryGetValue(nodeId, out long[] childrenIds)) {
          var childrenNodes = new List<ProfileCallTreeNode>(childrenIds.Length);

          foreach (long childNodeId in childrenIds) {
            var childNode = state.NodeIdMap[childNodeId];
            childNode.SetParent(node);
            childrenNodes.Add(childNode);
          }

          node.SetChildrenNoLock(childrenNodes);
        }
      }

      callTree.funcToNodesMap_[function] = nodeList;
    }

    callTree.rootNodes_ = new ConcurrentDictionary<IRTextFunction, ProfileCallTreeNode>();

    foreach (long nodeId in state.RootNodeIds) {
      var node = state.NodeIdMap[nodeId];
      callTree.rootNodes_.TryAdd(node.Function, node);
    }

    return callTree;
  }

  public void UpdateCallTree(ProfileSample sample, ResolvedProfileStack resolvedStack) {
    // Build call tree. Note that the call tree methods themselves are thread-safe.
    bool isRootFrame = true;
    ProfileCallTreeNode prevNode = null;
    ResolvedProfileStackFrame prevFrame = null;

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

        //? TODO: Call sites can be computed on-demand when opening a func
        //? by going over the samples in the func, similar to how timeline selection works
        prevNode.AddCallSite(node, prevFrame.FrameRVA, sample.Weight);
      }

      node.AccumulateWeight(sample.Weight);
      node.AccumulateWeight(sample.Weight, TimeSpan.Zero, resolvedStack.Context.ThreadId);

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
      prevNode.AccumulateExclusiveWeight(sample.Weight);
      prevNode.AccumulateWeight(TimeSpan.Zero, sample.Weight, resolvedStack.Context.ThreadId);
    }
  }

  public byte[] Serialize() {
    var state = new ProfileCallTreeState(this);
    int index = 0;

    foreach (var node in rootNodes_) {
      state.RootNodeIds[index++] = node.Value.Id;
    }

    foreach (var pair in funcToNodesMap_) {
      long[] funcNodeIds = new long[pair.Value.Count];
      index = 0;

      foreach (var node in pair.Value) {
        state.NodeIdMap[node.Id] = node;
        funcNodeIds[index++] = node.Id;

        // Save children IDs.
        if (node.HasChildren) {
          long[] childNodeIds = new long[node.Children.Count];
          int childIndex = 0;

          foreach (var childNode in node.Children) {
            childNodeIds[childIndex++] = childNode.Id;
          }

          state.ChildrenIds[node.Id] = childNodeIds;
        }
      }

      // Save mapping from function to all node instance IDs.
      state.FunctionNodeIdMap[pair.Key] = funcNodeIds;
    }

    return StateSerializer.Serialize(state);
  }

  public ProfileCallTreeNode AddRootNode(FunctionDebugInfo funcInfo, IRTextFunction function) {
    if (rootNodes_.TryGetValue(function, out var existingNode)) {
      return existingNode;
    }

    var node = rootNodes_.GetOrAdd(function, static (func, info) => new ProfileCallTreeNode(info, func), funcInfo);
    RegisterFunctionTreeNode(node);
    return node;
  }

  public ProfileCallTreeNode AddChildNode(ProfileCallTreeNode node, FunctionDebugInfo funcInfo,
                                          IRTextFunction function) {
    (var childNode, bool isNewNode) = node.AddChild(funcInfo, function);

    if (isNewNode) {
      RegisterFunctionTreeNode(childNode);
    }

    return childNode;
  }

  public void RegisterFunctionTreeNode(ProfileCallTreeNode node) {
    // Add an unique instance of the node for a function.
    node.Id = Interlocked.Increment(ref nextNodeId_);
    List<ProfileCallTreeNode> nodeList = null;

    try {
      //funcLock_.EnterUpgradeableReadLock();

      if (!funcToNodesMap_.TryGetValue(node.Function, out nodeList)) {
        //funcLock_.EnterWriteLock();

        try {
          nodeList = new List<ProfileCallTreeNode>();
          funcToNodesMap_[node.Function] = nodeList;
        }
        finally {
          //funcLock_.ExitWriteLock();
        }
      }

      nodeList.Add(node);
    }
    finally {
      //funcLock_.ExitUpgradeableReadLock();
    }
  }

  public ProfileCallTreeNode FindNode(long nodeId) {
    // Build mapping on-demand.
    if (nodeIdMap_ == null) {
      try {
        //funcLock_.EnterWriteLock();

        if (nodeIdMap_ == null) {
          nodeIdMap_ = new Dictionary<long, ProfileCallTreeNode>(funcToNodesMap_.Count);

          foreach (var list in funcToNodesMap_.Values) {
            foreach (var node in list) {
              nodeIdMap_[node.Id] = node;
            }
          }
        }
      }
      finally {
        //funcLock_.ExitWriteLock();
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
    try {
      //funcLock_.EnterReadLock();

      if (funcToNodesMap_.TryGetValue(function, out var nodeList)) {
        return nodeList;
      }
    }
    finally {
      //funcLock_.ExitReadLock();
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
      bool countWeight = true;

      if (!node.IsGroup) {
        var callerNode = node.Caller;

        while (callerNode != null) {
          if (handledNodes.Contains(callerNode)) {
            countWeight = false;
            break;
          }

          callerNode = callerNode.Caller;
        }
      }

      if (countWeight) {
        weight += node.Weight;
        handledNodes.Add(node);
      }

      excWeight += node.ExclusiveWeight;

      // Sum up per-thread weights.
      if (combineLists && node.HasThreadWeights) {
        foreach (var pair in node.ThreadWeights) {
          threadsMap.AccumulateValue(pair.Key,
                                     countWeight ? pair.Value.Weight : TimeSpan.Zero,
                                     pair.Value.ExclusiveWeight);
        }
      }

      kind = node.Kind;

      if (combineLists && node.HasChildren) {
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

      if (combineLists && node.HasCallers) {
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

      if (combineLists && node.HasCallSites) {
        foreach (var pair in node.CallSites) {
          if (!callSiteMap.TryGetValue(pair.Key, out var callsite)) {
            callsite = new ProfileCallSite(pair.Key);
            callSiteMap[pair.Key] = callsite;
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

  public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node, bool combineInstances = true) {
    var funcMap = new Dictionary<IRTextFunction, ProfileCallTreeNode>();

    if (node is ProfileCallTreeGroupNode groupNode) {
      foreach (var n in groupNode.Nodes) {
        CollectFunctions(n, funcMap, combineInstances);
      }
    }
    else {
      CollectFunctions(node, funcMap, combineInstances);
    }

    var funcList = new List<ProfileCallTreeNode>(funcMap.Count);

    foreach (var func in funcMap.Values) {
      funcList.Add(func);
    }

    funcList.Sort((a, b) => b.ExclusiveWeight.CompareTo(a.ExclusiveWeight));
    return funcList;
  }

  public void CollectFunctions(ProfileCallTreeNode node, Dictionary<IRTextFunction, ProfileCallTreeNode> funcMap,
                               bool combineInstances = true) {
    var entry = node;

    if (combineInstances) {
      // Combine all instances of a function under the node.
      entry = funcMap.GetOrAddValue(node.Function, () =>
                                      new ProfileCallTreeGroupNode(node.FunctionDebugInfo, node.Function) {
                                        Kind = node.Kind
                                      });

      var groupEntry = (ProfileCallTreeGroupNode)entry;
      groupEntry.Nodes.Add(node);
      groupEntry.AccumulateWeight(node.Weight);
      groupEntry.AccumulateExclusiveWeight(node.ExclusiveWeight);
    }

    if (node.HasChildren) {
      foreach (var childNode in node.Children) {
        CollectFunctions(childNode, funcMap);
      }
    }
  }

  public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
    var moduleMap = new Dictionary<string, ModuleProfileInfo>();

    if (node is ProfileCallTreeGroupNode groupNode) {
      foreach (var n in groupNode.Nodes) {
        CollectModules(n, moduleMap);
      }
    }
    else {
      CollectModules(node, moduleMap);
    }

    var moduleList = new List<ModuleProfileInfo>(moduleMap.Count);

    foreach (var module in moduleMap.Values) {
      module.Percentage = node.ScaleWeight(module.Weight);
      moduleList.Add(module);
    }

    moduleList.Sort((a, b) => b.Weight.CompareTo(a.Weight));
    return moduleList;
  }

  public void CollectModules(ProfileCallTreeNode node, Dictionary<string, ModuleProfileInfo> moduleMap) {
    var entry = moduleMap.GetOrAddValue(node.ModuleName,
                                        () => new ModuleProfileInfo(node.ModuleName));
    entry.Weight += node.ExclusiveWeight;
    entry.Functions.Add(node);

    if (node.HasChildren) {
      foreach (var childNode in node.Children) {
        CollectModules(childNode, moduleMap);
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

  public override string ToString() {
    return $"Root nodes: {rootNodes_.Count}, Weight: {TotalRootNodesWeight}";
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    rootNodes_ ??= new ConcurrentDictionary<IRTextFunction, ProfileCallTreeNode>();
    funcToNodesMap_ ??= new Dictionary<IRTextFunction, List<ProfileCallTreeNode>>();
    //lock_ ??= new ReaderWriterLockSlim();
    //funcLock_ ??= new ReaderWriterLockSlim();
  }

  [ProtoContract(SkipConstructor = true)]
  public class ProfileCallTreeState {
    [ProtoMember(1)]
    public Dictionary<long, ProfileCallTreeNode> NodeIdMap;
    [ProtoMember(2)]
    public Dictionary<IRTextFunctionId, long[]> FunctionNodeIdMap;
    [ProtoMember(3)]
    public Dictionary<long, long[]> ChildrenIds; // Node ID to list of child IDs.
    [ProtoMember(4)]
    public long[] RootNodeIds;
    [ProtoMember(5)]
    public int NextNodeId;

    public ProfileCallTreeState(ProfileCallTree callTree) {
      NodeIdMap = new Dictionary<long, ProfileCallTreeNode>(callTree.funcToNodesMap_.Count * 2);
      FunctionNodeIdMap = new Dictionary<IRTextFunctionId, long[]>(callTree.funcToNodesMap_.Count);
      ChildrenIds = new Dictionary<long, long[]>(callTree.funcToNodesMap_.Count * 2);
      RootNodeIds = new long[callTree.rootNodes_.Count];
      NextNodeId = callTree.nextNodeId_;
    }
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
