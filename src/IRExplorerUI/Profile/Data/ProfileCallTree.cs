// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using Microsoft.Diagnostics.Runtime;
using ProtoBuf;

namespace IRExplorerUI.Profile;

public sealed class ProfileCallTree {
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
        public long NextNodeId;

        public ProfileCallTreeState(ProfileCallTree callTree) {
            NodeIdMap = new Dictionary<long, ProfileCallTreeNode>(callTree.funcToNodesMap_.Count * 2);
            FunctionNodeIdMap = new Dictionary<IRTextFunctionId, long[]>(callTree.funcToNodesMap_.Count);
            ChildrenIds = new Dictionary<long, long[]>(callTree.funcToNodesMap_.Count * 2);
            RootNodeIds = new long[callTree.rootNodes_.Count];
            NextNodeId = callTree.nextNodeId_;
        }
    }

    private ConcurrentDictionary<IRTextFunction, ProfileCallTreeNode> rootNodes_;
    private Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcToNodesMap_;
    private Dictionary<long, ProfileCallTreeNode> nodeIdMap_;
    private long nextNodeId_;
    private ReaderWriterLockSlim lock_;
    private ReaderWriterLockSlim funcLock_;

    public List<ProfileCallTreeNode> RootNodes => rootNodes_.ToValueList();
    public TimeSpan TotalRootNodesWeight {
        get {
            TimeSpan sum = TimeSpan.Zero;
            foreach (var node in rootNodes_) {
                sum += node.Value.Weight;
            }
            return sum;
        }
    }

    public void UpdateCallTree(ProfileSample sample, ResolvedProfileStack resolvedStack) {
        // Build call tree. Note that the call tree methods themselves are thread-safe.
        bool isRootFrame = true;
        ProfileCallTreeNode prevNode = null;
        ResolvedProfileStackFrame prevFrame = new();

        for (int k = resolvedStack.FrameCount - 1; k >= 0; k--) {
            var resolvedFrame = resolvedStack.StackFrames[k];

            if (resolvedFrame.FrameRVA == 0 &&  resolvedFrame.FrameDetails.DebugInfo == null) {
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

        if (prevNode != null) {
            prevNode.AccumulateExclusiveWeight(sample.Weight);
        }
    }

    public byte[] Serialize() {
        var state = new ProfileCallTreeState(this);
        int index = 0;

        foreach (var node in rootNodes_) {
            state.RootNodeIds[index++] = node.Value.Id;
        }

        foreach (var pair in funcToNodesMap_) {
            var funcNodeIds = new long[pair.Value.Count];
            index = 0;

            foreach (var node in pair.Value) {
                state.NodeIdMap[node.Id] = node;
                funcNodeIds[index++] = node.Id;

                // Save children IDs.
                if (node.HasChildren) {
                    var childNodeIds = new long[node.Children.Count];
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

            foreach (var nodeId in pair.Value) {
                var node = state.NodeIdMap[nodeId];
                node.InitializeFunction(summaryMap);
                nodeList.Add(node);
                callTree.nodeIdMap_[nodeId] = node;

                if (state.ChildrenIds.TryGetValue(nodeId, out var childrenIds)) {
                    var childrenNodes = new List<ProfileCallTreeNode>(childrenIds.Length);

                    foreach (var childNodeId in childrenIds) {
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

        foreach (var nodeId in state.RootNodeIds) {
            var node = state.NodeIdMap[nodeId];
            callTree.rootNodes_.TryAdd(node.Function, node);
        }

        return callTree;
    }

    public ProfileCallTree() {
        InitializeReferenceMembers();
    }

    [ProtoAfterDeserialization]
    private void InitializeReferenceMembers() {
        rootNodes_ ??= new ConcurrentDictionary<IRTextFunction, ProfileCallTreeNode>();
        funcToNodesMap_ ??= new Dictionary<IRTextFunction, List<ProfileCallTreeNode>>();
        lock_ ??= new ReaderWriterLockSlim();
        funcLock_ ??= new ReaderWriterLockSlim();
    }

    public ProfileCallTreeNode AddRootNode(FunctionDebugInfo funcInfo, IRTextFunction function) {
        if (rootNodes_.TryGetValue(function, out var existingNode)) {
            return existingNode;
        }

        var node = rootNodes_.GetOrAdd(function, static (func, info) => new ProfileCallTreeNode(info, func), funcInfo);
        RegisterFunctionTreeNode(node);
        return node;
    }

    public ProfileCallTreeNode AddChildNode(ProfileCallTreeNode node, FunctionDebugInfo funcInfo, IRTextFunction function) {
        var (childNode, isNewNode) = node.AddChild(funcInfo, function);

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
            funcLock_.EnterUpgradeableReadLock();

            if (!funcToNodesMap_.TryGetValue(node.Function, out nodeList)) {
                funcLock_.EnterWriteLock();
                try {
                    nodeList = new List<ProfileCallTreeNode>();
                    funcToNodesMap_[node.Function] = nodeList;
                }
                finally {
                    funcLock_.ExitWriteLock();
                }
            }
        }
        finally {
            funcLock_.ExitUpgradeableReadLock();
        }

        lock (nodeList) {
            nodeList.Add(node);
        }
    }

    public ProfileCallTreeNode FindNode(long nodeId) {
        // Build mapping on-demand.
        if (nodeIdMap_ == null) {
            try {
                funcLock_.EnterWriteLock();

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
                funcLock_.ExitWriteLock();
            }
        }

        return nodeIdMap_.GetValueOrNull(nodeId);
    }

    public List<ProfileCallTreeNode> GetCallTreeNodes(IRTextFunction function) {
        try {
            funcLock_.EnterReadLock();

            if (funcToNodesMap_.TryGetValue(function, out var nodeList)) {
                return nodeList;
            }
        }
        finally {
            funcLock_.ExitReadLock();
        }

        return null;
    }

    public List<ProfileCallTreeNode> GetSortedCallTreeNodes(IRTextFunction function) {
        var nodeList = GetCallTreeNodes(function);

        if (nodeList == null) {
            return nodeList;
        }

        // Make a copy of the list since it's shared with all other instances
        // of the node and it may be iterated on another thread.
        var nodeListCopy = new List<ProfileCallTreeNode>(nodeList);
        nodeListCopy.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        return nodeListCopy;
    }

    public ProfileCallTreeNode GetCombinedCallTreeNode(IRTextFunction function, ProfileCallTreeNode parentNode = null) {
        var nodes = GetCallTreeNodes(function);

        if (nodes == null) {
            return null;
        }
        else if (nodes.Count == 1) {
            return nodes[0];
        }

        var comparer = new ProfileCallTreeNodeComparer();
        var childrenSet = new HashSet<ProfileCallTreeNode>(comparer);
        var callersSet = new HashSet<ProfileCallTreeNode>(comparer);
        var callSiteMap = new Dictionary<long, ProfileCallSite>();
        TimeSpan weight = TimeSpan.Zero;
        TimeSpan excWeight = TimeSpan.Zero;
        var kind = ProfileCallTreeNodeKind.Unset;

        foreach (var node in nodes) {
            // When the function is a callee, consider only the nodes that are actually being called
            // by the parent node - by default the list contains every node representing the function,
            // on all paths through the call tree.
            if (parentNode != null && !node.HasParent(parentNode, comparer)) {
                continue;
            }

            weight += node.Weight;
            excWeight += node.ExclusiveWeight;
            kind = node.Kind;

            if (node.HasChildren) {
                foreach (var childNode in node.Children) {
                    if (!childrenSet.TryGetValue(childNode, out var existingNode)) {
                        existingNode = new ProfileCallTreeNode(childNode.FunctionDebugInfo, childNode.Function);
                        existingNode.Id = childNode.Id;
                        childrenSet.Add(existingNode);
                    }

                    existingNode.Weight += childNode.Weight;
                    existingNode.ExclusiveWeight += childNode.ExclusiveWeight;
                }
            }

            if (node.HasCallers) {
                if (!callersSet.TryGetValue(node.Caller, out var existingNode)) {
                    existingNode = new ProfileCallTreeNode(node.Caller.FunctionDebugInfo, node.Caller.Function);
                    existingNode.Id = node.Caller.Id;
                    callersSet.Add(existingNode);
                }

                existingNode.Weight += node.Caller.Weight;
                existingNode.ExclusiveWeight += node.Caller.ExclusiveWeight;
            }

            if (node.HasCallSites) {
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
                                            childrenSet.ToList(), callersSet.ToList(), callSiteMap) {
            Weight = weight, ExclusiveWeight = excWeight,
            Kind = kind
        };
    }

    public TimeSpan GetCombinedCallTreeNodeWeight(IRTextFunction function) {
        var nodes = GetCallTreeNodes(function);

        if (nodes == null) {
            return TimeSpan.Zero;
        }
        else if (nodes.Count == 1) {
            return nodes[0].Weight;
        }

        TimeSpan weight = TimeSpan.Zero;

        foreach (var node in nodes) {
            weight += node.Weight;
        }

        return weight;
    }

    public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
        var list = new List<ProfileCallTreeNode>();

        while(node.HasCallers) {
            list.Add(node.Callers[0]);
            node = node.Callers[0];
        }

        return list;
    }

    public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node) {
        var funcMap = new Dictionary<IRTextFunction, ProfileCallTreeNode>();
        CollectFunctions(node, funcMap);
        var funcList = new List<ProfileCallTreeNode>(funcMap.Count);

        foreach (var func in funcMap.Values) {
            funcList.Add(func);
        }

        funcList.Sort((a, b) => b.ExclusiveWeight.CompareTo(a.ExclusiveWeight));
        return funcList;
    }

    public void CollectFunctions(ProfileCallTreeNode node, Dictionary<IRTextFunction, ProfileCallTreeNode> funcMap) {
        //? TODO: Instead of making a fake CallTreeNode, have CallTreeNodePanel accept an interface
        //? implemented by both CallTreeNode and FGNode exposing weight/time info?

        // Combine all instances of a function under the node.
        var entry = funcMap.GetOrAddValue(node.Function,
            () => new ProfileCallTreeGroupNode(node.FunctionDebugInfo, node.Function) {
                Kind = node.Kind
            });

        var groupEntry = (ProfileCallTreeGroupNode)entry;
        groupEntry.Nodes.Add(node);
        groupEntry.Weight += node.Weight;
        groupEntry.ExclusiveWeight += node.ExclusiveWeight;

        if (node.HasChildren) {
            foreach (var childNode in node.Children) {
                CollectFunctions(childNode, funcMap);
            }
        }
    }

    public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
        var moduleMap = new Dictionary<string, ModuleProfileInfo>();
        CollectModules(node, moduleMap);
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
}

public enum ProfileCallTreeNodeKind {
    Unset,
    NativeUser,
    NativeKernel,
    Managed
}

[ProtoContract(SkipConstructor = true)]
public class ProfileCallTreeNode : IEquatable<ProfileCallTreeNode> {
    [ProtoMember(1)]
    //? Remove reference once no longer serialized
    private IRTextFunctionReference functionRef_ { get; set; }

    private TinyList<ProfileCallTreeNode> children_;

    //? TODO: ProfileCallSite not serialized properly, references CallTreeNode and should use Id instead
    //[ProtoMember(3)]
    private Dictionary<long, ProfileCallSite> callSites_; //? Use Hybrid array/dict to save space

    private ProfileCallTreeNode caller_; // Can't be serialized, reconstructed.
    private ReaderWriterLockSlim lock_; // Lock for updating children, callers, call sites.

    [ProtoMember(4)]
    public long Id { get; set; }
    [ProtoMember(5)]
    public FunctionDebugInfo FunctionDebugInfo { get; set; }

    [ProtoMember(6)] private long weight_; // Weight saved as ticks to use Interlocked.Add
    [ProtoMember(7)] private long exclusiveWeight_;
    [ProtoMember(8)]
    public ProfileCallTreeNodeKind Kind { get; set; }

    public TimeSpan Weight {
        get => TimeSpan.FromTicks(weight_);
        set => weight_ = value.Ticks;
    }

    public TimeSpan ExclusiveWeight {
        get => TimeSpan.FromTicks(exclusiveWeight_);
        set => exclusiveWeight_ = value.Ticks;
    }

    public IRTextFunction Function {
        get => functionRef_;
        set => functionRef_ = value;
    }

    public IList<ProfileCallTreeNode> Children => children_;
    public virtual List<ProfileCallTreeNode> Callers => new() { caller_ };

#if DEBUG
    public ProfileCallTreeNode Caller => !IsGroup ? caller_ : throw new InvalidOperationException("For group use Callers");
#else
    public ProfileCallTreeNode Caller => caller_;
#endif
    public Dictionary<long, ProfileCallSite> CallSites => callSites_;

    public virtual bool IsGroup => false;
    public bool HasChildren => Children != null && Children.Count > 0;
    public virtual bool HasCallers => caller_ != null;
    public bool HasCallSites => CallSites != null && CallSites.Count > 0;
    public string FunctionName => Function.Name;
    public string ModuleName => Function.ParentSummary.ModuleName;

    public double ScaleWeight(TimeSpan relativeWeigth) {
        return (double)relativeWeigth.Ticks / (double)Weight.Ticks;
    }

    public (TimeSpan Weight, TimeSpan ExclusiveWeight) ChildrenWeight {
        get {
            Debug.Assert(HasChildren);
            TimeSpan weight = TimeSpan.Zero;
            TimeSpan exclusiveWeight = TimeSpan.Zero;

            foreach (var child in Children) {
                weight += child.Weight;
                exclusiveWeight += child.ExclusiveWeight;
            }

            return (weight, exclusiveWeight);
        }
    }

    public ProfileCallTreeNode() {}

    public ProfileCallTreeNode(FunctionDebugInfo funcInfo, IRTextFunction function,
                               List<ProfileCallTreeNode> children = null,
                               ProfileCallTreeNode caller = null,
                               Dictionary<long, ProfileCallSite> callSites = null) {
        InitializeReferenceMembers();
        FunctionDebugInfo = funcInfo;

        if (function != null) { // Happens for dummy nodes used in UI.
            Function = function;
        }

        children_ = new TinyList<ProfileCallTreeNode>(children);
        caller_ = caller;
        callSites_ = callSites;
    }

    [ProtoAfterDeserialization]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InitializeReferenceMembers() {
        lock_ ??= new ReaderWriterLockSlim();
        functionRef_ ??= new IRTextFunctionReference();
    }

    internal void InitializeFunction(Dictionary<Guid, IRTextSummary> summaryMap) {
        var summary = summaryMap[functionRef_.Id.SummaryId];
        var func = summary.GetFunctionWithId(functionRef_.Id.FunctionNumber);

        if (func == null) {
            Debug.Assert(false, "Could not find func");
            return;
        }

        Function = func;
    }

    public void AccumulateWeight(TimeSpan weight) {
        // Avoid lock incrementing the weight ticks.
        Interlocked.Add(ref weight_, weight.Ticks);
    }

    public void AccumulateExclusiveWeight(TimeSpan weight) {
        // Avoid lock incrementing the weight ticks.
        Interlocked.Add(ref exclusiveWeight_, weight.Ticks);
    }

    public (ProfileCallTreeNode, bool) AddChild(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
        return GetOrCreateChildNode(functionDebugInfo, function);
    }

    internal void SetChildrenNoLock(List<ProfileCallTreeNode> children) {
        // Used by ProfileCallTree.Deserialize.
        children_ = new TinyList<ProfileCallTreeNode>(children);
    }

    internal void SetParent(ProfileCallTreeNode parentNode) {
        // Used by ProfileCallTree.Deserialize.
        caller_ = parentNode;
    }

    public bool HasParent(ProfileCallTreeNode parentNode, ProfileCallTreeNodeComparer comparer) {
        return caller_ != null && comparer.Equals(caller_, parentNode);
    }

    private (ProfileCallTreeNode, bool) GetOrCreateChildNode(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
        try {
            lock_.EnterUpgradeableReadLock();

            var childNode = FindExistingNode(functionDebugInfo, function);

            if (childNode != null) {
                return (childNode, false);
            }

            try {
                // Check again if another thread added the child in the meantime.
                lock_.EnterWriteLock();
                childNode = FindExistingNode(functionDebugInfo, function);

                if (childNode != null) {
                    return (childNode, false);
                }

                childNode = new ProfileCallTreeNode(functionDebugInfo, function, null, this);
                children_.Add(childNode);
                return (childNode, true);
            }
            finally {
                lock_.ExitWriteLock();
            }
        }
        finally {
            lock_.ExitUpgradeableReadLock();
        }
    }

    public void AddCallSite(ProfileCallTreeNode childNode, long rva, TimeSpan weight) {
        try {
            lock_.EnterWriteLock();

            if (callSites_ == null || !callSites_.TryGetValue(rva, out var callsite)) {
                callSites_ ??= new Dictionary<long, ProfileCallSite>();
                callsite = new ProfileCallSite(rva);
                callSites_[rva] = callsite;
            }

            callsite.AddTarget(childNode, weight);
        }
        finally {
            lock_.ExitWriteLock();
        }
    }

    private ProfileCallTreeNode FindExistingNode(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
        for(int i = 0; i < children_.Count; i++) {
            var child = children_[i];

            if (child.Equals(function)) {
                return child;
            }
        }

        return null;
    }

    internal void Print(StringBuilder builder, int level = 0, bool caller = false) {
        builder.Append(new string(' ', level * 4));
        builder.AppendLine($"{FunctionDebugInfo.Name}, RVA {FunctionDebugInfo.RVA}, Id {Id}");
        builder.Append(new string(' ', level * 4));
        builder.AppendLine($"    weight {Weight.TotalMilliseconds}");
        builder.Append(new string(' ', level * 4));
        builder.AppendLine($"    exc weight {ExclusiveWeight.TotalMilliseconds}");
        builder.Append(new string(' ', level * 4));
        builder.AppendLine($"    callees: {(Children != null ? Children.Count : 0)}");

        if (Children != null && !caller) {
            foreach (var child in Children) {
                child.Print(builder, level + 1);
            }
        }
    }

    public bool Equals(IRTextFunction function) {
        return Function.Equals(function);
    }

    public bool Equals(ProfileCallTreeNode other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        return Id == other.Id;
    }

    public override bool Equals(object obj) {
        if (ReferenceEquals(null, obj)) {
            return false;
        }

        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj.GetType() != this.GetType()) {
            return false;
        }

        return Equals((ProfileCallTreeNode)obj);
    }

    public override int GetHashCode() {
        return Id.GetHashCode();
    }

    public static bool operator ==(ProfileCallTreeNode left, ProfileCallTreeNode right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileCallTreeNode left, ProfileCallTreeNode right) {
        return !Equals(left, right);
    }

    public override string ToString() {
        return $"{FunctionName}, weight: {Weight}, exc weight {ExclusiveWeight}, children: {(HasCallers ? Children.Count : 0)}";
    }
}

public sealed class ProfileCallTreeGroupNode : ProfileCallTreeNode {
    private List<ProfileCallTreeNode> nodes_;
    private List<ProfileCallTreeNode> callers_;

    public override bool IsGroup => true;
    public List<ProfileCallTreeNode> Nodes => nodes_;
    public override List<ProfileCallTreeNode> Callers => callers_;

    public override bool HasCallers => callers_ != null && callers_.Count > 0;

    public ProfileCallTreeGroupNode(FunctionDebugInfo funcInfo, IRTextFunction function,
                                    List<ProfileCallTreeNode> nodes = null,
                                    List<ProfileCallTreeNode> children = null,
                                    List<ProfileCallTreeNode> callers = null,
                                    Dictionary<long, ProfileCallSite> callSites = null) :
        base(funcInfo, function, children, null, callSites) {
        nodes_= nodes ?? new List<ProfileCallTreeNode>();
        callers_ = callers ?? new List<ProfileCallTreeNode>();
    }
}

[ProtoContract(SkipConstructor = true)]
public class ProfileCallSite : IEquatable<ProfileCallSite> {
    [ProtoMember(1)]
    public long RVA { get; set; }
    [ProtoMember(2)]
    public TimeSpan Weight { get; set; }
    [ProtoMember(3)]
    public List<(ProfileCallTreeNode Node, TimeSpan Weight)> Targets { get; set; }

    private bool isSorted_;

    public List<(ProfileCallTreeNode Node, TimeSpan Weight)> SortedTargets {
        get {
            if (!HasSingleTarget && !isSorted_) {
                Targets.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            }

            return Targets;
        }
    }

    public bool HasSingleTarget => Targets.Count == 1;

    public ProfileCallSite(long rva) {
        InitializeReferenceMembers();
        RVA = rva;
        Weight = TimeSpan.Zero;
    }

    public double ScaleWeight(TimeSpan weight) {
        return (double)weight.Ticks / (double)Weight.Ticks;
    }

    public void AddTarget(ProfileCallTreeNode node, TimeSpan weight) {
        Weight += weight; // Total weight of targets.
        int index = Targets.FindIndex(item => item.Node.Equals(node.Function));

        if (index != -1) {
            var span = CollectionsMarshal.AsSpan(Targets);
            span[index].Weight += weight; // Modify in-place per-target weight.
        }
        else {
            Targets.Add((node, weight));
        }
    }

    [ProtoAfterDeserialization]
    private void InitializeReferenceMembers() {
        Targets ??= new List<(ProfileCallTreeNode NodeId, TimeSpan Weight)>();
    }

    public bool Equals(ProfileCallSite other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return RVA == other.RVA;
    }

    public override bool Equals(object obj) {
        if (ReferenceEquals(null, obj)) {
            return false;
        }

        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj.GetType() != this.GetType()) {
            return false;
        }

        return Equals((ProfileCallSite)obj);
    }

    public override int GetHashCode() {
        return RVA.GetHashCode();
    }

    public static bool operator ==(ProfileCallSite left, ProfileCallSite right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileCallSite left, ProfileCallSite right) {
        return !Equals(left, right);
    }
}

// Comparer used for the root nodes in order to ignore the ID part.
public class ProfileCallTreeNodeComparer : IEqualityComparer<ProfileCallTreeNode> {
    public bool Equals(ProfileCallTreeNode x, ProfileCallTreeNode y) {
        return x.Equals(y.Function);
    }

    public int GetHashCode(ProfileCallTreeNode obj) {
        return HashCode.Combine(obj.Function);
    }
}

public class ModuleProfileInfo {
    public ModuleProfileInfo() {}

    public ModuleProfileInfo(string name) {
        Name = name;
    }

    public string Name { get; set; }
    public double Percentage { get; set; }
    public TimeSpan Weight { get; set; }
}