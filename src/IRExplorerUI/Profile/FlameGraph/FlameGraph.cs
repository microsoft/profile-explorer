// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using IRExplorerCore;

namespace IRExplorerUI.Profile;

public class FlameGraphNode : SearchableProfileItem, IEquatable<FlameGraphNode> {
  internal const double DefaultMargin = 4;
  internal const double ExtraValueMargin = 20;
  internal const double MinVisibleRectWidth = 4;
  internal const double RecomputeVisibleRectWidth = MinVisibleRectWidth * 4;
  internal const double MinVisibleWidth = 1;
  internal const int MaxTextParts = 3;
  private string functionName_;

  public FlameGraphNode(ProfileCallTreeNode callTreeNode, TimeSpan weight, int depth,
                        FunctionNameFormatter funcNameFormatter) :
    base(funcNameFormatter) {
    CallTreeNode = callTreeNode;
    Weight = weight;
    Depth = depth;
  }

  public virtual bool IsGroup => false;
  public ProfileCallTreeNode CallTreeNode { get; }
  public FlameGraphRenderer Owner { get; set; }
  public FlameGraphNode Parent { get; set; }
  public List<FlameGraphNode> Children { get; set; }
  public TimeSpan ChildrenWeight { get; set; }
  public int Depth { get; set; }
  public HighlightingStyle Style { get; set; }
  public Brush TextColor { get; set; }
  public Brush ModuleTextColor { get; set; }
  public Brush WeightTextColor { get; set; }
  public Brush PercentageTextColor { get; set; }
  public Rect Bounds { get; set; }
  public double PercentageTextPosition { get; set; }
  public uint RenderVersion { get; set; }
  public bool IsDummyNode { get; set; }
  public bool IsHidden { get; set; }
  public bool HasFunction => CallTreeNode != null;
  public bool HasChildren => Children is {Count: > 0};
  public IRTextFunction Function => CallTreeNode?.Function;
  public TimeSpan StartTime { get; set; }
  public TimeSpan EndTime { get; set; }
  public TimeSpan Duration => EndTime - StartTime;
  public override string ModuleName =>
    CallTreeNode is {HasFunction: true} ? CallTreeNode.ModuleName : null;

  public bool IsDummyNodeOrChild {
    get {
      if (IsDummyNode) {
        return true;
      }

      var node = Parent;

      while (node != null) {
        if (node.IsDummyNode) {
          return true;
        }

        node = node.Parent;
      }

      return false;
    }
  }

  public static bool operator ==(FlameGraphNode left, FlameGraphNode right) {
    return Equals(left, right);
  }

  public static bool operator !=(FlameGraphNode left, FlameGraphNode right) {
    return !Equals(left, right);
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

    return Equals((FlameGraphNode)obj);
  }

  public override int GetHashCode() {
    return CallTreeNode != null ? CallTreeNode.GetHashCode() : 0;
  }

  public bool Equals(FlameGraphNode other) {
    if (ReferenceEquals(null, other)) {
      return false;
    }

    if (ReferenceEquals(this, other)) {
      return true;
    }

    return Equals(CallTreeNode, other.CallTreeNode);
  }

  protected override string GetFunctionName() {
    return CallTreeNode is {HasFunction: true} ? CallTreeNode.FunctionName : null;
  }
}

public sealed class FlameGraphGroupNode : FlameGraphNode {
  public FlameGraphGroupNode(FlameGraphNode parentNode, int startIndex,
                             int replacedNodeCount, TimeSpan weight, int depth) :
    base(null, weight, depth, null) {
    Parent = parentNode;
    ReplacedStartIndex = startIndex;
    ReplacedNodeCount = replacedNodeCount;
  }

  public override bool IsGroup => true;
  public int ReplacedNodeCount { get; }
  public int ReplacedStartIndex { get; }
  public int ReplacedEndIndex => ReplacedStartIndex + ReplacedNodeCount;
}

public sealed class FlameGraph {
  private FlameGraphNode rootNode_;
  private TimeSpan rootWeight_;
  private TimeSpan profileStartTime_;
  private TimeSpan profileEndTime_;
  private double rootWeightReciprocal_;
  private double rootDurationReciprocal_;
  private double profileDurationReciprocal_;
  private FunctionNameFormatter nameFormatter_;

  public FlameGraph(ProfileCallTree callTree, FunctionNameFormatter nameFormatter) {
    CallTree = callTree;
    nameFormatter_ = nameFormatter;
  }

  public ProfileCallTree CallTree { get; set; }

  public FlameGraphNode RootNode {
    get => rootNode_;
    set {
      rootNode_ = value;

      if (rootNode_.Duration.Ticks != 0) {
        rootDurationReciprocal_ = 1.0 / rootNode_.Duration.Ticks;
        profileDurationReciprocal_ = 1.0 / (profileEndTime_ - profileStartTime_).Ticks;
      }
    }
  }

  public TimeSpan RootWeight {
    get => rootWeight_;
    set {
      rootWeight_ = value;

      if (rootWeight_.Ticks != 0) {
        rootWeightReciprocal_ = 1.0 / rootWeight_.Ticks;
      }
    }
  }

  public List<FlameGraphNode> GetNodes(ProfileCallTreeNode node) {
    var list = new List<FlameGraphNode>();
    AppendNodes(node, list);
    return list;
  }

  public void AppendNodes(ProfileCallTreeNode node, List<FlameGraphNode> resultList) {
    if (!node.IsGroup) {
      if (node.Tag is FlameGraphNode fgNode) {
        resultList.Add(fgNode);
      }

      return;
    }

    var groupNode = node as ProfileCallTreeGroupNode;

    foreach (var childNode in groupNode.Nodes) {
      if (childNode.Tag is FlameGraphNode fgNode) {
        resultList.Add(fgNode);
      }
    }
  }

  public FlameGraphNode GetFlameGraphNode(ProfileCallTreeNode callNode) {
    return callNode.Tag as FlameGraphNode;
  }

  public List<FlameGraphNode> SearchNodes(string text, bool caseInsensitive) {
    var nodes = new List<FlameGraphNode>();
    var searcher = new TextSearcher(text, caseInsensitive);
    SearchNodesImpl(RootNode, text, searcher, nodes);

    // Sort the nodes by weight, descending.
    nodes.Sort((a, b) => b.Weight.CompareTo(a.Weight));
    return nodes;
  }

  public void SearchNodesImpl(FlameGraphNode node, string text, TextSearcher searcher,
                              List<FlameGraphNode> nodes) {
    if (node.HasFunction) {
      var result = searcher.FirstIndexOf(node.FunctionName);

      if (result.HasValue) {
        node.SearchResult = result;
        nodes.Add(node);
      }
    }

    if (node.HasChildren) {
      foreach (var child in node.Children) {
        SearchNodesImpl(child, text, searcher, nodes);
      }
    }
  }

  public void ResetSearchResults(List<FlameGraphNode> nodes) {
    foreach (var node in nodes) {
      node.SearchResult = null;
    }
  }

  public List<FlameGraphNode> GetNodesInTimeRange(TimeSpan startTime, TimeSpan endTime) {
    var nodes = new List<FlameGraphNode>();
    GetNodesInTimeRangeImpl(RootNode, startTime, endTime, nodes);
    return nodes;
  }

  public void Build(ProfileCallTreeNode rootNode) {
    if (rootNode == null) {
      // Make on dummy root node that hosts all real root nodes.
      RootWeight = CallTree.TotalRootNodesWeight;
      var flameNode = CreateFlameGraphNode(null, RootWeight, 0);
      RootNode = Build(flameNode, CallTree.RootNodes, 0);
    }
    else {
      RootWeight = rootNode.Weight;
      var flameNode = CreateFlameGraphNode(rootNode, rootNode.Weight, 0);
      RootNode = Build(flameNode, rootNode.Children, 0);
    }
  }

  public double ScaleWeight(FlameGraphNode node) {
    return node.Weight.Ticks * rootWeightReciprocal_;
  }

  public double ScaleExclusiveWeight(FlameGraphNode node) {
    return node.ExclusiveWeight.Ticks * rootWeightReciprocal_;
  }

  public double ScaleStartTime(TimeSpan time) {
    return (time.Ticks - profileStartTime_.Ticks) * profileDurationReciprocal_;
  }

  public double ScaleStartTime(FlameGraphNode node) {
    if (node.CallTreeNode != null) {
      return (node.StartTime.Ticks - profileStartTime_.Ticks) * profileDurationReciprocal_;
    }

    return (node.StartTime.Ticks - RootNode.StartTime.Ticks) * rootDurationReciprocal_;
  }

  public double ScaleDuration(FlameGraphNode node) {
    if (node.CallTreeNode != null) {
      return (node.EndTime.Ticks - node.StartTime.Ticks) * profileDurationReciprocal_;
    }

    return (node.EndTime.Ticks - node.StartTime.Ticks) * rootDurationReciprocal_;
  }

  public double InverseScaleWeight(TimeSpan weight) {
    return (double)RootWeight.Ticks / weight.Ticks;
  }

  private void GetNodesInTimeRangeImpl(FlameGraphNode node, TimeSpan startTime, TimeSpan endTime,
                                       List<FlameGraphNode> nodes) {
    if (node.StartTime >= endTime || node.EndTime <= startTime) {
      return;
    }

    if (node.HasFunction) {
      nodes.Add(node);
    }

    if (node.HasChildren) {
      foreach (var child in node.Children) {
        GetNodesInTimeRangeImpl(child, startTime, endTime, nodes);
      }
    }
  }

  private void AddSample(FlameGraphNode rootNode, ProfileSample sample, ResolvedProfileStack stack) {
    var node = rootNode;
    int depth = 0;

    for (int k = stack.FrameCount - 1; k >= 0; k--) {
      var resolvedFrame = stack.StackFrames[k];

      if (resolvedFrame.FrameDetails.Function == null) {
        continue;
      }

      FlameGraphNode targetNode = null;

      if (node.HasChildren) {
        for (int i = node.Children.Count - 1; i >= 0; i--) {
          var child = node.Children[i];

          if (!child.CallTreeNode.Function.Equals(resolvedFrame.FrameDetails.Function)) {
            break; // Last func is different, stop and start a new stack.
          }

          if (sample.Time - child.EndTime < TimeSpan.FromMilliseconds(100)) {
            targetNode = child; // Also start a new stack if nothing executed for a while.
          }

          break;
        }
      }

      if (targetNode == null) {
        var callNode = new ProfileCallTreeNode(resolvedFrame.FrameDetails.DebugInfo,
                                               resolvedFrame.FrameDetails.Function,
                                               null, node.CallTreeNode);
        targetNode = new FlameGraphNode(callNode, TimeSpan.Zero, depth, nameFormatter_);
        node.Children ??= new List<FlameGraphNode>();
        node.Children.Add(targetNode);
        targetNode.StartTime = sample.Time;
        targetNode.EndTime = sample.Time + sample.Weight;
        targetNode.Parent = node;
      }
      else {
        targetNode.StartTime = TimeSpan.FromTicks(Math.Min(targetNode.StartTime.Ticks, sample.Time.Ticks));
        targetNode.EndTime =
          TimeSpan.FromTicks(Math.Max(targetNode.EndTime.Ticks, sample.Time.Ticks + sample.Weight.Ticks));
      }

      if (k > 0) {
        node.ChildrenWeight += sample.Weight;
      }
      else {
        node.Weight += sample.Weight;
      }

      targetNode.Weight += sample.Weight;
      node = targetNode;
      depth++;
    }
  }

  private FlameGraphNode Build(FlameGraphNode flameNode,
                               ICollection<ProfileCallTreeNode> children, int depth) {
    if (children == null || children.Count == 0) {
      return flameNode;
    }

    var sortedChildren = new List<ProfileCallTreeNode>(children.Count);
    var childrenWeight = TimeSpan.Zero;

    foreach (var child in children) {
      sortedChildren.Add(child);
      childrenWeight += child.Weight;
    }

    // Place nodes left to right based on weight, heaviest first.
    sortedChildren.Sort((a, b) => b.Weight.CompareTo(a.Weight));

    flameNode.Children = new List<FlameGraphNode>(children.Count);
    flameNode.ChildrenWeight = childrenWeight;

    foreach (var child in sortedChildren) {
      var childFlameNode = CreateFlameGraphNode(child, child.Weight, depth + 1);
      var childNode = Build(childFlameNode, child.Children, depth + 1);
      childNode.Parent = flameNode;
      flameNode.Children.Add(childNode);
      child.Tag = childFlameNode; // Quick mapping between call tree and flame graph node.
    }

    return flameNode;
  }

  private FlameGraphNode CreateFlameGraphNode(ProfileCallTreeNode callTreeNode, TimeSpan weight, int depth) {
    var flameNode = new FlameGraphNode(callTreeNode, weight, depth, nameFormatter_);

    if (callTreeNode != null) {
      flameNode.ExclusiveWeight = callTreeNode.ExclusiveWeight;
      flameNode.Percentage = ScaleWeight(flameNode);
      flameNode.ExclusivePercentage = ScaleExclusiveWeight(flameNode);
    }
    else {
      // For the root node, exclusive time is 0% and inclusive 100%.
      flameNode.Percentage = 1;
    }

    return flameNode;
  }

  public void BuildTimeline(ProfileData data, int threadId) {
    Trace.WriteLine($"Timeline Samples: {data.Samples.Count}");
    data.Samples.Sort((a, b) => a.Sample.Time.CompareTo(b.Sample.Time));

    var flameNode = CreateFlameGraphNode(null, RootWeight, 0);
    flameNode.StartTime = TimeSpan.MaxValue;
    flameNode.EndTime = TimeSpan.MinValue;

    if (data.Samples.Count > 0) {
      profileStartTime_ = data.Samples[0].Sample.Time;
      profileEndTime_ = data.Samples[^1].Sample.Time;
    }

    foreach (var (sample, stack) in data.Samples) {
      if (threadId != -1 && stack.Context.ThreadId != threadId) {
        continue;
      }

      AddSample(flameNode, sample, stack);

      flameNode.StartTime = TimeSpan.FromTicks(Math.Min(flameNode.StartTime.Ticks, sample.Time.Ticks));
      flameNode.EndTime =
        TimeSpan.FromTicks(Math.Max(flameNode.EndTime.Ticks, sample.Time.Ticks + sample.Weight.Ticks));
      flameNode.Weight = flameNode.EndTime - flameNode.StartTime + sample.Weight;
    }

    //flameNode.Duration = flameNode.EndTime - flameNode.StartTime;
    RootNode = flameNode;
    RootWeight = flameNode.Weight;
    //Dump(RootNode);
  }

  public void Dump(FlameGraphNode node, int level = 0) {
    Trace.Write(new string(' ', level * 2));
    Trace.WriteLine($"{node.CallTreeNode?.FunctionName}  | {node.Depth} | {node.Weight.TotalMilliseconds}");

    if (node.Weight.Ticks == 0) {
      Trace.WriteLine("=> no weight");
    }

    if (node.HasChildren) {
      foreach (var child in node.Children) {
        Dump(child, level + 1);
      }
    }

    if (level < 1) {
      Trace.WriteLine("==============================================");
    }

    else if (level < 2) {
      Trace.WriteLine("----------------------");
    }
  }
}