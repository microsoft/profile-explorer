// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling;

/// <summary>
/// A node in a profiling call tree representing one function in a specific call path.
/// </summary>
public class CallTreeNode {
  private readonly List<CallTreeNode> children_ = [];
  private readonly List<CallTreeNode> callers_ = [];
  private readonly List<CallSite> callSites_ = [];
  private readonly Dictionary<int, ThreadWeight> threadWeights_ = [];

  public CallTreeNode(string moduleName, string functionName, long functionRva, CallTreeNodeKind kind) {
    ModuleName = moduleName;
    FunctionName = functionName;
    FunctionRva = functionRva;
    Kind = kind;
  }

  /// <summary>Module/image name.</summary>
  public string ModuleName { get; }

  /// <summary>Function name.</summary>
  public string FunctionName { get; }

  /// <summary>Function RVA.</summary>
  public long FunctionRva { get; }

  /// <summary>Total time including all descendants (inclusive weight).</summary>
  public TimeSpan InclusiveWeight { get; internal set; }

  /// <summary>Self time only — leaf samples (exclusive weight).</summary>
  public TimeSpan ExclusiveWeight { get; internal set; }

  /// <summary>Inclusive weight as percentage of total trace time.</summary>
  public double InclusivePercent { get; internal set; }

  /// <summary>Exclusive weight as percentage of total trace time.</summary>
  public double ExclusivePercent { get; internal set; }

  /// <summary>Child nodes (callees from this function).</summary>
  public IReadOnlyList<CallTreeNode> Children => children_;

  /// <summary>Parent nodes (callers of this function).</summary>
  public IReadOnlyList<CallTreeNode> Callers => callers_;

  /// <summary>Per-thread weight breakdown.</summary>
  public IReadOnlyDictionary<int, ThreadWeight> ThreadWeights => threadWeights_;

  /// <summary>Whether this is native user, native kernel, or managed code.</summary>
  public CallTreeNodeKind Kind { get; }

  /// <summary>Call sites within this function that call into children.</summary>
  public IReadOnlyList<CallSite> CallSites => callSites_;

  /// <summary>Qualified name in "module!function" format.</summary>
  public string QualifiedName => $"{ModuleName}!{FunctionName}";

  internal void AddChild(CallTreeNode child) {
    children_.Add(child);
  }

  internal void AddCaller(CallTreeNode caller) {
    if (!callers_.Contains(caller)) {
      callers_.Add(caller);
    }
  }

  internal void AddCallSite(CallSite site) {
    callSites_.Add(site);
  }

  internal void AccumulateWeight(TimeSpan weight) {
    InclusiveWeight += weight;
  }

  internal void AccumulateExclusiveWeight(TimeSpan weight) {
    ExclusiveWeight += weight;
  }

  internal void AccumulateThreadWeight(int threadId, TimeSpan inclusive, TimeSpan exclusive) {
    if (threadWeights_.TryGetValue(threadId, out var existing)) {
      threadWeights_[threadId] = new ThreadWeight(
        existing.Inclusive + inclusive,
        existing.Exclusive + exclusive);
    }
    else {
      threadWeights_[threadId] = new ThreadWeight(inclusive, exclusive);
    }
  }

  public override string ToString() =>
    $"{QualifiedName} (Incl: {InclusiveWeight.TotalMilliseconds:F1}ms, Excl: {ExclusiveWeight.TotalMilliseconds:F1}ms)";
}

/// <summary>
/// A call site within a function — a specific instruction that calls other functions.
/// </summary>
public class CallSite {
  private readonly List<(CallTreeNode Target, TimeSpan Weight)> targets_ = [];

  public CallSite(long rva) {
    Rva = rva;
  }

  /// <summary>RVA of the call instruction.</summary>
  public long Rva { get; }

  /// <summary>Total weight through this call site.</summary>
  public TimeSpan Weight { get; internal set; }

  /// <summary>Target functions called from this site (supports polymorphic/indirect calls).</summary>
  public IReadOnlyList<(CallTreeNode Target, TimeSpan Weight)> Targets => targets_;

  internal void AddTarget(CallTreeNode target, TimeSpan weight) {
    targets_.Add((target, weight));
    Weight += weight;
  }
}

/// <summary>Per-thread inclusive and exclusive weight.</summary>
public readonly record struct ThreadWeight(TimeSpan Inclusive, TimeSpan Exclusive);

/// <summary>Kind of code a call tree node represents.</summary>
public enum CallTreeNodeKind {
  NativeUser,
  NativeKernel,
  Managed
}
