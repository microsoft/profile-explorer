﻿using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis {
    public class CallSite : TaggedObject {
        public ulong CallInstructionId { get; set; }
        public CallGraphNode Target{ get; set; }
        public CallGraphNode Parent { get; set; }

        public CallSite(ulong callInstrId, CallGraphNode target, CallGraphNode parent) {
            CallInstructionId = callInstrId;
            Target = target;
            Parent = parent;
        }

        public CallSite(InstructionIR callInstr, CallGraphNode target, CallGraphNode parent) :
            this(callInstr.Id, target, parent) {
        }

        public override bool Equals(object obj) {
            return obj is CallSite site &&
                   CallInstructionId == site.CallInstructionId &&
                   EqualityComparer<CallGraphNode>.Default.Equals(Target, site.Target) &&
                   EqualityComparer<CallGraphNode>.Default.Equals(Parent, site.Parent);
        }

        public override int GetHashCode() {
            return HashCode.Combine(CallInstructionId, Target, Parent);
        }

        public static bool operator ==(CallSite left, CallSite right) {
            return EqualityComparer<CallSite>.Default.Equals(left, right);
        }

        public static bool operator !=(CallSite left, CallSite right) {
            return !(left == right);
        }
    }

    public enum CallGraphNodeKind {
        Internal,
        External
    }

    public class CallGraphNode : TaggedObject {
        public string FunctionName { get; set; }
        public int Number { get;set; }
        public CallGraphNodeKind Kind { get; set; }
        public List<CallSite> Callers { get; set; }
        public List<CallSite> Callees { get; set; }

        public CallGraphNode(string funcName, int number, CallGraphNodeKind kind) {
            FunctionName = funcName;
            Number = number;
            Kind = kind;
        }

        public bool HasCallers => Callers != null && Callers.Count > 0;
        public bool HasCallees => Callees != null && Callees.Count > 0;
        public bool IsLeafFunction => !HasCallees;
        public bool IsInternal => Kind == CallGraphNodeKind.Internal;
        public bool IsExternal => Kind == CallGraphNodeKind.External;

        public void AddCallee(CallSite callsite) {
            Callees ??= new List<CallSite>();
            Callees.Add(callsite);
        }

        public void AddCaller(CallSite callsite) {
            Callers ??= new List<CallSite>();
            Callers.Add(callsite);
        }

        public override bool Equals(object obj) {
            return obj is CallGraphNode node &&
                   FunctionName == node.FunctionName;
        }

        public override int GetHashCode() {
            return HashCode.Combine(FunctionName);
        }

        public IEnumerable<CallGraphNode> UniqueCallers {
            get {
                if (Callers != null) {
                    var nodeSet = new HashSet<CallGraphNode>();

                    foreach (var callsite in Callers) {
                        if (nodeSet.Add(callsite.Parent)) {
                            yield return callsite.Parent;
                        }
                    }
                }
            }
        }

        public IEnumerable<CallGraphNode> UniqueCallees {
            get {
                if (Callees != null) {
                    var nodeSet = new HashSet<CallGraphNode>();

                    foreach (var callsite in Callees) {
                        if (nodeSet.Add(callsite.Target)) {
                            yield return callsite.Target;
                        }
                    }
                }
            }
        }

        public static bool operator ==(CallGraphNode left, CallGraphNode right) {
            return EqualityComparer<CallGraphNode>.Default.Equals(left, right);
        }

        public static bool operator !=(CallGraphNode left, CallGraphNode right) {
            return !(left == right);
        }
    }

    public class CallGraph {
        private List<CallGraphNode> nodes_;
        private List<CallGraphNode> entryNodes_;
        private Dictionary<IRTextFunction, CallGraphNode> funcToNodeMap_;
        private Dictionary<string, CallGraphNode> externalFuncToNodeMap_;
        private HashSet<IRTextFunction> visitedFuncts_;
        private IRTextSummary summary_;
        private IRTextSectionLoader loader_;
        private ICompilerIRInfo irInfo_;
        private int nextCallNodeId_;

        public List<CallGraphNode> EntryFunctionNodes => entryNodes_;
        public List<CallGraphNode> FunctionNodes => nodes_;

        public CallGraph(IRTextSummary summary, IRTextSectionLoader loader, ICompilerIRInfo irInfo) {
            summary_ = summary;
            loader_ = loader;
            irInfo_ = irInfo;
            nodes_ = new List<CallGraphNode>(summary.Functions.Count);
            funcToNodeMap_ = new Dictionary<IRTextFunction, CallGraphNode>(summary.Functions.Count);
            externalFuncToNodeMap_ = new Dictionary<string, CallGraphNode>();
            visitedFuncts_ = new HashSet<IRTextFunction>();
            entryNodes_ = new List<CallGraphNode>();
        }

        public void Execute(string sectionName) {
            foreach(var func in summary_.Functions) {
                if(visitedFuncts_.Contains(func)) {
                    continue;
                }

                BuildCallSubgraph(func, sectionName);
            }

            // Find entry functions.
            foreach(var node in nodes_) {
                if(!node.HasCallees) {
                    entryNodes_.Add(node);
                }
            }
        }

        public CallGraphNode Execute(IRTextFunction startFunction, string sectionName) {
            BuildCallSubgraph(startFunction, sectionName);
            return GetOrCreateNode(startFunction);
        }

        private void BuildCallSubgraph(IRTextFunction startFunction, string sectionName) {
            var worklist = new Queue<IRTextFunction>();
            worklist.Enqueue(startFunction);
            visitedFuncts_.Add(startFunction);

            while (worklist.Count > 0) {
                var func = worklist.Dequeue();
                var funcNode = GetOrCreateNode(func);

                if (funcNode == null) {
                    continue; // An unknown/external function, ignore.
                }

                var funcIR = LoadSection(func, sectionName);

                if(funcIR == null) {
                    continue; // Failed to parse function, ignore.
                }

                foreach (var instr in funcIR.AllInstructions) {
                    if (irInfo_.IsCallInstruction(instr)) {
                        var callTarget = irInfo_.GetCallTarget(instr);

                        if (callTarget != null && callTarget.IsAddress) {
                            var calleeFuncName = callTarget.NameValue.ToString();
                            var calleeNode = GetOrCreateNode(calleeFuncName);

                            var callsite = new CallSite(instr, calleeNode, funcNode);
                            funcNode.AddCallee(callsite);
                            calleeNode.AddCaller(callsite);

                            // If the function has a definition, add it to the worklist.
                            var calleeFunc = summary_.FindFunction(calleeFuncName);

                            if (calleeFunc != null && visitedFuncts_.Add(calleeFunc)) {
                                worklist.Enqueue(calleeFunc);
                            }
                        }
                        else {
                            //? TODO: Add a "Unknown target" node
                        }
                    }
                }
            }
        }

        private CallGraphNode GetOrCreateNode(IRTextFunction func) {
            return GetOrCreateNode(func.Name);
        }

        private CallGraphNode GetOrCreateNode(string funcName) {
            var func = summary_.FindFunction(funcName);

            if(func == null) {
                // Consider that it's an external function without definition.
                if(externalFuncToNodeMap_.TryGetValue(funcName, out var externalNode)) {
                    return externalNode;
                }

                externalNode = new CallGraphNode(funcName, GetNextCallNodeId(),
                                                 CallGraphNodeKind.External);
                externalFuncToNodeMap_[funcName] = externalNode;
                nodes_.Add(externalNode);
                return externalNode;
            }

            if(funcToNodeMap_.TryGetValue(func, out var node)) {
                return node;
            }

            node = new CallGraphNode(funcName, GetNextCallNodeId(),
                                     CallGraphNodeKind.Internal);
            funcToNodeMap_[func] = node;
            nodes_.Add(node);
            return node;
        }

        private int GetNextCallNodeId() {
            return nextCallNodeId_++;
        }

        private FunctionIR LoadSection(IRTextFunction func, string sectionName) {
            var section = func.FindSection(sectionName);

            if(section == null) {
                return null;
            }

            var loadedDoc = loader_.LoadSection(section);
            return loadedDoc?.Function;
        }
    }
}
