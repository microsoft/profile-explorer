// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

//? TODO: Reference finding for symbols withou SSA can use the reachability graph
//? to trim down the set of potential definitions. Useful in the text viewer for highlighting and goto def.

//? For syms without ref, try to find a block-local source by scanning back
//?    useful especially for registers
//?    can be done in parallel too

namespace IRExplorerCore.Analysis {
    [Flags]
    public enum ReferenceKind {
        Load = 1 << 0,
        Store = 1 << 1,
        Address = 1 << 2,
        SSA = 1 << 3
    }

    public class Reference {
        public Reference(IRElement element, ReferenceKind kind) {
            Element = element;
            Kind = kind;
        }

        public IRElement Element { get; set; }
        public ReferenceKind Kind { get; set; }
    }

    public interface IReachableReferenceFilter {
        public bool FilterDefinitions { get; set; }
        public bool FilterUses { get; set; }

        public bool AcceptReference(IRElement element, IRElement startElement);
        public bool AcceptDefinitionReference(IRElement element, IRElement startSourceElement);
        public bool AcceptUseReference(IRElement element, IRElement startDestElement);
    }

    public sealed class ReferenceFinder {
        private FunctionIR function_;
        private ICompilerIRInfo irInfo_;
        private IReachableReferenceFilter referenceFilter_;
        private Dictionary<int, SSADefinitionTag> ssaDefTagMap_;

        public ReferenceFinder(FunctionIR function, ICompilerIRInfo irInfo = null,
                               IReachableReferenceFilter referenceFilter = null) {
            function_ = function;
            irInfo_ = irInfo;
            referenceFilter_ = referenceFilter;
        }

        public void PrecomputeAllReferences() {
            //? TODO using single walk to collect all refs for each operand
        }


        //? TODO: Should be moved to the SimilarValueFinder
        public IRElement FindEquivalentValue(IRElement element, bool onlySSA = false) {
            //? TODO: Handle t100 -> tv100 changes in UTC IR
            //? by querying an IR-level interface for "equivalent symbols"
            if (element.ParentFunction == function_) {
                return element;
            }

            //? TODO: Handle instructions
            if (!(element is OperandIR op)) {
                return null;
            }

            // If it's an SSA value, try to find an operand with the same def. ID.
            var defId = GetSSADefinitionId(op);

            if (defId.HasValue) {
                var foundDefOp = FindOperandWithSSADefinitionID(defId.Value);

                // If found, also check that the symbol name is the same.
                if (foundDefOp != null && IsSameSymbolOperand(op, foundDefOp)) {
                    if (foundDefOp.Role == OperandRole.Destination) {
                        return foundDefOp;
                    }
                    else if (op.Role == OperandRole.Source) {
                        // Try to find a matching source operand by looking
                        // for an SSA use that is found in a block with the same ID.
                        var matchingOp = FindBestMatchingOperand(EnumerateSSAUses(foundDefOp), op, false);

                        if (matchingOp != null) {
                            return matchingOp;
                        }
                    }

                    return foundDefOp;
                }
            }

            // For non-SSA values, search the entire function 
            // for a symbol with the same name and type.
            //? TODO: inefficient, use a symbol table indexed by name (PrecomputeAllReferences)
            return onlySSA ? null : FindBestMatchingOperand(EnumerateAllOperands(), op);
        }

        private IRElement FindBestMatchingOperand(IEnumerable<IRElement> candidates,
                                                  OperandIR op, bool checkSymbol = true) {
            // Try to find a matching source operand by looking
            // for an SSA use that is found in a block with the same ID.
            var opBlock = op.ParentBlock;
            OperandIR firstUse = null;
            OperandIR bestUse = null;
            int bestUseDistance = int.MaxValue;

            foreach (var useElement in candidates) {
                if (!(useElement is OperandIR useOp)) {
                    continue;
                }

                if (checkSymbol && !IsSameSymbolOperand(useOp, op)) {
                    continue;
                }

                if (useOp.Role == op.Role && IsSameBlock(useOp.ParentBlock, opBlock)) {
                    // If in the same block, pick the use that is the closest
                    // to the original one based on the index in the block.
                    int opBlockIndex = op.ParentTuple.IndexInBlock;
                    int useBlockIndex = useOp.ParentTuple.IndexInBlock;
                    int distance = Math.Abs(useBlockIndex - opBlockIndex);
                    
                    if (distance < bestUseDistance) {
                        bestUse = useOp;
                        bestUseDistance = distance;
                    }
                }

                firstUse ??= useOp;
            }

            // Return first use if one in the same block not found.
            if (bestUse != null) {
                return bestUse;
            }
            else if (firstUse != null) {
                return firstUse;
            }

            return null;
        }

        private bool IsSameBlock(BlockIR firstBlock, BlockIR secondBlock) {
            if (firstBlock == secondBlock || firstBlock.Number == secondBlock.Number) {
                return true;
            }

            if (firstBlock.HasLabel && secondBlock.HasLabel) {
                return firstBlock.Label.Name == secondBlock.Label.Name;
            }

            return false;
        }

        public IEnumerable<OperandIR> EnumerateAllOperands(bool includeDestinations = true,
                                                            bool includeSources = true) {
            foreach (var block in function_.Blocks) {
                foreach (var tuple in block.Tuples) {
                    if (!(tuple is InstructionIR instr)) {
                        continue;
                    }

                    if (includeDestinations) {
                        foreach (var destOp in instr.Destinations) {
                            yield return destOp;
                        }
                    }

                    if (includeSources) {
                        foreach (var sourceOp in instr.Sources) {
                            yield return sourceOp;
                        }
                    }
                }
            }
        }

        public OperandIR FindOperandWithSSADefinitionID(int defId) {
            // Build a cache mapping the definition IDs to the tag
            // to speed up lookup and avoid n-squared behavior.
            if (ssaDefTagMap_ == null) {
                ssaDefTagMap_ = new Dictionary<int, SSADefinitionTag>();

                foreach (var destOp in EnumerateAllOperands(true, false)) {
                    var ssaDefTag = GetSSADefinitionTag(destOp);

                    if (ssaDefTag != null) {
                        ssaDefTagMap_[ssaDefTag.DefinitionId] = ssaDefTag;
                    }
                }
            }

            return ssaDefTagMap_.TryGetValue(defId, out var tag)
                ? tag.DefinitionOperand
                : null;
        }

        public List<Reference> FindAllSSAUsesOrReferences(IRElement element) {
            if (element is OperandIR op) {
                var ssaDef = GetSSADefinition(op);

                if (ssaDef != null) {
                    var list = new List<Reference> { new Reference(ssaDef, ReferenceKind.SSA) };
                    var useList = FindSSAUses(op);
                    useList.ForEach((item) => list.Add(new Reference(item, ReferenceKind.SSA)));
                    return list;
                }
            }

            return FindAllReferences(element);
        }

        public List<Reference> FindAllReferences(IRElement element, bool includeSSAUses = true,
                                                 Func<IRElement, ReferenceKind, bool>
                                                 filterAction = null) {
            var list = new List<Reference>();

            if (!(element is OperandIR op)) {
                return list;
            }

            foreach (var block in function_.Blocks) {
                foreach (var tuple in block.Tuples) {
                    if (!(tuple is InstructionIR instr)) {
                        continue;
                    }

                    foreach (var destOp in instr.Destinations) {
                        var comparedOp = destOp;
                        var refKind = ReferenceKind.Store;

                        if (destOp.IsIndirection) {
                            comparedOp = destOp.IndirectionBaseValue;
                            refKind = ReferenceKind.Load;
                        }

                        if (IsSameSymbolOperand(comparedOp, op)) {
                            CreateReference(destOp, refKind, list, filterAction);
                        }
                    }

                    foreach (var sourceOp in instr.Sources) {
                        var comparedOp = sourceOp;

                        if (sourceOp.IsIndirection) {
                            comparedOp = sourceOp.IndirectionBaseValue;
                        }

                        if (IsSameSymbolOperand(comparedOp, op)) {
                            var refKind = comparedOp.IsAddress
                                ? ReferenceKind.Address
                                : ReferenceKind.Load;
                            CreateReference(sourceOp, refKind, list, filterAction);
                        }
                    }
                }
            }

            // Also go over the function parameters.
            foreach (var paramOp in function_.Parameters) {
                if (IsSameSymbolOperand(paramOp, op)) {
                    CreateReference(paramOp, ReferenceKind.Store, list, filterAction);
                }
            }

            if (includeSSAUses) {
                var useList = FindSSAUses(op);
                useList.ForEach((item) => list.Add(new Reference(item, ReferenceKind.SSA)));
            }

            return list;
        }

        private void CreateReference(OperandIR operand, ReferenceKind refKind, List<Reference> list,
                                     Func<IRElement, ReferenceKind, bool> filterAction) {
            if (filterAction != null) {
                if (!filterAction(operand, refKind)) {
                    return;
                }
            }

            list.Add(new Reference(operand, refKind));
        }

        public List<Reference> FindAllStores(IRElement element) {
            return FindAllReferences(element, false,
                                     (element, kind) => kind == ReferenceKind.Store);
        }

        public List<Reference> FindAllLoads(IRElement element) {
            return FindAllReferences(element, false,
                                     (element, kind) => kind == ReferenceKind.Load);
        }

        public static List<IRElement> FindSSAUses(OperandIR op) {
            var list = new List<IRElement>();
            FindSSAUses(op, list);
            return list;
        }

        public static List<IRElement> FindSSAUses(InstructionIR instr) {
            var list = new List<IRElement>();

            if (instr.Destinations.Count == 0) {
                return list;
            }

            FindSSAUses(instr.Destinations[0], list);
            return list;
        }

        public List<IRElement> FindAllUses(OperandIR op) {
            var list = new List<IRElement>();
            return FindAllUses(op, list);
        }

        public List<IRElement> FindAllUses(InstructionIR instr) {
            var list = new List<IRElement>();

            if (instr.Destinations.Count == 0) {
                return list;
            }

            FindAllUses(instr.Destinations[0], list);
            return list;
        }

        private List<IRElement> FindAllUses(OperandIR op, List<IRElement> list) {
            foreach (var use in EnumerateSSAUses(op)) {
                list.Add(use);
            }

            // If there is no SSA info, collect all the symbol loads.
            if(list.Count == 0 && op.GetTag<SSADefinitionTag>() == null) {
                var allLoads = FindAllLoads(op);

                foreach (var reference in allLoads) {
                    if (AcceptReferenceForDestination(reference.Element, op)) {
                        list.Add(reference.Element);
                    }
                }
            }

            return list;
        }

        public IRElement FindSingleDefinition(IRElement element) {
            // Try to use SSA info first.
            if (element is OperandIR op) {
                var ssaDefOp = GetSSADefinition(op);

                if (ssaDefOp != null) {
                    return ssaDefOp;
                }
            }

            //? TODO: Very inefficient
            var list = FindAllDefinitions(element);

            if (list.Count == 1) {
                return list[0];
            }

            return null;
        }

        public List<IRElement> FindAllDefinitions(IRElement element) {
            var list = new List<IRElement>();

            // Try to use SSA info first.
            if (element is OperandIR op) {
                var ssaDefOp = GetSSADefinition(op);

                if (ssaDefOp != null) {
                    list.Add(ssaDefOp);
                    return list;
                }
                else if (op.IsIndirection) {
                    // Search for the base operand of an indirection.
                    return FindAllDefinitions(op.IndirectionBaseValue);
                }
            }

            //? TODO: Very inefficient
            var refList = FindAllStores(element);

            foreach (var reference in refList) {
                if (AcceptReferenceForSource(reference.Element, element)) {
                    list.Add(reference.Element);
                }
            }

            return list;
        }

        public static SSADefinitionTag GetSSADefinitionTag(OperandIR op) {
            var defLinkTag = op.GetTag<SSAUseTag>();

            if (defLinkTag != null) {
                return defLinkTag.Definition;
            }

            if (op.IsIndirection) {
                return GetSSADefinitionTag(op.IndirectionBaseValue);
            }

            return op.GetTag<SSADefinitionTag>();
        }

        public static IRElement GetSSADefinition(OperandIR op) {
            var tag = GetSSADefinitionTag(op);
            return tag?.OwnerElement;
        }

        public static InstructionIR GetSSADefinitionInstruction(OperandIR op) {
            var tag = GetSSADefinitionTag(op);
            return tag?.DefinitionInstruction;
        }

        public static int? GetSSADefinitionId(OperandIR op) {
            var tag = GetSSADefinitionTag(op);
            return tag?.DefinitionId;
        }

        public static void FindSSAUses(OperandIR op, List<IRElement> list) {
            foreach (var use in EnumerateSSAUses(op)) {
                list.Add(use);
            }
        }

        public IRElement GetSingleUse(OperandIR op) {
            var ssaDefTag = op.GetTag<SSADefinitionTag>();

            if (ssaDefTag != null) {
                if (ssaDefTag.HasSingleUser) {
                    return ssaDefTag.Users[0].OwnerElement;
                }

                return null;
            }

            var useList = FindAllUses(op);

            if(useList.Count == 1) {
                return useList[0];
            }

            return null;
        }

        private static IEnumerable<IRElement> EnumerateSSAUses(OperandIR op) {
            // If it's a definition operand, enumerate the SSA uses.
            var ssaDefTag = op.GetTag<SSADefinitionTag>();

            if (ssaDefTag != null) {
                foreach (var use in ssaDefTag.Users) {
                    yield return use.OwnerElement;
                }
            }

            // For source operands, go to the linked definition SSA tag.
            var ssaDefUseTag = op.GetTag<SSAUseTag>();

            if (ssaDefUseTag != null) {
                foreach (var use in ssaDefUseTag.Definition.Users) {
                    yield return use.OwnerElement;
                }
            }
        }

        public bool IsSameSymbolOperand(OperandIR op, OperandIR searchedOp,
                                         bool checkType = false,
                                         bool exactCheck = false) {
            if (!AreSymbolOperandsCompatible(op, searchedOp)) {
                return false;
            }

            if (op.Kind == OperandKind.Variable ||
                op.Kind == OperandKind.Temporary ||
                op.Kind == OperandKind.Address) {
                // Check if symbol names are the same.
                if (!op.NameValue.Span.Equals(searchedOp.NameValue.Span,
                                              StringComparison.Ordinal)) {
                    // Not same symbol name, but the IR may define names
                    // that should be considered to represent the same symbol.
                    if (irInfo_ == null || !irInfo_.OperandsReferenceSameSymbol(op, searchedOp, exactCheck)) {
                        return false;
                    }

                    // Check for same type if requested.
                    return !checkType || op.Type.Equals(searchedOp.Type);
                }

                // The same symbol name, but the IR may define names
                // that should not be considered the same symbol (different offsets, for ex.)
                if (irInfo_ != null && !irInfo_.OperandsReferenceSameSymbol(op, searchedOp, exactCheck)) {
                    return false;
                }

                return true;
            }

            return false;
        }

        private bool AreSymbolOperandsCompatible(OperandIR op1, OperandIR op2) {
            if (op1.Kind == op2.Kind) {
                return true;
            }

            if (op1.Kind == OperandKind.Variable ||
                op1.Kind == OperandKind.Address) {
                return op2.Kind == OperandKind.Variable ||
                       op2.Kind == OperandKind.Address;
            }

            return false;
        }

        public static string GetSymbolName(OperandIR op) {
            if (op.Kind == OperandKind.Variable ||
                op.Kind == OperandKind.Temporary ||
                op.Kind == OperandKind.Address) {
                return op.NameValue.ToString();
            }

            return "";
        }

        private bool AcceptReference(IRElement element, IRElement startElement) {
            if(referenceFilter_ != null) {
                return referenceFilter_.AcceptReference(element, startElement);
            }

            return true;
        }
        private bool AcceptReferenceForSource(IRElement element, IRElement startSourceElement) {
            if (referenceFilter_ != null) {
                return referenceFilter_.AcceptDefinitionReference(element, startSourceElement);
            }

            return true;
        }
        private bool AcceptReferenceForDestination(IRElement element, IRElement startDestElement) {
            if (referenceFilter_ != null) {
                return referenceFilter_.AcceptUseReference(element, startDestElement);
            }

            return true;
        }
    }
}
