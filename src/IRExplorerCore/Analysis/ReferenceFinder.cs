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
    public enum ReferenceKind {
        Load,
        Store,
        Address,
        SSA
    }

    public sealed class Reference {
        public Reference(IRElement element, ReferenceKind kind) {
            Element = element;
            Kind = kind;
        }

        public IRElement Element { get; set; }
        public ReferenceKind Kind { get; set; }
    }

    public sealed class ReferenceFinder {
        private FunctionIR function_;
        private Dictionary<int, SSADefinitionTag> ssaDefTagMap_;

        public ReferenceFinder(FunctionIR function) {
            function_ = function;
        }

        public void PrecomputeAllReferences() {
            //? TODO using single walk
        }


        //? TODO: Should be moved to the SimilarValueFinder
        public IRElement FindEquivalentValue(IRElement element, bool onlySSA = false) {
            //? TODO: Handle t100 -> tv100 changes in UTC IR
            //? by querying an IR-level interface for "equivalent symbols"
            if (element.ParentFunction == function_) {
                return element;
            }

            if (!(element is OperandIR op)) {
                return null;
            }

            // If it's an SSA value, try to find an operand with the same def. ID.
            var defId = GetSSADefinitionId(op);

            if (defId.HasValue) {
                var foundDefOp = FindOperandWithSSADefinitionID(defId.Value);

                // If found, also check that the symbol name is the same.
                if (foundDefOp != null && IsSameOperand(op, foundDefOp)) {
                    if (foundDefOp.Role == OperandRole.Destination) {
                        return foundDefOp;
                    }
                    else if (op.Role == OperandRole.Source) {
                        // Try to find a matching source operand by looking
                        // for an SSA use that is found in a block with the same ID.
                        var matchingOp = FindBestMatchingOperand(EnumerateSSAUses(foundDefOp),
                                                                 op, false);

                        if (matchingOp != null) {
                            return matchingOp;
                        }
                    }

                    return foundDefOp;
                }
            }

            // For non-SSA values, search the entire function 
            // for a symbol with the same name and type.
            //? TODO: inefficient, use a symbol table indexed by name
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

                if (checkSymbol && !IsSameOperand(useOp, op)) {
                    continue;
                }

                if (useOp.Role == op.Role && IsSameBlock(useOp.ParentBlock, opBlock)) {
                    // If in the same block, pick the use that is the closest
                    // to the original one based on the index in the block.
                    int opBlockIndex = op.ParentTuple.BlockIndex;
                    int useBlockIndex = useOp.ParentTuple.BlockIndex;
                    int distance = Math.Abs(useBlockIndex - opBlockIndex);

                    if (distance == 0) {
                        //return useOp;
                    }
                    else if (distance < bestUseDistance) {
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
                return firstBlock.Label.Name.Span.Equals(secondBlock.Label.Name.Span,
                                                         StringComparison.Ordinal);
            }

            return false;
        }

        private IEnumerable<OperandIR> EnumerateAllOperands(bool includeDestinations = true,
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

        public List<Reference> FindAllDefUsesOrReferences(IRElement element) {
            if (element is OperandIR op) {
                var ssaDef = GetSSADefinition(op);

                if (ssaDef != null) {
                    var list = new List<Reference> { new Reference(ssaDef, ReferenceKind.SSA) };
                    FindSSAUses(op, list);
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
                        if (IsSameOperand(destOp, op)) {
                            if (filterAction != null) {
                                if (!filterAction(destOp, ReferenceKind.Store)) {
                                    continue;
                                }
                            }

                            list.Add(new Reference(destOp, ReferenceKind.Store));
                        }
                    }

                    foreach (var sourceOp in instr.Sources) {
                        if (IsSameOperand(sourceOp, op)) {
                            var refKind = sourceOp.IsAddress
                                ? ReferenceKind.Address
                                : ReferenceKind.Load;

                            if (filterAction != null) {
                                if (!filterAction(sourceOp, refKind)) {
                                    continue;
                                }
                            }

                            list.Add(new Reference(sourceOp, refKind));
                        }
                    }
                }
            }

            if (includeSSAUses) {
                FindSSAUses(op, list);
            }

            return list;
        }

        public List<Reference> FindAllStores(IRElement element) {
            return FindAllReferences(element, false,
                                     (element, kind) => kind == ReferenceKind.Store);
        }

        public List<Reference> FindAllLoads(IRElement element) {
            return FindAllReferences(element, false,
                                     (element, kind) => kind == ReferenceKind.Load);
        }

        public static List<Reference> FindSSAUses(OperandIR  op) {
            var list = new List<Reference>();
            FindSSAUses(op, list);
            return list;
        }

        public static List<Reference> FindSSAUses(InstructionIR instr) {
            var list = new List<Reference>();

            if(instr.Destinations.Count == 0) {
                return list;
            }

            FindSSAUses(instr.Destinations[0], list);
            return list;
        }

        public IRElement FindDefinition(IRElement element) {
            //? TODO: Very inefficient
            var list = FindAllReferences(element, false);
            IRElement definition = null;

            foreach (var reference in list) {
                if (reference.Kind == ReferenceKind.Store) {
                    if (definition == null) {
                        definition = reference.Element;
                    }
                    else {
                        return null; // Multiple definitions.
                    }
                }
            }

            return definition;
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
            return tag != null ? tag.Parent : null;
        }

        public static int? GetSSADefinitionId(OperandIR op) {
            var tag = GetSSADefinitionTag(op);
            return tag?.DefinitionId;
        }

        public static void FindSSAUses(OperandIR op, List<Reference> list) {
            foreach (var use in EnumerateSSAUses(op)) {
                list.Add(new Reference(use, ReferenceKind.SSA));
            }
        }

        public static IRElement GetSingleUse(OperandIR op) {
            var ssaDefTag = op.GetTag<SSADefinitionTag>();

            if (ssaDefTag != null) {
                if (ssaDefTag.HasSingleUser) {
                    return ssaDefTag.Users[0].Parent;
                }
            }

            return null;
        }

        private static IEnumerable<IRElement> EnumerateSSAUses(OperandIR op) {
            // If it's a definition operand, enumerate the SSA uses.
            var ssaDefTag = op.GetTag<SSADefinitionTag>();

            if (ssaDefTag != null) {
                foreach (var use in ssaDefTag.Users) {
                    yield return use.Parent;
                }
            }

            // For source operands, go to the linked definition SSA tag.
            var ssaDefUseTag = op.GetTag<SSAUseTag>();

            if (ssaDefUseTag != null) {
                foreach (var use in ssaDefUseTag.Definition.Users) {
                    yield return use.Parent;
                }
            }
        }

        private bool IsSameOperand(OperandIR op, OperandIR searchedOp,
                                   bool checkType = false) {
            if (op.Kind != searchedOp.Kind) {
                return false;
            }

            if (op.Kind == OperandKind.Variable ||
                op.Kind == OperandKind.Temporary ||
                op.Kind == OperandKind.Address) {
                // Check if symbol names are the same.
                if (!op.NameValue.Span.Equals(searchedOp.NameValue.Span,
                                              StringComparison.Ordinal)) {
                    return false;
                }

                // Check for same type if requested.
                if (checkType && !op.Type.Equals(searchedOp.Type)) {
                    return false;
                }

                return true;
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
    }
}
