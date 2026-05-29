// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Concurrent;

namespace ProfileExplorer.Profiling.Profiling;

/// <summary>
/// Builds a profiling call tree from resolved stack samples.
/// Thread-safe — supports parallel chunk-based construction with merge.
/// </summary>
internal class CallTreeBuilder {
  private readonly ConcurrentDictionary<string, CallTreeNode> rootNodes_ = new(StringComparer.OrdinalIgnoreCase);
  private readonly IpResolver ipResolver_;
  private TimeSpan totalWeight_;
  private readonly object weightLock_ = new();

  public CallTreeBuilder(IpResolver ipResolver) {
    ipResolver_ = ipResolver;
  }

  /// <summary>
  /// Add samples with stack frames to the call tree.
  /// Stacks are expected to be leaf-first (index 0 = leaf, last index = root).
  /// </summary>
  public void AddSamples(IEnumerable<IProfileSample> samples) {
    TimeSpan batchWeight = TimeSpan.Zero;

    foreach (var sample in samples) {
      if (sample.StackFrames is not { Count: > 0 }) continue;

      batchWeight += sample.Weight;

      // Resolve all frames.
      var resolvedFrames = new List<ResolvedIp>(sample.StackFrames.Count);
      for (int i = sample.StackFrames.Count - 1; i >= 0; i--) { // Walk root-to-leaf
        var resolved = ipResolver_.Resolve(sample.StackFrames[i]);
        if (resolved != null) {
          resolvedFrames.Add(resolved);
        }
      }

      if (resolvedFrames.Count == 0) continue;

      // Build tree path root → leaf.
      CallTreeNode? parent = null;

      for (int i = 0; i < resolvedFrames.Count; i++) {
        var frame = resolvedFrames[i];
        bool isLeaf = i == resolvedFrames.Count - 1;
        string funcName = frame.FunctionName ?? $"<unknown+0x{frame.Rva:X}>";
        var kind = frame.IsManaged ? CallTreeNodeKind.Managed : CallTreeNodeKind.NativeUser;

        CallTreeNode node;

        if (parent == null) {
          // Root node.
          string rootKey = $"{frame.ModuleName}!{funcName}";
          node = rootNodes_.GetOrAdd(rootKey, _ => new CallTreeNode(frame.ModuleName, funcName, frame.Rva, kind));
        }
        else {
          // Child node — find or create.
          node = FindOrAddChild(parent, frame.ModuleName, funcName, frame.Rva, kind);
        }

        node.AccumulateWeight(sample.Weight);
        node.AccumulateThreadWeight(sample.ThreadId, sample.Weight, isLeaf ? sample.Weight : TimeSpan.Zero);

        if (isLeaf) {
          node.AccumulateExclusiveWeight(sample.Weight);
        }

        parent = node;
      }
    }

    lock (weightLock_) {
      totalWeight_ += batchWeight;
    }
  }

  /// <summary>
  /// Build the final call tree. Returns a synthetic root node containing all actual roots as children.
  /// </summary>
  public CallTreeNode Build() {
    var root = new CallTreeNode("[Root]", "[Root]", 0, CallTreeNodeKind.NativeUser);
    double totalMs = totalWeight_.TotalMilliseconds;

    foreach (var (_, node) in rootNodes_) {
      root.AddChild(node);
      root.AccumulateWeight(node.InclusiveWeight);
      ComputePercents(node, totalMs);
    }

    root.InclusivePercent = 100.0;
    return root;
  }

  private static CallTreeNode FindOrAddChild(CallTreeNode parent, string moduleName, string funcName,
                                              long rva, CallTreeNodeKind kind) {
    // Check existing children.
    foreach (var child in parent.Children) {
      if (string.Equals(child.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase) &&
          string.Equals(child.FunctionName, funcName, StringComparison.Ordinal)) {
        return child;
      }
    }

    // Create new child.
    var newChild = new CallTreeNode(moduleName, funcName, rva, kind);
    parent.AddChild(newChild);
    newChild.AddCaller(parent);
    return newChild;
  }

  private static void ComputePercents(CallTreeNode node, double totalMs) {
    if (totalMs > 0) {
      node.InclusivePercent = node.InclusiveWeight.TotalMilliseconds / totalMs * 100;
      node.ExclusivePercent = node.ExclusiveWeight.TotalMilliseconds / totalMs * 100;
    }

    foreach (var child in node.Children) {
      ComputePercents(child, totalMs);
    }
  }
}
