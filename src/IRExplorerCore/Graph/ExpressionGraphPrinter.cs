// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
    public class ExpressionGraphPrinterOptions {
        public bool PrintVariableNames { get; set; }
        public bool PrintSSANumbers { get; set; }
        public bool GroupInstructions { get; set; }
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
        private List<Tuple<IRElement, IRElement>> edges_;

        private List<Tuple<IRElement, IRElement, string>> nodes_;
        private ExpressionGraphPrinterOptions options_;
        private IRElement rootElement_;
        private IRElement startElement_;
        private Dictionary<IRElement, IRElement> visitedElements_;
        private Dictionary<string, TaggedObject> elementNameMap_;
        private Dictionary<TaggedObject, List<TaggedObject>> blockNodeGroupsMap_;

        public ExpressionGraphPrinter(IRElement startElement,
                                      ExpressionGraphPrinterOptions options,
                                      GraphVizPrinterNameProvider nameProvider) :
            base(nameProvider) {
            startElement_ = startElement;
            options_ = options;
            visitedElements_ = new Dictionary<IRElement, IRElement>();
            nodes_ = new List<Tuple<IRElement, IRElement, string>>();
            edges_ = new List<Tuple<IRElement, IRElement>>();
            elementNameMap_ = new Dictionary<string, TaggedObject>();
            blockNodeGroupsMap_ = new Dictionary<TaggedObject, List<TaggedObject>>();
        }

        private void CreateNode(IRElement element, IRElement parent, string label) {
            nodes_.Add(new Tuple<IRElement, IRElement, string>(element, parent, label));
        }

        private void CreateEdge(IRElement element1, IRElement element2) {
            edges_.Add(new Tuple<IRElement, IRElement>(element1, element2));
        }

        protected override void PrintGraph(StringBuilder builder) {
            builder_ = builder;
            var refFinder = new ReferenceFinder(startElement_.ParentFunction);
            var exprNode = PrintExpression(startElement_, null, 0, refFinder);
            rootElement_ = CreateFakeIRElement();
            CreateNode(rootElement_, null, "ROOT");
            CreateEdge(rootElement_, exprNode);

            if (options_.GroupInstructions) {
                PrintGroupedNodes();
            }
            else {
                PrintNodes();
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
                    case TupleIR tuple:
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

            foreach (var (_, tuples) in blockGroups) {
                int margin = Math.Min(10 * (tuples.Count + 1), 50);
                StartSubgraph(margin, builder_);

                foreach ((var irElement, _, string label) in tuples) {
                    PrintNode(irElement, label);
                }

                EndSubgraph(builder_);
            }

            foreach ((var irElement, _, string label) in noGroupNodes) {
                PrintNode(irElement, label);
            }
        }

        private void PrintNodes() {
            foreach ((var element, _, string label) in nodes_) {
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

        private void AddElementToGroupMap(IRElement element, TupleIR tuple) {
            var block = tuple.ParentBlock;

            if (!blockNodeGroupsMap_.TryGetValue(block, out var group)) {
                group = new List<TaggedObject>();
                blockNodeGroupsMap_.Add(block, group);
            }

            group.Add(element);
        }

        private void PrintNode(IRElement element, string label) {
            // Numbers picked by trial and error for graph to look good...
            double verticalMargin = 0.055;
            double horizontalMargin = Math.Min(Math.Max(0.1, label.Length * 0.03), 1.0);

            string elementName = CreateNodeWithMargins(element.Id, label, builder_,
                                                       horizontalMargin, verticalMargin);
            elementNameMap_[elementName] = element;
        }

        private void PrintEdges() {
            foreach (var (element1, element2) in edges_) {
                CreateEdge(element1.Id, element2.Id, builder_);
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

                    foreach (var sourceOp in instr.Sources) {
                        var result = PrintExpression(sourceOp, instr, level + 1, refFinder);
                        ConnectChildNode(instr, result);
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

        private void ConnectChildNode(InstructionIR instr, IRElement result) {
            if (result != null) {
                if (options_.PrintBottomUp) {
                    CreateEdge(instr, result);
                }
                else {
                    CreateEdge(result, instr);
                }
            }
        }

        private string GetOperandLabel(OperandIR op) {
            if (!op.HasName) {
                return "<Untitled>";
            }

            string label = op.Name;

            if (options_.PrintSSANumbers) {
                var ssaNumber = ReferenceFinder.GetSSADefinitionId(op);

                if (ssaNumber.HasValue) {
                    return $"{label}<{ssaNumber.Value.ToString(CultureInfo.InvariantCulture)}>";
                }
            }

            if (op.IsAddress || op.IsLabelAddress) {
                return $"&{label}";
            }
            else if (op.IsIndirection) {
                return $"[{label}]";
            }

            return label;
        }

        private string GetInstructionLabel(InstructionIR instr) {
            string label = instr.OpcodeText.ToString();

            if (instr.Destinations.Count > 0) {
                var destOp = instr.Destinations[0];
                string variableName = "";
                string ssaNumber = "";

                if (destOp.HasName && options_.PrintVariableNames) {
                    variableName = destOp.Name;
                }

                var ssaTag = destOp.GetTag<SSADefinitionTag>();

                if (ssaTag != null && options_.PrintSSANumbers) {
                    ssaNumber = ssaTag.DefinitionId.ToString(CultureInfo.InvariantCulture);
                }

                if (!string.IsNullOrEmpty(variableName)) {
                    return !string.IsNullOrEmpty(ssaNumber) ?
                        $"{variableName}<{ssaNumber}> = {label}" :
                        $"{variableName} = {label}";
                }
                else if (!string.IsNullOrEmpty(ssaNumber)) {
                    return $"<{ssaNumber}> = {label}";
                }
            }

            return label;
        }

        public override Dictionary<string, TaggedObject> CreateNodeDataMap() {
            return elementNameMap_;
        }

        public override Dictionary<TaggedObject, List<TaggedObject>> CreateNodeDataGroupsMap() {
            return blockNodeGroupsMap_;
        }
    }
}