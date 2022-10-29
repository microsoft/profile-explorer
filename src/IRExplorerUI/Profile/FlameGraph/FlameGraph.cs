using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Documents;
using Brush = System.Windows.Media.Brush;
using Size = System.Windows.Size;
using IRExplorerUI.Profile;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace IRExplorerUI.Profile {
    public class FlameGraphNode {
        internal const double DefaultMargin = 4;
        internal const double ExtraValueMargin = 20;
        internal const double MinVisibleRectWidth = 4;
        internal const double RecomputeVisibleRectWidth = MinVisibleRectWidth * 4;
        internal const double MinVisibleWidth = 1;
        internal const int MaxTextParts = 3;

        public FlameGraphNode(ProfileCallTreeNode callTreeNode, TimeSpan weight, int depth) {
            CallTreeNode = callTreeNode;
            Weight = weight;
            Depth = depth;
            ShowWeight = true;
            ShowWeightPercentage = true;
        }

        public virtual bool IsGroup => false;
        public ProfileCallTreeNode CallTreeNode { get; }
        public FlameGraphRenderer Owner { get; set; }
        public FlameGraphNode Parent { get; set; }
        public List<FlameGraphNode> Children { get; set; }
        public TextSearchResult? SearchResult { get; set; }

        public TimeSpan Weight { get; set; }
        public TimeSpan ChildrenWeight { get; set; }
        public int Depth { get; set; }
        public int MaxDepthUnder { get; set; }

        public HighlightingStyle Style { get; set; }
        public Brush TextColor { get; set; }
        public Brush ModuleTextColor { get; set; }
        public Brush WeightTextColor { get; set; }
        public Rect Bounds { get; set; }
        public bool ShowWeight { get; set; }
        public bool ShowWeightPercentage { get; set; }
        public bool ShowInclusiveWeight { get; set; }
        public bool IsDummyNode { get; set; }

        public bool HasFunction => CallTreeNode != null;
        public bool HasChildren => Children is { Count: > 0 };

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
    }

    public sealed class FlameGraphGroupNode : FlameGraphNode {
        public override bool IsGroup => true;
        public int ReplacedNodeCount  { get; }
        public int ReplacedStartIndex { get; }
        public int ReplacedEndIndex => ReplacedStartIndex + ReplacedNodeCount;

        public FlameGraphGroupNode(FlameGraphNode parentNode, int startIndex,
                                   int replacedNodeCount, TimeSpan weight, int depth) :
            base(null, weight, depth) {
            Parent = parentNode;
            ReplacedStartIndex = startIndex;
            ReplacedNodeCount = replacedNodeCount;
        }
    }

    public sealed class FlameGraph {
        private Dictionary<ProfileCallTreeNode, FlameGraphNode> treeNodeToFgNodeMap_;
        private FlameGraphNode rootNode_;
        private TimeSpan rootWeight_;
        private TimeSpan profileStartTime_;
        private TimeSpan profileEndTime_;
        private double rootWeightReciprocal_;
        private double rootDurationReciprocal_;
        private double profileDurationReciprocal_;

        public ProfileCallTree CallTree { get; set; }

        public FlameGraphNode RootNode {
            get => rootNode_;
            set {
                rootNode_ = value;

                if (rootNode_.Duration.Ticks != 0) {
                    rootDurationReciprocal_ = 1.0 / (double)rootNode_.Duration.Ticks;
                    profileDurationReciprocal_ = 1.0 / (double)(profileEndTime_ - profileStartTime_).Ticks;
                }
            }
        }

        public TimeSpan RootWeight {
            get => rootWeight_;
            set {
                rootWeight_ = value;

                if (rootWeight_.Ticks != 0) {
                    rootWeightReciprocal_ = 1.0 / (double)rootWeight_.Ticks;
                }
            }
        }

        public FlameGraph(ProfileCallTree callTree) {
            CallTree = callTree;
            treeNodeToFgNodeMap_ = new Dictionary<ProfileCallTreeNode, FlameGraphNode>();
        }

        public List<FlameGraphNode> GetNodes(ProfileCallTreeNode node) {
            if (!node.IsGroup) {
                return new List<FlameGraphNode>() { treeNodeToFgNodeMap_.GetValueOrDefault(node) };
            }

            var groupNode = node as ProfileCallTreeGroupNode;
            var list = new List<FlameGraphNode>(groupNode.Nodes.Count);

            foreach (var childNode in groupNode.Nodes) {
                list.Add(treeNodeToFgNodeMap_.GetValueOrDefault(childNode));
            }

            return list;
        }

        public List<FlameGraphNode> SearchNodes(string text, bool includeModuleName = true) {
            var nodes = new List<FlameGraphNode>();
            SearchNodesImpl(RootNode, text, nodes, includeModuleName);
            return nodes;
        }

        public void SearchNodesImpl(FlameGraphNode node, string text,
                                    List<FlameGraphNode> nodes, bool includeModuleName) {
            if (node.HasFunction) {
                var result = TextSearcher.FirstIndexOf(node.CallTreeNode.FunctionName, text, 0, TextSearchKind.CaseInsensitive);
                if (result.HasValue) {
                    node.SearchResult = result;
                    nodes.Add(node);
                }
                else {
                    result = TextSearcher.FirstIndexOf(node.CallTreeNode.ModuleName, text, 0, TextSearchKind.CaseInsensitive);
                    if (result.HasValue) {
                        nodes.Add(node);
                    }
                }
            }

            if (node.HasChildren) {
                foreach (var child in node.Children) {
                    SearchNodesImpl(child, text, nodes, includeModuleName);
                }
            }
        }

        public void ResetSearchResults(List<FlameGraphNode> nodes) {
            foreach(var node in nodes) {
                node.SearchResult = null;
            }
        }

        public List<FlameGraphNode> GetNodesInTimeRange(TimeSpan startTime, TimeSpan endTime) {
            var nodes = new List<FlameGraphNode>();
            GetNodesInTimeRangeImpl(RootNode, startTime, endTime, nodes);
            return nodes;
        }

        private void GetNodesInTimeRangeImpl(FlameGraphNode node, TimeSpan startTime, TimeSpan endTime, List<FlameGraphNode> nodes) {
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

        public void BuildTimeline(ProfileData data, int threadId) {
            Trace.WriteLine($"Timeline Samples: {data.Samples.Count}");
            data.Samples.Sort((a, b) => a.Sample.Time.CompareTo(b.Sample.Time));

            var flameNode = new FlameGraphNode(null, RootWeight, 0);
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
                flameNode.EndTime = TimeSpan.FromTicks(Math.Max(flameNode.EndTime.Ticks, sample.Time.Ticks + sample.Weight.Ticks));
                flameNode.Weight = flameNode.EndTime - flameNode.StartTime + sample.Weight;
            }

            //flameNode.Duration = flameNode.EndTime - flameNode.StartTime;
            RootNode = flameNode;
            RootWeight = flameNode.Weight;
            //Dump(RootNode);
        }

        public void Dump(FlameGraphNode node, int level = 0) {
            Trace.Write(new  string(' ', level * 2 ));
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

        private void AddSample(FlameGraphNode rootNode, ProfileSample sample, ResolvedProfileStack stack) {
            var node = rootNode;
            int depth = 0;

            for (int k = stack.FrameCount - 1; k >= 0; k--) {
                var resolvedFrame = stack.StackFrames[k];

                // if (resolvedFrame.IsUnknown) {
                //     continue;
                // }

                if (resolvedFrame.Info.Function == null) {
                    continue;
                }

                FlameGraphNode targetNode = null;

                if (node.HasChildren) {
                    for (int i = node.Children.Count - 1; i >= 0; i--) {
                        var child = node.Children[i];

                        if (!child.CallTreeNode.Function.Equals(resolvedFrame.Info.Function)) {
                            break; // Last func is different, stop and start a new stack.
                        }

                        if (sample.Time - child.EndTime < TimeSpan.FromMilliseconds(100)) {
                            targetNode = child; // Also start a new stack if nothing executed for a while.
                        }

                        break;
                    }
                }

                if (targetNode == null) {
                    var callNode = new ProfileCallTreeNode(resolvedFrame.Info.DebugInfo, resolvedFrame.Info.Function) {
                        //? TODO: Kind = resolvedFrame.IsKernelCode
                    };
                    targetNode = new FlameGraphNode(callNode, TimeSpan.Zero, depth);
                    node.Children ??= new List<FlameGraphNode>();
                    node.Children.Add(targetNode);
                    targetNode.StartTime = sample.Time;
                    targetNode.EndTime = sample.Time + sample.Weight;
                    targetNode.Parent = node;

                    if (node.HasFunction) {
                        targetNode.CallTreeNode.AddParentNoLock(node.CallTreeNode);
                    }
                }
                else {
                    targetNode.StartTime = TimeSpan.FromTicks(Math.Min(targetNode.StartTime.Ticks, sample.Time.Ticks));
                    targetNode.EndTime = TimeSpan.FromTicks(Math.Max(targetNode.EndTime.Ticks, sample.Time.Ticks + sample.Weight.Ticks));
                }

                if (k > 0) {
                    node.ChildrenWeight += sample.Weight;
                }
                else {
                    node.Weight += sample.Weight;
                }

                targetNode.Weight += sample.Weight;
                //targetNode.Weight = targetNode.EndTime - targetNode.StartTime + sample.Weight;

                node = targetNode;
                depth++;
            }
        }

        public void Build(ProfileCallTreeNode rootNode) {
            if (rootNode == null) {
                // Make on dummy root node that hosts all real root nodes.
                RootWeight = CallTree.TotalRootNodesWeight;
                var flameNode = new FlameGraphNode(null, RootWeight, 0);
                RootNode = Build(flameNode, CallTree.RootNodes, 0);
            }
            else {
                RootWeight = rootNode.Weight;
                var flameNode = new FlameGraphNode(rootNode, rootNode.Weight, 0);
                RootNode = Build(flameNode, rootNode.Children, 0);
            }
        }

        private FlameGraphNode Build(FlameGraphNode flameNode,
            ICollection<ProfileCallTreeNode> children, int depth) {
            if (children == null || children.Count == 0) {
                return flameNode;
            }

            var sortedChildren = new List<ProfileCallTreeNode>(children.Count);
            TimeSpan childrenWeight = TimeSpan.Zero;

            foreach (var child in children) {
                sortedChildren.Add(child);
                childrenWeight += child.Weight;
            }

            sortedChildren.Sort((a, b) => b.Weight.CompareTo(a.Weight));

            flameNode.Children = new List<FlameGraphNode>(children.Count);
            flameNode.ChildrenWeight = childrenWeight;

            foreach (var child in sortedChildren) {
                var childFlameNode = new FlameGraphNode(child, child.Weight, depth + 1);
                var childNode = Build(childFlameNode, child.Children, depth + 1);
                childNode.Parent = flameNode;
                flameNode.Children.Add(childNode);
                treeNodeToFgNodeMap_[child] = childFlameNode;
            }

            return flameNode;
        }

        public double ScaleWeight(FlameGraphNode node) {
            return (double)node.Weight.Ticks * rootWeightReciprocal_;
        }

        public double ScaleStartTime(TimeSpan time) {
            return (double)(time.Ticks - profileStartTime_.Ticks) * profileDurationReciprocal_;
        }

        public double ScaleStartTime(FlameGraphNode node) {
            if (node.CallTreeNode != null) {
                return (double)(node.StartTime.Ticks - profileStartTime_.Ticks) * profileDurationReciprocal_;
            }
            else {
                return (double)(node.StartTime.Ticks - RootNode.StartTime.Ticks) * rootDurationReciprocal_;
            }
        }

        public double ScaleDuration(FlameGraphNode node) {
            if (node.CallTreeNode != null) {
                return (double)(node.EndTime.Ticks - node.StartTime.Ticks) * profileDurationReciprocal_;
            }
            else {
                return (double)(node.EndTime.Ticks - node.StartTime.Ticks) * rootDurationReciprocal_;
            }
        }

        public double InverseScaleWeight(TimeSpan weight) {
            return (double)RootWeight.Ticks / weight.Ticks;
        }
    }
}