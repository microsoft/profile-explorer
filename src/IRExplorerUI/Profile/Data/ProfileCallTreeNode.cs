using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using IRExplorerCore;
using IRExplorerCore.Collections;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public class ProfileCallTreeNode : IEquatable<ProfileCallTreeNode> {
  [ProtoMember(1)]
  public IRTextFunction Function { get; set; }
  private TinyList<ProfileCallTreeNode> children_;
  //private SparseBitvector samplesIndices_;

  //? TODO: ProfileCallSite not serialized properly, references CallTreeNode and should use Id instead
  //[ProtoMember(3)]
  private Dictionary<long, ProfileCallSite> callSites_; //? Use Hybrid array/dict to save space
  private ProfileCallTreeNode caller_; // Can't be serialized, reconstructed.
  [ProtoMember(4)]
  public long Id { get; set; }
  [ProtoMember(5)]
  public FunctionDebugInfo FunctionDebugInfo { get; set; }
  [ProtoMember(6)] private TimeSpan weight_; // Weight saved as ticks to use Interlocked.Add
  [ProtoMember(7)] private TimeSpan exclusiveWeight_;
  [ProtoMember(8)]
  public ProfileCallTreeNodeKind Kind { get; set; }
  public object Tag { get; set; }
  
  //? TODO: Replace Threads dict and CallSites with a TinyDictionary-like data struct
  //? like TinyList, also consider DictionarySlim instead of Dictionary from
  //? https://github.com/dotnet/corefxlab/blob/archive/src/Microsoft.Experimental.Collections/Microsoft/Collections/Extensions/DictionarySlim
  public Dictionary<int, (TimeSpan Weight, TimeSpan ExclusiveWeight)> ThreadWeights { get; set; }

  public TimeSpan Weight {
    get => weight_;
    set => weight_ = value;
  }

  public TimeSpan ExclusiveWeight {
    get => exclusiveWeight_;
    set => exclusiveWeight_ = value;
  }

  public IList<ProfileCallTreeNode> Children => children_;
  public virtual List<ProfileCallTreeNode> Callers => new List<ProfileCallTreeNode> {caller_};
#if DEBUG
  public ProfileCallTreeNode Caller =>
    !IsGroup ? caller_ : throw new InvalidOperationException("For group use Callers");
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

  public ProfileCallTreeNode() { }

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
    callSites_ = callSites;
  }

  public void AccumulateWeight(TimeSpan weight) {
    weight_ += weight;
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
    exclusiveWeight_ += weight;
  }

  public (ProfileCallTreeNode, bool) AddChild(FunctionDebugInfo functionDebugInfo, IRTextFunction function) {
    return GetOrCreateChildNode(functionDebugInfo, function);
  }

  public bool HasChild(ProfileCallTreeNode node) {
    return children_.Contains(node);
  }

  public ProfileCallTreeNode FindChild(IRTextFunction function) {
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

    // Check again if another thread added the child in the meantime.
    childNode = FindExistingNode(functionDebugInfo, function);

    if (childNode != null) {
      return (childNode, false);
    }

    childNode = new ProfileCallTreeNode(functionDebugInfo, function, null, this);
    children_.Add(childNode);
    return (childNode, true);
  }

  public void AddCallSite(ProfileCallTreeNode childNode, long rva, TimeSpan weight) {
    if (callSites_ == null || !callSites_.TryGetValue(rva, out var callsite)) {
      callSites_ ??= new Dictionary<long, ProfileCallSite>();
      callsite = new ProfileCallSite(rva);
      callSites_[rva] = callsite;
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
      weight_ = weight_,
      exclusiveWeight_ = exclusiveWeight_,
      children_ = children_,
      caller_ = caller_,
      callSites_ = callSites_
    };
  }
}

public sealed class ProfileCallTreeGroupNode : ProfileCallTreeNode {
  private List<ProfileCallTreeNode> nodes_;
  private List<ProfileCallTreeNode> callers_;

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

  public ProfileCallTreeGroupNode(ProfileCallTreeNode baseNode, TimeSpan weight) :
    this(baseNode.FunctionDebugInfo, baseNode.Function) {
    nodes_.Add(baseNode);
    Weight = weight;
  }

  public override bool IsGroup => true;
  public List<ProfileCallTreeNode> Nodes => nodes_;
  public override List<ProfileCallTreeNode> Callers => callers_;
  public override bool HasCallers => callers_ != null && callers_.Count > 0;

  public override string ToString() {
    return $"{FunctionDebugInfo.Name}, RVA {FunctionDebugInfo.RVA}, Id {Id}, Nodes: {nodes_.Count}";
  }
}



[ProtoContract(SkipConstructor = true)]
public class ProfileCallSite : IEquatable<ProfileCallSite> {
  private bool isSorted_;

  public ProfileCallSite(long rva) {
    InitializeReferenceMembers();
    RVA = rva;
    Weight = TimeSpan.Zero;
  }

  [ProtoMember(1)]
  public long RVA { get; set; }
  [ProtoMember(2)]
  public TimeSpan Weight { get; set; }
  [ProtoMember(3)]
  public List<(ProfileCallTreeNode Node, TimeSpan Weight)> Targets { get; set; }

  public List<(ProfileCallTreeNode Node, TimeSpan Weight)> SortedTargets {
    get {
      if (!HasSingleTarget && !isSorted_) {
        Targets.Sort((a, b) => b.Weight.CompareTo(a.Weight));
      }

      return Targets;
    }
  }

  public bool HasSingleTarget => Targets.Count == 1;

  public static bool operator ==(ProfileCallSite left, ProfileCallSite right) {
    return Equals(left, right);
  }

  public static bool operator !=(ProfileCallSite left, ProfileCallSite right) {
    return !Equals(left, right);
  }

  public double ScaleWeight(TimeSpan weight) {
    return weight.Ticks / (double)Weight.Ticks;
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

    return Equals((ProfileCallSite)obj);
  }

  public override int GetHashCode() {
    return RVA.GetHashCode();
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

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    Targets ??= new List<(ProfileCallTreeNode NodeId, TimeSpan Weight)>();
  }

  public override string ToString() {
    return $"RVA: {RVA}, Weight: {Weight.TotalMilliseconds}, Targets: {Targets.Count}";
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