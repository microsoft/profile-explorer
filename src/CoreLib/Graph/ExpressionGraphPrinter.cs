using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.GraphViz {
    public class ExpressionGraphPrinterOptions {
        public bool PrintVariableNames { get; set; }
        public bool PrintSSANumbers { get; set; }
        public bool GroupInstructions { get; set; }
        public bool PrintBottomUp { get; set; }
        public bool SkipCopyInstructions { get; set; }
        public int MaxExpressionDepth { get; set; }
        public ICompilerIRInfo IR { get; set; }
    }

    public sealed class ExpressionGraphPrinter : GraphVizPrinter {
        private StringBuilder builder_;
        private List<Tuple<IRElement, IRElement>> edges_;

        private List<Tuple<IRElement, IRElement, string>> nodes_;
        private ExpressionGraphPrinterOptions options_;
        private IRElement rootElement_;
        private IRElement startElement_;
        private Dictionary<IRElement, IRElement> visitedElements_;

        public ExpressionGraphPrinter(IRElement startElement,
                                      ExpressionGraphPrinterOptions options) {
            startElement_ = startElement;
            options_ = options;
            visitedElements_ = new Dictionary<IRElement, IRElement>();
            nodes_ = new List<Tuple<IRElement, IRElement, string>>();
            edges_ = new List<Tuple<IRElement, IRElement>>();
            ElementNameMap = new Dictionary<string, IRElement>();
            BlockNodeGroupsMap = new Dictionary<IRElement, List<IRElement>>();
        }

        public Dictionary<string, IRElement> ElementNameMap { get; set; }
        public Dictionary<IRElement, List<IRElement>> BlockNodeGroupsMap { get; set; }

        private void CreateNode(IRElement element, IRElement parent, string label) {
            //Debug.WriteLine($"+ Node {element}"); 
            nodes_.Add(new Tuple<IRElement, IRElement, string>(element, parent, label));
        }

        private void CreateEdge(IRElement element1, IRElement element2) {
            //Debug.WriteLine($"Edge {element1} => {element2}");
            edges_.Add(new Tuple<IRElement, IRElement>(element1, element2));
        }

        protected override void PrintGraph(StringBuilder builder) {
            builder_ = builder;
            var exprNode = PrintSSAExpression(startElement_, null, 0);
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
            var blockGroups =
                new Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>>();

            var noGroupNodes = new List<Tuple<IRElement, IRElement, string>>();

            foreach (var node in nodes_) {
                if (node.Item1 is TupleIR tuple) {
                    AddNodeToGroup(node, tuple, blockGroups);
                    AddElementToGroupMap(node.Item1, tuple);
                    continue;
                }
                else if (node.Item1 is OperandIR) {
                    if (node.Item2 is TupleIR parentTuple) {
                        AddNodeToGroup(node, parentTuple, blockGroups);
                        AddElementToGroupMap(node.Item1, parentTuple);
                        continue;
                    }
                }

                noGroupNodes.Add(node);
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
                                    Dictionary<BlockIR,
                                            List<Tuple<IRElement, IRElement, string>>>
                                        blockGroups) {
            var block = tuple.ParentBlock;

            if (!blockGroups.TryGetValue(block, out var group)) {
                group = new List<Tuple<IRElement, IRElement, string>>();
                blockGroups.Add(block, group);
            }

            group.Add(node);
        }

        private void AddElementToGroupMap(IRElement element, TupleIR tuple) {
            var block = tuple.ParentBlock;

            if (!BlockNodeGroupsMap.TryGetValue(block, out var group)) {
                group = new List<IRElement>();
                BlockNodeGroupsMap.Add(block, group);
            }

            group.Add(element);
        }

        private void PrintNode(IRElement element, string label) {
            double verticalMargin = 0.055;
            double horizontalMargin = Math.Min(Math.Max(0.1, label.Length * 0.03), 1.0);

            string elementName =
                CreateNodeWithMargins(element.Id, label, builder_, horizontalMargin,
                                      verticalMargin);

            ElementNameMap[elementName] = element;
        }

        private void PrintEdges() {
            foreach (var (element1, element2) in edges_) {
                base.CreateEdge(element1.Id, element2.Id, builder_);
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

        private IRElement PrintSSAExpression(IRElement element, IRElement parent, int level) {
            if (visitedElements_.TryGetValue(element, out var mappedElement)) {
                return mappedElement;
            }

            if (options_.SkipCopyInstructions) {
                element = SkipAllCopies(element);
            }

            if (element is OperandIR op) {
                var defElement = ReferenceFinder.GetSSADefinition(op);

                if (defElement != null) {
                    if (defElement is OperandIR defOp) {
                        if (defOp.Role == OperandRole.Parameter) {
                            string label = GetOperandLabel(defOp);
                            CreateNode(defOp, parent, label);
                            visitedElements_[element] = defOp;
                            return defOp;
                        }
                    }

                    var result = PrintSSAExpression(defElement.ParentTuple, op, level);
                    visitedElements_[element] = result;
                    return result;
                }
                else if (op.IsIntConstant) {
                    CreateNode(op, parent, op.IntValue.ToString(CultureInfo.InvariantCulture));
                }
                else if (op.IsFloatConstant) {
                    CreateNode(op, parent,
                               op.FloatValue.ToString(CultureInfo.InvariantCulture));
                }
                else {
                    string label = GetOperandLabel(op);
                    CreateNode(op, parent, label);
                }

                visitedElements_[element] = op;
                return op;
            }
            else if (element is InstructionIR instr) {
                string label = GetInstructionLabel(instr);
                CreateNode(instr, parent, label);
                visitedElements_[element] = instr;

                if (level >= options_.MaxExpressionDepth) {
                    return instr;
                }

                foreach (var sourceOp in instr.Sources) {
                    var result = PrintSSAExpression(sourceOp, instr, level + 1);
                    ConnectChildNode(instr, result);
                }

                foreach (var destOp in instr.Destinations) {
                    if (destOp.IsIndirection) {
                        var result = PrintSSAExpression(destOp, instr, level + 1);
                        ConnectChildNode(instr, result);
                    }
                }

                return instr;
            }
            else {
                // Use the element text.
                CreateNode(element, parent, "<Undefined>");
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

            string label = op.NameValue.ToString();

            if (options_.PrintSSANumbers) {
                var ssaNumber = ReferenceFinder.GetSSADefinitionId(op);

                if (ssaNumber.HasValue) {
                    return $"{label}<{ssaNumber.Value.ToString()}>";
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
                    variableName = destOp.NameValue.ToString();
                }

                var ssaTag = destOp.GetTag<SSADefinitionTag>();

                if (ssaTag != null && options_.PrintSSANumbers) {
                    ssaNumber = ssaTag.DefinitionId.ToString();
                }

                if (!string.IsNullOrEmpty(variableName)) {
                    if (!string.IsNullOrEmpty(ssaNumber)) {
                        return $"{variableName}<{ssaNumber}> = {label}";
                    }
                    else {
                        return $"{variableName} = {label}";
                    }
                }
                else if (!string.IsNullOrEmpty(ssaNumber)) {
                    return $"<{ssaNumber}> = {label}";
                }
            }

            return label;
        }

        public override Dictionary<string, IRElement> CreateBlockNodeMap() {
            return ElementNameMap;
        }

        public override Dictionary<IRElement, List<IRElement>> CreateBlockNodeGroupsMap() {
            return BlockNodeGroupsMap;
        }
    }
}
