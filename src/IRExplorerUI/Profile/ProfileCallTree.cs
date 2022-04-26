using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using IRExplorerCore;
using IRExplorerUI.Compilers;

namespace IRExplorerUI.Profile;

public class ProfileCallTree {
    private HashSet<ProfileCallTreeNode> rootNodes_;
    private Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcToNodesMap_;
    private ReaderWriterLockSlim lock_;
    private ReaderWriterLockSlim funcLock_;

    public HashSet<ProfileCallTreeNode> RootNodes => rootNodes_;

    public ProfileCallTree() {
        rootNodes_ = new HashSet<ProfileCallTreeNode>();
        funcToNodesMap_ = new Dictionary<IRTextFunction, List<ProfileCallTreeNode>>();
        lock_ = new ReaderWriterLockSlim();
        funcLock_ = new ReaderWriterLockSlim();
    }

    public ProfileCallTreeNode AddRootNode(DebugFunctionInfo funcInfo, IRTextFunction function) {
        var node = new ProfileCallTreeNode(funcInfo, function);
        lock_.EnterUpgradeableReadLock();

        try {
            if (rootNodes_.TryGetValue(node, out var existingNode)) {
                return existingNode;
            }

            lock_.EnterWriteLock();
            try {
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

    public ProfileCallTreeNode AddChildNode(ProfileCallTreeNode node, DebugFunctionInfo funcInfo, IRTextFunction function) {
        var (childNode, isNewNode) = node.AddChild(funcInfo, function);

        if (isNewNode) {
            RegisterFunctionTreeNode(childNode);
        }

        return childNode;
    }

    public void RegisterFunctionTreeNode(ProfileCallTreeNode node) {
        List<ProfileCallTreeNode> nodeList = null;
        funcLock_.EnterUpgradeableReadLock();

        try {
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

    public List<ProfileCallTreeNode> GetCallTreeNodes(IRTextFunction function) {
        funcLock_.EnterReadLock();

        try {
            if (funcToNodesMap_.TryGetValue(function, out var nodeList)) {
                return nodeList;
            }
        }
        finally {
            funcLock_.ExitReadLock();
        }

        return null;
    }

    public ProfileCallTreeNode GetCombinedCallTreeNode(IRTextFunction function) {
        var nodes = GetCallTreeNodes(function);

        if (nodes == null) {
            return null;
        }
        else if (nodes.Count == 1) {
            return nodes[0];
        }

        var childrenSet = new HashSet<ProfileCallTreeNode>();
        var callersSet = new HashSet<ProfileCallTreeNode>();
        TimeSpan weight = TimeSpan.Zero;
        TimeSpan excWeight = TimeSpan.Zero;

        foreach (var node in nodes) {
            weight += node.Weight;
            excWeight += node.ExclusiveWeight;

            if (node.HasChildren) {
                foreach (var childNode in node.Children) {
                    childrenSet.Add(childNode);
                }
            }

            if (node.HasCallers) {
                foreach (var callerNode in node.Callers) {
                    callersSet.Add(callerNode);
                }
            }
        }

        return new ProfileCallTreeNode(nodes[0].DebugInfo, nodes[0].Function,
                                       childrenSet.ToList(), callersSet.ToList()) {
            Weight = weight, ExclusiveWeight = excWeight
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


public class ProfileCallTreeNode : IEquatable<ProfileCallTreeNode> {
    public IRTextFunction Function { get; set; }
    public DebugFunctionInfo DebugInfo { get; set; }
    public TimeSpan Weight { get; set; }
    public TimeSpan ExclusiveWeight { get; set; }

    private List<ProfileCallTreeNode> children_;
    private List<ProfileCallTreeNode> callers_;
    private ReaderWriterLockSlim lock_;

    public List<ProfileCallTreeNode> Children => children_;

    public List<ProfileCallTreeNode> Callers => callers_;

    public bool HasChildren => Children != null && Children.Count > 0;
    public bool HasCallers => Callers != null && Callers.Count > 0;
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

    public ProfileCallTreeNode(DebugFunctionInfo funcInfo, IRTextFunction function,
        List<ProfileCallTreeNode> children = null,
        List<ProfileCallTreeNode> callers = null) {
        DebugInfo = funcInfo;
        Function = function;
        children_ = children;
        callers_ = callers;
        lock_ = new ReaderWriterLockSlim();
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
        
    public (ProfileCallTreeNode, bool) AddChild(DebugFunctionInfo debugInfo, IRTextFunction function) {
        var (childNode, isNewNode) = GetOrCreateChildNode(debugInfo, function);

        if (isNewNode) {
            childNode.AddParent(this);
        }

        return (childNode, isNewNode);
    }
        
    public void RecordSample(ProfileSample sample, ResolvedProfileStackFrame stackFrame) {
        //lock_.EnterWriteLock();
        //try {
        //    samples_ ??= new Dictionary<long, ProfileSample>();
        //    samples_[stackFrame.FrameIP] = sample;
        //    sampleCount_++;
        //}
        //finally {
        //    lock_.ExitWriteLock();
        //}
    }

    private void AddParent(ProfileCallTreeNode parentNode) {
        lock_.EnterUpgradeableReadLock();
        ref var list = ref callers_;
        try {
            var childNode = FindExistingNode(ref list, parentNode.DebugInfo, parentNode.Function);
            if (childNode != null) {
                return;
            }

            lock_.EnterWriteLock();
            try {
                list ??= new List<ProfileCallTreeNode>();
                list.Add(parentNode);
            }
            finally {
                lock_.ExitWriteLock();
            }
        }
        finally {
            lock_.ExitUpgradeableReadLock();
        }
    }

    private (ProfileCallTreeNode, bool) GetOrCreateChildNode(DebugFunctionInfo debugInfo, IRTextFunction function) {
        lock_.EnterUpgradeableReadLock();
        ref var list = ref children_;

        try {
            var childNode = FindExistingNode(ref list, debugInfo, function);

            if (childNode != null) {
                return (childNode, false);
            }

            lock_.EnterWriteLock();
            try {
                list ??= new List<ProfileCallTreeNode>();
                childNode = new ProfileCallTreeNode(debugInfo, function);
                list.Add(childNode);
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

    private ProfileCallTreeNode FindExistingNode(ref List<ProfileCallTreeNode> list,
        DebugFunctionInfo debugInfo, IRTextFunction function) {
        if (list != null) {
            foreach (var child in list) {
                if (child.Equals(debugInfo, function)) {
                    return child;
                }
            }
        }

        return null;
    }

    internal void Print(StringBuilder builder, int level = 0, bool caller = false) {
        builder.Append(new string(' ', level * 4));
        builder.AppendLine($"{DebugInfo.Name}, RVA {DebugInfo.RVA}");
        builder.Append(new string(' ', level * 4));
        builder.AppendLine($"        weight {Weight.TotalMilliseconds}");
        builder.Append(new string(' ', level * 4));
        builder.AppendLine($"    exc weight {ExclusiveWeight.TotalMilliseconds}");
        builder.Append(new string(' ', level * 4));
        builder.AppendLine($"    callees: {(Children != null ? Children.Count : 0)}");

        if (Children != null && !caller) {
            foreach (var child in Children) {
                child.Print(builder, level + 1);
            }
        }

        if (Callers != null) {
            builder.AppendLine($"Callers: {Callers.Count}");
            foreach (var child in Callers) {
                child.Print(builder, level + 1, true);
            }

            builder.AppendLine("-------------------------------------------------");
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
        
    public bool Equals(DebugFunctionInfo debugInfo, IRTextFunction function) {
        return Function.Equals(function) &&
               DebugInfo.Equals(debugInfo);
    }

    public bool Equals(ProfileCallTreeNode other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Function.Equals(other.Function) &&
               DebugInfo.Equals(other.DebugInfo);
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
        return HashCode.Combine(Function, DebugInfo);
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
