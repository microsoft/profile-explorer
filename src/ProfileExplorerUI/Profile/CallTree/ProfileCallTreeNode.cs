// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Collections;
using ProfileExplorer.UI.Binary;
using ProfileExplorer.UI.Compilers;

namespace ProfileExplorer.UI.Profile;

public class ProfileCallTreeNode : IEquatable<ProfileCallTreeNode> {
  private static readonly object MergedNodeTag = new();
  public int Id { get; set; }
  public IRTextFunction Function { get; set; }
  public ProfileCallTreeNodeKind Kind { get; set; }
  private TinyList<ProfileCallTreeNode> children_;
  private ProfileCallTreeNode caller_; // Can't be serialized, reconstructed.
  public FunctionDebugInfo FunctionDebugInfo { get; set; }

  //? TODO: Replace Threads dict and CallSites with a TinyDictionary-like data struct
  //? like TinyList, also consider DictionarySlim instead of Dictionary from
  //? https://github.com/dotnet/corefxlab/blob/archive/src/Microsoft.Experimental.Collections/Microsoft/Collections/Extensions/DictionarySlim
  public Dictionary<long, ProfileCallSite> CallSites { get; set; }
  public Dictionary<int, (TimeSpan Weight, TimeSpan ExclusiveWeight)> ThreadWeights { get; set; }
  public TimeSpan Weight { get; set; }
  public TimeSpan ExclusiveWeight { get; set; }
  public object Tag { get; set; }
  public virtual List<ProfileCallTreeNode> Nodes => new() {this};
  public IList<ProfileCallTreeNode> Children => children_;
  public virtual List<ProfileCallTreeNode> Callers => new() {caller_};
#if DEBUG
  public ProfileCallTreeNode Caller =>
    !IsGroup ? caller_ : throw new InvalidOperationException("For group use Callers");
#else
  public ProfileCallTreeNode Caller => caller_;
#endif
  public virtual bool IsGroup => false;
  public bool HasChildren => Children != null && Children.Count > 0;
  public virtual bool HasCallers => caller_ != null;
  public bool HasCallSites => CallSites != null && CallSites.Count > 0;
  public bool HasThreadWeights => ThreadWeights != null && ThreadWeights.Count > 0;
  public bool HasFunction => Function != null;
  public string FunctionName => Function.Name;
  public string ModuleName => Function.ModuleName;

  public double ScaleWeight(TimeSpan relativeWeigth) {
    return relativeWeigth.Ticks / (double)Weight.Ticks;
  }

  public (TimeSpan Weight, TimeSpan ExclusiveWeight) ChildrenWeight {
    get {
      var weight = TimeSpan.Zero;
      var exclusiveWeight = TimeSpan.Zero;

      if (!HasChildren) {
        return (weight, exclusiveWeight);
      }

      foreach (var child in Children) {
        weight += child.Weight;
        exclusiveWeight += child.ExclusiveWeight;
      }

      return (weight, exclusiveWeight);
    }
  }

  protected ProfileCallTreeNode() { }

  public ProfileCallTreeNode(FunctionDebugInfo funcInfo, IRTextFunction function,
                             List<ProfileCallTreeNode> children = null,
                             ProfileCallTreeNode caller = null,
                             Dictionary<long, ProfileCallSite> callSites = null,
                             Dictionary<int, (TimeSpan, TimeSpan)> threadWeights = null) {
    FunctionDebugInfo = funcInfo;
    Function = function;
    ThreadWeights = threadWeights ?? new Dictionary<int, (TimeSpan, TimeSpan)>();
    children_ = new TinyList<ProfileCallTreeNode>(children);
    caller_ = caller;
    CallSites = callSites;
  }

  public void AccumulateWeight(TimeSpan weight) {
    Weight += weight;
  }

  public void AccumulateWeight(TimeSpan weight, TimeSpan exclusiveWeight, int threadId) {
    ThreadWeights.AccumulateValue(threadId, weight, exclusiveWeight);
  }

  public List<(int ThreadId, (TimeSpan Weight, TimeSpan ExclusiveWeight) Values)>
    SortedByWeightPerThreadWeights {
    get {
      var list = ThreadWeights.ToList();
      list.Sort((a, b) => b.Item2.Weight.CompareTo(a.Item2.Weight));
      return list;
    }
  }

  public List<(int ThreadId, (TimeSpan Weight, TimeSpan ExclusiveWeight) Values)>
    SortedByIdPerThreadWeights {
    get {
      var list = ThreadWeights.ToList();
      list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
      return list;
    }
  }

  public void AccumulateExclusiveWeight(TimeSpan weight) {
    ExclusiveWeight += weight;
  }

  public (ProfileCallTreeNode, bool) AddChild(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
    return GetOrCreateChildNode(functionDebugInfo, function);
  }

  public bool HasChild(ProfileCallTreeNode node) {
    return children_.Contains(node);
  }

  public ProfileCallTreeNode FindChildNode(IRTextFunction function) {
    return children_.Find(node => node.Function == function);
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

  private (ProfileCallTreeNode, bool)
    GetOrCreateChildNode(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
    var childNode = FindExistingNode(functionDebugInfo, function);

    if (childNode != null) {
      return (childNode, false);
    }

    childNode = new ProfileCallTreeNode(functionDebugInfo, function, null, this);
    children_.Add(childNode);
    return (childNode, true);
  }

  public void AddCallSite(ProfileCallTreeNode childNode, long rva, TimeSpan weight) {
    CallSites ??= new Dictionary<long, ProfileCallSite>();
    ref var callsite = ref CollectionsMarshal.GetValueRefOrAddDefault(CallSites, rva, out bool exists);

    if (!exists) {
      callsite = new ProfileCallSite(rva);
    }

    callsite.AddTarget(childNode, weight);
  }

  private ProfileCallTreeNode FindExistingNode(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
    for (int i = 0; i < children_.Count; i++) {
      var child = children_[i];

      if (child.Equals(function)) {
        return child;
      }
    }

    return null;
  }

  public void MergeWith(ProfileCallTreeNode otherNode) {
    // Accumulate the weights and merge all data structures,
    // then recursively merge the common child nodes
    // and copy over any new child nodes.
    otherNode.Tag = MergedNodeTag; // Mark node as merged to be discarded later.
    Weight += otherNode.Weight;
    ExclusiveWeight += otherNode.ExclusiveWeight;

    if (otherNode.HasCallSites) {
      CallSites ??= new Dictionary<long, ProfileCallSite>();

      foreach (var callSite in otherNode.CallSites) {
        ref var existingCallSite =
          ref CollectionsMarshal.GetValueRefOrAddDefault(CallSites, callSite.Key, out bool exists);

        if (!exists) {
          existingCallSite = callSite.Value;
        }
        else {
          existingCallSite.MergeWith(callSite.Value);
        }
      }
    }

    if (otherNode.HasThreadWeights) {
      ThreadWeights ??= new Dictionary<int, (TimeSpan Weight, TimeSpan ExclusiveWeight)>();

      foreach (var threadWeight in otherNode.ThreadWeights) {
        AccumulateWeight(threadWeight.Value.Weight, threadWeight.Value.ExclusiveWeight, threadWeight.Key);
      }
    }

    if (otherNode.HasChildren) {
      foreach (var child in otherNode.children_) {
        var existingChild = FindChildNode(child.Function);

        if (existingChild != null) {
          // Recursively merge child nodes.
          existingChild.MergeWith(child);
        }
        else {
          // Copy over the child from the other node.
          children_.Add(child);
        }
      }
    }
  }

  public bool IsMergeNode() {
    return Tag == MergedNodeTag;
  }

  public void ClearIsMergedNode() {
    Tag = null;
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

    // Note that this holds only for nodes
    // belonging to the same ProfileCallTree instance.
    return Id == other.Id;
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj)) {
      return false;
    }

    if (ReferenceEquals(this, obj)) {
      return true;
    }

    if (obj.GetType() != GetType()) {
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
    return $"Name: {FunctionDebugInfo?.Name}\n" +
           $"RVA {FunctionDebugInfo.RVA}, Id {Id}\n" +
           $"Weight: {Weight}\n" +
           $"ExclusiveWeight: {ExclusiveWeight}\n" +
           $"Children: {Children?.Count ?? 0}\n" +
           $"CallSites: {CallSites?.Count ?? 0}";
  }

  public ProfileCallTreeNode Clone() {
    return new ProfileCallTreeNode {
      Id = Id,
      Kind = Kind,
      Function = Function,
      FunctionDebugInfo = FunctionDebugInfo,
      Weight = Weight,
      ExclusiveWeight = ExclusiveWeight,
      children_ = children_,
      caller_ = caller_,
      CallSites = CallSites
    };
  }
}

public sealed class ProfileCallTreeGroupNode : ProfileCallTreeNode {
  private List<ProfileCallTreeNode> nodes_;
  private List<ProfileCallTreeNode> callers_;

  public ProfileCallTreeGroupNode() {
  }

  public ProfileCallTreeGroupNode(FunctionDebugInfo funcInfo, IRTextFunction function,
                                  List<ProfileCallTreeNode> nodes = null,
                                  List<ProfileCallTreeNode> children = null,
                                  List<ProfileCallTreeNode> callers = null,
                                  Dictionary<long, ProfileCallSite> callSites = null,
                                  Dictionary<int, (TimeSpan, TimeSpan)> threadWeights = null) :
    base(funcInfo, function, children, null, callSites, threadWeights) {
    nodes_ = nodes ?? new List<ProfileCallTreeNode>();
    callers_ = callers ?? new List<ProfileCallTreeNode>();
  }

  public ProfileCallTreeGroupNode(FunctionDebugInfo funcInfo, IRTextFunction function,
                                  ProfileCallTreeNodeKind kind) :
    base(funcInfo, function) {
    nodes_ = new List<ProfileCallTreeNode>();
    Kind = kind;
  }

  public ProfileCallTreeGroupNode(ProfileCallTreeNode baseNode, TimeSpan weight) :
    this(baseNode.FunctionDebugInfo, baseNode.Function) {
    nodes_.Add(baseNode);
    Weight = weight;
  }

  public override bool IsGroup => true;
  public override List<ProfileCallTreeNode> Nodes => nodes_;
  public override List<ProfileCallTreeNode> Callers => callers_;
  public override bool HasCallers => callers_ != null && callers_.Count > 0;

  public override string ToString() {
    return $"{FunctionDebugInfo.Name}, RVA {FunctionDebugInfo.RVA}, Id {Id}, Nodes: {nodes_.Count}";
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