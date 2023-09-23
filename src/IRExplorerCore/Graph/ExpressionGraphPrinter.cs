// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
    public class ExpressionGraphPrinterOptions {
        public bool PrintVariableNames { get; set; }
        public bool PrintSSANumbers { get; set; }
        public bool GroupInstructionsByBlock { get; set; }
        public bool GroupBlocksByRegion { get; set; }
        public bool PrintBottomUp { get; set; }
        public bool SkipCopyInstructions { get; set; }
        public int MaxExpressionDepth { get; set; }
        public ICompilerIRInfo IR { get; set; }

        //? TODO:
        // - Handlers for overriding operand/instr labels
        //   (virtual func return bool on handled)
        // - Handle regions, option to group by region
        //   (blocks as subregions, link instr to subregion)
        // -
    }

    public sealed class ExpressionGraphPrinter : GraphVizPrinter {
        private StringBuilder builder_;
        private List<Tuple<IRElement, IRElement, int>> edges_;
        private List<Tuple<IRElement, IRElement, string>> nodes_;
        private ExpressionGraphPrinterOptions options_;
        private IRElement rootElement_;
        private IRElement startElement_;
        private Dictionary<IRElement, IRElement> visitedElements_;
        private Dictionary<string, TaggedObject> elementNameMap_;
        private Dictionary<TaggedObject, GraphNodeGroup> blockNodeGroupsMap_;

        public ExpressionGraphPrinter(IRElement startElement,
            ExpressionGraphPrinterOptions options,
            GraphPrinterNameProvider nameProvider) :
            base(nameProvider) {
            startElement_ = startElement;
            options_ = options;
            visitedElements_ = new Dictionary<IRElement, IRElement>();
            nodes_ = new List<Tuple<IRElement, IRElement, string>>();
            edges_ = new List<Tuple<IRElement, IRElement, int>>();
            elementNameMap_ = new Dictionary<string, TaggedObject>();
            blockNodeGroupsMap_ = new Dictionary<TaggedObject, GraphNodeGroup>();
        }

        private void CreateNode(IRElement element, IRElement parent, string label) {
            nodes_.Add(new Tuple<IRElement, IRElement, string>(element, parent, label));
        }

        private void CreateEdge(IRElement element1, IRElement element2, int index = -1) {
            edges_.Add(new Tuple<IRElement, IRElement, int>(element1, element2, index));
        }

        protected override void PrintGraph(StringBuilder builder) {
            builder_ = builder;
            var refFinder = new ReferenceFinder(startElement_.ParentFunction);
            var exprNode = PrintExpression(startElement_, null, 0, refFinder);
            rootElement_ = CreateFakeIRElement();
            CreateNode(rootElement_, null, "ROOT");
            CreateEdge(rootElement_, exprNode);

            if (options_.GroupInstructionsByBlock ||
                options_.GroupBlocksByRegion) {
                PrintGroupedNodes();
            }
            else {
                PrintNodes(nodes_);
            }

            PrintEdges();
        }

        private TupleIR CreateFakeIRElement() {
            return new TupleIR(IRElementId.FromLong(1), TupleKind.Other,
                startElement_.ParentBlock);
        }

        private void PrintGroupedNodes() {
            var blockGroups = new Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>>();
            var noGroupNodes = new List<Tuple<IRElement, IRElement, string>>();

            foreach (var node in nodes_) {
                switch (node.Item1) {
                    case TupleIR tuple when tuple != rootElement_:
                        AddNodeToGroup(node, tuple, blockGroups);
                        AddElementToGroupMap(node.Item1, tuple);
                        break;
                    case OperandIR _ when node.Item2 is TupleIR parentTuple:
                        AddNodeToGroup(node, parentTuple, blockGroups);
                        AddElementToGroupMap(node.Item1, parentTuple);
                        break;
                    default:
                        noGroupNodes.Add(node);
                        break;
                }
            }

            if (options_.GroupBlocksByRegion) {
                // Group blocks  by regions.
                var regionGroups = BuildRegionGroups(blockGroups);

                // Print nodes each region and its blocks.
                foreach (var (region, blockGroup) in regionGroups) {
                    PrintRegionGroup(region, blockGroup);
                }
            }
            else {
                // Print nodes in reach block.
                foreach (var (block, tuples) in blockGroups) {
                    PrintBlockGroup(tuples);
                }
            }

            // Print anything left that's not in some region.
            foreach ((var irElement, _, string label) in noGroupNodes) {
                PrintNode(irElement, label);
            }
        }

        private static Dictionary<RegionIR, Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>>> BuildRegionGroups(Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>> blockGroups) {
            var regionGroups = new Dictionary<RegionIR, Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>>>();

            foreach (var (block, tuples) in blockGroups) {
                var region = block.ParentRegion;

                if (region != null) {
                    if (!regionGroups.TryGetValue(region, out var blockGroup)) {
                        blockGroup = new Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>>();
                        regionGroups.Add(block.ParentRegion, blockGroup);
                    }

                    blockGroup.Add(block, tuples);
                }
            }

            return regionGroups;
        }

        private void PrintRegionGroup(RegionIR region, Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>> blockGroup) {
            int regionMargin = 10;
            StartSubgraph(regionMargin, builder_);

            var nodeGroup = new GraphNodeGroup(region.ParentRegion != null); // Don't draw the root region.
            nodeGroup.Label = nameProvider_.GetRegionNodeLabel(region);
            blockNodeGroupsMap_[region] = nodeGroup;

            foreach (var (block, tuples) in blockGroup) {
                nodeGroup.Nodes.Add(block);
                PrintBlockGroup(tuples);
            }

            EndSubgraph(builder_);
        }

        private void PrintBlockGroup(List<Tuple<IRElement, IRElement, string>> tuples) {
            int margin = Math.Min(10 * (tuples.Count + 1), 50);
            StartSubgraph(margin, builder_);
            PrintNodes(tuples);
            EndSubgraph(builder_);
        }

        private void PrintNodes(List<Tuple<IRElement, IRElement, string>> nodes) {
            foreach ((var element, _, string label) in nodes) {
                PrintNode(element, label);
            }
        }

        private void AddNodeToGroup(Tuple<IRElement, IRElement, string> node, TupleIR tuple,
            Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>> blockGroups) {
            var block = tuple.ParentBlock;

            if (!blockGroups.TryGetValue(block, out var group)) {
                group = new List<Tuple<IRElement, IRElement, string>>();
                blockGroups.Add(block, group);
            }

            group.Add(node);
        }

        private void AddElementToGroupMap(IRElement element, TupleIR parentTuple) {
            var block = parentTuple.ParentBlock;

            if (!blockNodeGroupsMap_.TryGetValue(block, out var group)) {
                group = new GraphNodeGroup(options_.GroupInstructionsByBlock);
                group.Label = nameProvider_.GetBlockNodeLabel(block);
                blockNodeGroupsMap_.Add(block, group);
            }

            group.Nodes.Add(element);
        }

        private void PrintNode(IRElement element, string label) {
            // Numbers picked by trial and error for graph to look good...
            bool isMultiline = label.Contains("\\n");
            double verticalMargin = isMultiline ? 0.12 : 0.06;
            double horizontalMargin = Math.Min(Math.Max(0.1, label.Length * (isMultiline ? 0.02 : 0.03)), 2.0);

            string elementName = CreateNodeWithMargins(element.Id, label, builder_,
                horizontalMargin, verticalMargin);
            elementNameMap_[elementName] = element;
        }

        private void PrintEdges() {
            foreach (var (element1, element2, index) in edges_) {
                if (index != -1) {
                    CreateEdgeWithLabel(element1.Id, element2.Id, index.ToString(), builder_);
                }
                else {
                    CreateEdge(element1.Id, element2.Id, builder_);
                }
            }
        }

        private IRElement SkipAllCopies(IRElement op) {
            var defInstr = op.ParentInstruction;

            while (defInstr != null) {
                var sourceOp = options_.IR.SkipCopyInstruction(defInstr);

                if (sourceOp != null) {
                    op = sourceOp;
                    defInstr = sourceOp.ParentInstruction;
                }
                else {
                    break;
                }
            }

            return op;
        }

        private IRElement PrintExpression(IRElement element, IRElement parent,
            int level, ReferenceFinder refFinder) {
            if (visitedElements_.TryGetValue(element, out var mappedElement)) {
                return mappedElement;
            }

            if (options_.SkipCopyInstructions) {
                element = SkipAllCopies(element);
            }

            switch (element) {
                case OperandIR op: {
                    var defElement = refFinder.FindSingleDefinition(op);

                    if (defElement != null) {
                        if (defElement is OperandIR defOp) {
                            if (defOp.Role == OperandRole.Parameter) {
                                string label = GetOperandLabel(defOp);
                                CreateNode(defOp, parent, label);
                                visitedElements_[element] = defOp;
                                return defOp;
                            }
                        }

                        var result = PrintExpression(defElement.ParentTuple, op, level, refFinder);
                        visitedElements_[element] = result;
                        return result;
                    }
                    else if (op.IsIntConstant) {
                        CreateNode(op, parent, op.IntValue.ToString(CultureInfo.InvariantCulture));
                    }
                    else if (op.IsFloatConstant) {
                        CreateNode(op, parent, op.FloatValue.ToString(CultureInfo.InvariantCulture));
                    }
                    else {
                        string label = GetOperandLabel(op);
                        CreateNode(op, parent, label);
                    }

                    visitedElements_[element] = op;
                    return op;
                }
                case InstructionIR instr: {
                    string label = GetInstructionLabel(instr);
                    CreateNode(instr, parent, label);
                    visitedElements_[element] = instr;

                    if (level >= options_.MaxExpressionDepth) {
                        return instr;
                    }

                    int sourceIndex = 0;

                    foreach (var sourceOp in instr.Sources) {
                        var result = PrintExpression(sourceOp, instr, level + 1, refFinder);
                        ConnectChildNode(instr, result, sourceIndex);
                        sourceIndex++;
                    }

                    foreach (var destOp in instr.Destinations) {
                        if (destOp.IsIndirection) {
                            var result = PrintExpression(destOp, instr, level + 1, refFinder);
                            ConnectChildNode(instr, result);
                        }
                    }

                    return instr;
                }
                default:
                    // Use the element text.
                    CreateNode(element, parent, "<Undefined>");
                    break;
            }

            visitedElements_[element] = element;
            return element;
        }

        private void ConnectChildNode(InstructionIR instr, IRElement result, int index = -1) {
            if (result != null) {
                if (options_.PrintBottomUp) {
                    CreateEdge(instr, result, index);
                }
                else {
                    CreateEdge(result, instr, index);
                }
            }
        }

        private string GetOperandLabel(OperandIR op) {
            return nameProvider_.GetOperandNodeLabel(op, options_.PrintSSANumbers);
        }

        private string GetInstructionLabel(InstructionIR instr) {
            return nameProvider_.GetInstructionNodeLabel(instr, options_.PrintVariableNames, options_.PrintSSANumbers);
        }

        public override Dictionary<string, TaggedObject> CreateNodeDataMap() {
            return elementNameMap_;
        }

        public override Dictionary<TaggedObject, GraphNodeGroup> CreateNodeDataGroupsMap() {
            return blockNodeGroupsMap_;
        }
    }
}