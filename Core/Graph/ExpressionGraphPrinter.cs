using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Text;
using Core.IR;
using Core.Analysis;
using System.Diagnostics;

namespace Core.GraphViz
{
    public class ExpressionGraphPrinterOptions
    {
        public bool PrintVariableNames { get; set; }
        public bool PrintSSANumbers { get; set; }
        public bool GroupInstructions { get; set; }
        public bool PrintBottomUp { get; set; }
        public int MaxExpressionDepth { get; set; }

        public ExpressionGraphPrinterOptions()
        {
            PrintVariableNames = true;
            PrintSSANumbers = true;
            GroupInstructions = true;
            PrintBottomUp = false;
            MaxExpressionDepth = 8;
        }
    }

    public sealed class ExpressionGraphPrinter : GraphVizPrinter
    {
        public Dictionary<string, IRElement> ElementNameMap { get; set; }
        public Dictionary<IRElement, List<IRElement>> BlockNodeGroupsMap { get; set; }

        List<Tuple<IRElement, IRElement, string>> nodes_;
        List<Tuple<IRElement, IRElement>> edges_;
        Dictionary<IRElement, IRElement> visitedElements_;
        StringBuilder builder_;
        IRElement startElement_;
        IRElement rootElement_;
        ExpressionGraphPrinterOptions options_;

        public ExpressionGraphPrinter(IRElement startElement, ExpressionGraphPrinterOptions options)
        {
            startElement_ = startElement;
            options_ = options;
            visitedElements_ = new Dictionary<IRElement, IRElement>();
            nodes_ = new List<Tuple<IRElement, IRElement, string>>();
            edges_ = new List<Tuple<IRElement, IRElement>>();

            ElementNameMap = new Dictionary<string, IRElement>();
            BlockNodeGroupsMap = new Dictionary<IRElement, List<IRElement>>();
        }

        void CreateNode(IRElement element, IRElement parent, string label)
        {
            //Debug.WriteLine($"+ Node {element}"); 
            nodes_.Add(new Tuple<IRElement, IRElement, string>(element, parent, label));
        }

        void CreateEdge(IRElement element1, IRElement element2)
        {
            //Debug.WriteLine($"Edge {element1} => {element2}");
            edges_.Add(new Tuple<IRElement, IRElement>(element1, element2));    
        }

        protected override async void PrintGraph(StringBuilder builder)
        {
            builder_ = builder;
            var exprNode = PrintSSAExpression(startElement_, null, 0);

            rootElement_ = CreateFakeIRElement();
            CreateNode(rootElement_, null, "ROOT");
            CreateEdge(rootElement_, exprNode);

            if(options_.GroupInstructions)
            {
                PrintGroupedNodes();
            }
            else
            {
                PrintNodes();
            }

            PrintEdges();
        }

        private TupleIR CreateFakeIRElement()
        {
            return new TupleIR(IRElementId.FromLong(1), TupleKind.Other, startElement_.ParentBlock);
        }

        void PrintGroupedNodes()
        {
            var blockGroups = new Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>>();
            var noGroupNodes = new List<Tuple<IRElement, IRElement, string>>();

            foreach (var node in nodes_)
            {
                if (node.Item1 is TupleIR tuple)
                {
                    AddNodeToGroup(node, tuple, blockGroups);
                    AddElementToGroupMap(node.Item1, tuple);
                    continue;
                }
                else if (node.Item1 is OperandIR op)
                {
                    if (node.Item2 is TupleIR parentTuple)
                    {
                        AddNodeToGroup(node, parentTuple, blockGroups);
                        AddElementToGroupMap(node.Item1, parentTuple);
                        continue;
                    }
                }

                noGroupNodes.Add(node);
            }

            foreach (var pair in blockGroups)
            {
                var margin = Math.Min(10 * (pair.Value.Count + 1), 50);
                StartSubgraph(margin, builder_);

                foreach(var node in pair.Value)
                {
                    PrintNode(node.Item1, node.Item3);
                }

                EndSubgraph(builder_);
            }

            foreach(var node in noGroupNodes)
            {
                PrintNode(node.Item1, node.Item3);
            }
        }

        void PrintNodes()
        {
            foreach (var node in nodes_)
            {
                PrintNode(node.Item1, node.Item3);
            }
        }

        private void AddNodeToGroup(Tuple<IRElement, IRElement, string> node, TupleIR tuple,
                                    Dictionary<BlockIR, List<Tuple<IRElement, IRElement, string>>> blockGroups)
        {
            var block = tuple.ParentBlock;
            List<Tuple<IRElement, IRElement, string>> group;

            if (!blockGroups.TryGetValue(block, out group))
            {
                group = new List<Tuple<IRElement, IRElement, string>>();
                blockGroups.Add(block, group);
            }

            group.Add(node);
        }

        private void AddElementToGroupMap(IRElement element, TupleIR tuple)
        {
            var block = tuple.ParentBlock;
            List<IRElement> group;

            if (!BlockNodeGroupsMap.TryGetValue(block, out group))
            {
                group = new List<IRElement>();
                BlockNodeGroupsMap.Add(block, group);
            }

            group.Add(element);
        }

        void PrintNode(IRElement element, string label)
        {
            double verticalMargin = 0.055;
            double horizontalMargin = 0.1;
            horizontalMargin = Math.Min(Math.Max(0.1, label.Length * 0.03), 1.0);

            var elementName = base.CreateNodeWithMargins(element.Id, label, builder_, horizontalMargin, verticalMargin);
            ElementNameMap[elementName] = element;
        }

        void PrintEdges()
        {
            foreach(var edge in edges_)
            {
                var element1 = edge.Item1;
                var element2 = edge.Item2;
                base.CreateEdge(element1.Id, element2.Id, builder_);
            }
        }

        IRElement PrintSSAExpression(IRElement element, IRElement parent, int level)
        {
            if (visitedElements_.TryGetValue(element, out var mappedElement))
            {
                return mappedElement;
            }

            if (element is OperandIR op)
            {
                var defElement = ReferenceFinder.GetSSADefinition(op);

                if(defElement != null)
                {
                    if(defElement is OperandIR defOp)
                    {
                        if(defOp.Role == OperandRole.Parameter)
                        {
                            var label = GetOperandLabel(defOp);
                            CreateNode(defOp, parent, label);
                            visitedElements_[element] = defOp;
                            return defOp;
                        }
                    }

                    var result = PrintSSAExpression(defElement.ParentTuple, op, level);
                    visitedElements_[element] = result;
                    return result;
                }
                else if(op.IsIntConstant)
                {
                    CreateNode(op, parent, op.IntValue.ToString());
                }
                else if(op.IsFloatConstant)
                {
                    CreateNode(op, parent, op.FloatValue.ToString());
                }
                else
                {
                    var label = GetOperandLabel(op);
                    CreateNode(op, parent, label);
                }

                visitedElements_[element] = op;
                return op;
            }
            else if (element is InstructionIR instr)
            {
                var label = GetInstructionLabel(instr);
                CreateNode(instr, parent, label);
                visitedElements_[element] = instr;

                if (level >= options_.MaxExpressionDepth)
                {
                    return instr;
                }

                foreach (var sourceOp in instr.Sources)
                {
                    var result = PrintSSAExpression(sourceOp, instr, level + 1);

                    if (result != null)
                    {
                        if(options_.PrintBottomUp)
                        {
                            CreateEdge(instr, result);
                        }
                        else
                        {
                            CreateEdge(result, instr);
                        }
                    }
                }

                //? Only for INDIR
                //if (level > 0)
                //{
                //    foreach (var destOp in instr.Destinations)
                //    {
                //        PrintSSAExpression(sourceOp, instr, level + 1);
                //    }
                //}

                return instr;
            }
            else
            {
                // Use the element text.
                CreateNode(element, parent, "<Undefined>");
            }

            visitedElements_[element] = element;
            return element;
        }

        private string GetOperandLabel(OperandIR op)
        {
            if(!op.HasName)
            {
                return "<Untitled>";
            }

            var label = op.NameValue.ToString();

            if (options_.PrintSSANumbers) {
                var ssaNumber = ReferenceFinder.GetSSADefinitionId(op);

                if(ssaNumber.HasValue)
                {
                    return $"{label}<{ssaNumber.Value.ToString()}>";
                }
            }

            if(op.IsAddress || op.IsLabelAddress)
            {
                return $"&{label}";
            }
            else if(op.IsIndirection)
            {
                return $"[{label}]";
            }

            return label;
        }

        private string GetInstructionLabel(InstructionIR instr)
        {
            var label = instr.OpcodeText.ToString();

            if (instr.Destinations.Count > 0)
            {
                var destOp = instr.Destinations[0];
                string variableName = "";
                string ssaNumber = "";

                if (destOp.HasName && options_.PrintVariableNames)
                {
                    variableName = destOp.NameValue.ToString();
                }

                var ssaTag = destOp.GetTag<SSADefinitionTag>();

                if (ssaTag != null && options_.PrintSSANumbers)
                {
                    ssaNumber = ssaTag.DefinitionId.ToString();
                }

                if (!string.IsNullOrEmpty(variableName))
                {
                    if (!string.IsNullOrEmpty(ssaNumber))
                    {
                        return $"{variableName}<{ssaNumber}> = {label}";
                    }
                    else
                    {
                        return $"{variableName} = {label}";
                    }
                }
                else if (!string.IsNullOrEmpty(ssaNumber))
                {
                    return $"<{ssaNumber}> = {label}";
                }
            }

            return label;
        }

        public override Dictionary<string, IRElement> CreateBlockNodeMap()
        {
            return ElementNameMap;
        }

        public override Dictionary<IRElement, List<IRElement>> CreateBlockNodeGroupsMap()
        {
            return BlockNodeGroupsMap;
        }
    }
}
