using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using ProtoBuf;
using static IRExplorerUI.Profile.ProfileData;

namespace IRExplorerUI.Profile;

public class ProfileCallTree {
    [ProtoContract(SkipConstructor = true)]
    public class ProfileCallTreeState {
        [ProtoMember(1)] public HashSet<ProfileCallTreeNode> RootNodes;
        [ProtoMember(2)] public Dictionary<IRTextFunctionId, List<ProfileCallTreeNode>> FuncToNodesMap;
        [ProtoMember(3)] public long NextNodeId;

        public ProfileCallTreeState(int funcCount) {
            FuncToNodesMap = new Dictionary<IRTextFunctionId, List<ProfileCallTreeNode>>(funcCount);
        }
    }

    // Comparer used for the root nodes in order to ignore the ID part.
    private class ProfileCallTreeNodeComparer : IEqualityComparer<ProfileCallTreeNode> {
        public bool Equals(ProfileCallTreeNode x, ProfileCallTreeNode y) {
            return x.Equals(y.FunctionDebugInfo, y.Function);
        }

        public int GetHashCode(ProfileCallTreeNode obj) {
            return HashCode.Combine(obj.Function, obj.FunctionDebugInfo);
        }
    }

    private HashSet<ProfileCallTreeNode> rootNodes_;
    private Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcToNodesMap_;
    private Dictionary<long, ProfileCallTreeNode> nodeIdMap_;
    private long nextNodeId_;
    private ReaderWriterLockSlim lock_;
    private ReaderWriterLockSlim funcLock_;

    public HashSet<ProfileCallTreeNode> RootNodes => rootNodes_;
    public TimeSpan TotalRootNodesWeight {
        get {
            TimeSpan sum = TimeSpan.Zero;
            foreach (var node in rootNodes_) {
                sum += node.Weight;
            }
            return sum;
        }
    }

    public byte[] Serialize() {
        var state = new ProfileCallTreeState(funcToNodesMap_.Count);
        state.RootNodes = rootNodes_;
        state.NextNodeId = nextNodeId_;
        
        foreach (var pair in funcToNodesMap_) {
            var funcId = new IRTextFunctionId(pair.Key);
            state.FuncToNodesMap[funcId] = pair.Value;
        }

        return StateSerializer.Serialize(state);
    }

    public static ProfileCallTree Deserialize(byte[] data, Dictionary<Guid, IRTextSummary> summaryMap) {
        var state = StateSerializer.Deserialize<ProfileCallTreeState>(data);
        var callTree = new ProfileCallTree();
        var nodesSet = new HashSet<ProfileCallTreeNode>();

        // The call tree has an unique node for each function.
        // Sadly deserializing doesn't guarantee this is the case, so collect
        // all nodes and replace all root nodes and children with the unique instances.
        foreach (var pair in state.FuncToNodesMap) {
            foreach (var node in pair.Value) {
                nodesSet.Add(node);
            }
        }

        callTree.nextNodeId_ = state.NextNodeId;
        callTree.rootNodes_ = new HashSet<ProfileCallTreeNode>(callTree.rootNodes_.Count);

        foreach(var node in state.RootNodes) {
            if (nodesSet.TryGetValue(node, out var uniqueNode)) {
                callTree.rootNodes_.Add(uniqueNode);
                uniqueNode.InitializeFunction(summaryMap);
            }
        }

        foreach (var pair in state.FuncToNodesMap) {
            var summary = summaryMap[pair.Key.SummaryId];
            var function = summary.GetFunctionWithId(pair.Key.FunctionNumber);

            if (function == null) {
                Debug.Assert(false, "Could not find node for func");
                continue;
            }

            var nodeList = new List<ProfileCallTreeNode>(pair.Value.Count);

            foreach (var node in pair.Value) {
                if (!nodesSet.TryGetValue(node, out var funcNode)) {
                    Debug.Assert(false, "Could not find node for func");
                    continue;
                }

                nodeList.Add(funcNode);
                funcNode.InitializeFunction(summaryMap);

                // Reconstruct the parent node list, since it can't be
                // serialized due to recursion issues.
                if (funcNode.HasChildren) {
                    int count = funcNode.Children.Count;

                    for(int i = 0; i < count; i++) {
                        var childNode = funcNode.Children[i];

                        if (nodesSet.TryGetValue(childNode, out var uniqueNode) && 
                            !ReferenceEquals(childNode, uniqueNode)) {
                            funcNode.Children[i] = uniqueNode;
                            childNode = uniqueNode;
                        }

                        childNode.InitializeFunction(summaryMap);
                        childNode.AddParentNoLock(funcNode);
                    }
                }
            }

            callTree.funcToNodesMap_[function] = nodeList;
        }
        
        return callTree;
    }

    public ProfileCallTree() {
        InitializeReferenceMembers();
    }

    [ProtoAfterDeserialization]
    private void InitializeReferenceMembers() {
        rootNodes_ ??= new HashSet<ProfileCallTreeNode>(new ProfileCallTreeNodeComparer());
        funcToNodesMap_ ??= new Dictionary<IRTextFunction, List<ProfileCallTreeNode>>();
        lock_ ??= new ReaderWriterLockSlim();
        funcLock_ ??= new ReaderWriterLockSlim();
    }

    public ProfileCallTreeNode AddRootNode(FunctionDebugInfo funcInfo, IRTextFunction function) {
        var node = new ProfileCallTreeNode(funcInfo, function);
        lock_.EnterUpgradeableReadLock();

        try {
            if (rootNodes_.TryGetValue(node, out var existingNode)) {
                return existingNode;
            }

            lock_.EnterWriteLock();
            try {
                if (rootNodes_.TryGetValue(node, out existingNode)) {
                    return existingNode;
                }

                rootNodes_.Add(node);
                RegisterFunctionTreeNode(node);
            }
            finally {
                lock_.ExitWriteLock();
            }
        }
        finally {
            lock_.ExitUpgradeableReadLock();
        }

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

    public ProfileCallTreeNode GetCombinedCallTreeNode(IRTextFunction function, ProfileCallTreeNode parentNode = null) {
        var nodes = GetCallTreeNodes(function);

        if (nodes == null) {
            return null;
        }
        else if (nodes.Count == 1) {
            return nodes[0];
        }

        var childrenSet = new HashSet<ProfileCallTreeNode>();
        var callersSet = new HashSet<ProfileCallTreeNode>();
        var callSiteMap = new Dictionary<long, ProfileCallSite>();
        TimeSpan weight = TimeSpan.Zero;
        TimeSpan excWeight = TimeSpan.Zero;

        foreach (var node in nodes) {
            // When the function is a callee, consider only the nodes that are actually being called
            // by the parent node - by default the list contains every node representing the function,
            // on all paths through the call tree.
            if (parentNode != null && !node.HasParent(parentNode)) {
                continue;
            }

            weight += node.Weight;
            excWeight += node.ExclusiveWeight;

            if (node.HasChildren) {
                foreach (var childNode in node.Children) {
                    if (!childrenSet.TryGetValue(childNode, out var existingNode)) {
                        existingNode = new ProfileCallTreeNode(childNode.FunctionDebugInfo, childNode.Function);
                        childrenSet.Add(existingNode);
                    }                        

                    existingNode.Weight += childNode.Weight;
                    existingNode.ExclusiveWeight += childNode.ExclusiveWeight;
                }
            }

            if (node.HasCallers) {
                foreach (var callerNode in node.Callers) {
                    if (!callersSet.TryGetValue(callerNode, out var existingNode)) {
                        existingNode = new ProfileCallTreeNode(callerNode.FunctionDebugInfo, callerNode.Function);
                        callersSet.Add(existingNode);
                    }

                    existingNode.Weight += callerNode.Weight;
                    existingNode.ExclusiveWeight += callerNode.ExclusiveWeight;
                }
            }

            if (node.HasCallSites) {
                foreach (var pair in node.CallSites) {
                    if (!callSiteMap.TryGetValue(pair.Key, out var callsite)) {
                        callsite = new ProfileCallSite(pair.Key);
                        callSiteMap[pair.Key] = callsite;
                    }

                    foreach (var target in pair.Value.Targets) {
                        callsite.AddTarget(target.NodeId, target.Weight);
                    }
                }
            }
        }

        return new ProfileCallTreeNode(nodes[0].FunctionDebugInfo, nodes[0].Function,
                                       childrenSet.ToList(), callersSet.ToList(), callSiteMap) {
            Weight = weight, ExclusiveWeight = excWeight,
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

    public string Print() {
        var builder = new StringBuilder();

        foreach (var node in rootNodes_) {
            builder.AppendLine("Call tree root node");
            builder.AppendLine("-----------------------");
            node.Print(builder);
        }

        return builder.ToString();
    }

    public string PrintSamples() {
        var samples = new List<(int total, int unique, string name)>();
        var builder = new StringBuilder();

        foreach (var node in rootNodes_) {
            node.CollectSamples(samples);
        }

        samples.Sort((a, b) => b.total.CompareTo(a.total));
        int total = 0;
        int unique = 0;

        foreach (var sample in samples) {
            builder.AppendLine($"{sample.name}");
            builder.AppendLine($"   {sample.total} samples, {sample.unique} unique");
            builder.AppendLine($"   {100 * (double)sample.unique / sample.total:F4} unique");
            total += sample.total;
            unique += sample.unique;
        }

        builder.AppendLine($"{total} total samples, {unique} unique");
        builder.AppendLine($"     {100 * (double)unique / total:F4} unique");
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
    private IRTextFunctionReference functionRef_ { get; set; }
    [ProtoMember(2)]
    private List<ProfileCallTreeNode> children_;
    [ProtoMember(3)] 
    private Dictionary<long, ProfileCallSite> callSites_;

    private List<ProfileCallTreeNode> callers_; // Can't be serialized, reconstructed.
    private ReaderWriterLockSlim lock_; // Lock for updating children, callers, call sites.

    [ProtoMember(4)]
    public long Id { get; set; }
    [ProtoMember(5)]
    public FunctionDebugInfo FunctionDebugInfo { get; set; }
    [ProtoMember(6)]
    public TimeSpan Weight { get; set; }
    [ProtoMember(7)]
    public TimeSpan ExclusiveWeight { get; set; }
    [ProtoMember(8)]
    public ProfileCallTreeNodeKind Kind { get; set; }

    public IRTextFunction Function {
        get => functionRef_;
        set => functionRef_ = value;
    }

    public List<ProfileCallTreeNode> Children => children_;
    public List<ProfileCallTreeNode> Callers => callers_;
    public Dictionary<long, ProfileCallSite> CallSites => callSites_;

    public bool HasChildren => Children != null && Children.Count > 0;
    public bool HasCallers => Callers != null && Callers.Count > 0;
    public bool HasCallSites => CallSites != null && CallSites.Count > 0;
    public string FunctionName => Function.Name;
    public string ModuleName => Function.ParentSummary.ModuleName;

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

    public ProfileCallTreeNode(FunctionDebugInfo funcInfo, IRTextFunction function,
        List<ProfileCallTreeNode> children = null,
        List<ProfileCallTreeNode> callers = null,
        Dictionary<long, ProfileCallSite> callSites = null) {
        InitializeReferenceMembers();
        FunctionDebugInfo = funcInfo;
        Function = function;
        children_ = children;
        callers_ = callers;
        callSites_ = callSites;
    }

    [ProtoAfterDeserialization]
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
        lock_.EnterWriteLock();
        Weight += weight;
        lock_.ExitWriteLock();
    }

    public void AccumulateExclusiveWeight(TimeSpan weight) {
        lock_.EnterWriteLock();
        ExclusiveWeight += weight;
        lock_.ExitWriteLock();
    }
        
    public (ProfileCallTreeNode, bool) AddChild(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
        var (childNode, isNewNode) = GetOrCreateChildNode(functionDebugInfo, function);

        if (isNewNode) {
            childNode.AddParent(this);
        }

        return (childNode, isNewNode);
    }
    
    private void AddParent(ProfileCallTreeNode parentNode) {
        try {
            lock_.EnterUpgradeableReadLock();
            var callerNode = FindExistingNode(callers_, parentNode.FunctionDebugInfo, parentNode.Function);
            if (callerNode != null) {
                return;
            }

            try {
                // Check again if another thread added the parent in the meantime.
                lock_.EnterWriteLock();
                callerNode = FindExistingNode(callers_, parentNode.FunctionDebugInfo, parentNode.Function);
                if (callerNode != null) {
                    return;
                }

                callers_ ??= new List<ProfileCallTreeNode>();
                callers_.Add(parentNode);
            }
            finally {
                lock_.ExitWriteLock();
            }
        }
        finally {
            lock_.ExitUpgradeableReadLock();
        }
    }

    internal void AddParentNoLock(ProfileCallTreeNode parentNode) {
        var callerNode = FindExistingNode(callers_, parentNode.FunctionDebugInfo, parentNode.Function);
        if (callerNode != null) {
            return;
        }

        callers_ ??= new List<ProfileCallTreeNode>();
        callers_.Add(parentNode);
    }

    public bool HasParent(ProfileCallTreeNode parentNode) {
        if (Callers == null) {
            return false;
        }

        return Callers.IndexOf(parentNode) != -1;
    }

    private (ProfileCallTreeNode, bool) GetOrCreateChildNode(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
        try {
            lock_.EnterUpgradeableReadLock();
            var childNode = FindExistingNode(children_, functionDebugInfo, function);

            if (childNode != null) {
                return (childNode, false);
            }

            try {
                // Check again if another thread added the child in the meantime.
                lock_.EnterWriteLock();
                childNode = FindExistingNode(children_, functionDebugInfo, function);

                if (childNode != null) {
                    return (childNode, false);
                }

                children_ ??= new List<ProfileCallTreeNode>();
                childNode = new ProfileCallTreeNode(functionDebugInfo, function);
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

            callsite.AddTarget(childNode.Id, weight);
        }
        finally {
            lock_.ExitWriteLock();
        }
    }

    private ProfileCallTreeNode FindExistingNode(List<ProfileCallTreeNode> list,
                                                 FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
        if (list != null) {
            foreach (var child in list) {
                if (child.Equals(functionDebugInfo, function)) {
                    return child;
                }
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

    public void CollectSamples(List<(int total, int unique, string name)> list) {
        //list.Add((sampleCount_, samples_.Count, FunctionName));

        //if (HasChildren) {
        //    foreach (var child in Children) {
        //        child.CollectSamples(list);
        //    }
        //}
    }
        
    public bool Equals(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
        return Function.Equals(function) &&
               FunctionDebugInfo.Equals(functionDebugInfo);
    }

    public bool Equals(ProfileCallTreeNode other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Id == other.Id &&
               FunctionDebugInfo.Equals(other.FunctionDebugInfo) &&
               Kind == other.Kind;
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
        return HashCode.Combine(Id, FunctionDebugInfo, Kind);
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

[ProtoContract(SkipConstructor = true)]
public class ProfileCallSite : IEquatable<ProfileCallSite> {
    [ProtoMember(1)]
    public long RVA { get; set; }
    [ProtoMember(2)]
    public TimeSpan Weight { get; set; }
    [ProtoMember(3)]
    public List<(long NodeId, TimeSpan Weight)> Targets { get; set; }

    public bool HasSingleTarget => Targets.Count == 1;

    public ProfileCallSite(long rva) {
        InitializeReferenceMembers();
        RVA = rva;
        Weight = TimeSpan.Zero;
    }

    public double ScaleWeight(TimeSpan weight) {
        return (double)weight.Ticks / (double)Weight.Ticks;
    }

    public void AddTarget(long nodeId, TimeSpan weight) {
        Weight += weight; // Total weight of targets.
        int index = Targets.FindIndex(item => item.NodeId == nodeId);

        if (index != -1) {
            var span = CollectionsMarshal.AsSpan(Targets);
            span[index].Weight += weight; // Modify in-place per-target weight.
        }
        else {
            Targets.Add((nodeId, weight));
        }
    }


    [ProtoAfterDeserialization]
    private void InitializeReferenceMembers() {
        Targets ??= new List<(long NodeId, TimeSpan Weight)>();
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
