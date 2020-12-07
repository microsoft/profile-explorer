using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.Analysis;
using IRExplorerCore.UTC;
using IRExplorerUI.Query;
using IRExplorerUI;
using IRExplorerUI.Scripting;
using System.Collections.Generic;
using System;
using System.Windows.Media;
using System.ComponentModel;

public class Script {
    class Options : IFunctionTaskOptions {
        [DisplayName("Check value definition dominance")]
        [Description("Checks that the definition of each source value dominates the instruction")]
        public bool CheckDominance { get; set; }

        [DisplayName("Check live range overlap")]
        [Description("Checks that there is no live range overlap of multiple SSA values of the same symbol")]
        public bool CheckLiveRangeOverlap { get; set; }

        [DisplayName("Error marker color (dominance)")]
        [Description("Color to be used for marking dominance errors")]
        public Color MarkerColor { get; set; }

        [DisplayName("Error marker color (live range)")]
        [Description("Color to be used for marking live range overlap errors")]
        public Color LiveRangeMarkerColor { get; set; }

         [DisplayName("Error marker color (dominance)")]
        [Description("Color to be used for marking the definition operand")]
        public Color DefinitionMarkerColor { get; set; }

        public Options() {
            Reset();
        }

        public void Reset() {
            CheckDominance = true;
            CheckLiveRangeOverlap = true;
            MarkerColor = Colors.Pink;
            LiveRangeMarkerColor = Colors.LightSalmon;
            DefinitionMarkerColor = Colors.Gold;
        }
    }

    public FunctionTaskInfo GetTaskInfo() {
        return new FunctionTaskInfo(Guid.Parse("C18CD53C-7BE6-4893-BCFC-B093DD5FD91C"),
                                    "SSA form checks", "Some description") {
            HasOptionsPanel = true,
            OptionsType = typeof(Options)
        };
    }

    public bool Execute(ScriptSession s) {
    	var func = s.CurrentFunction;    	
        var options = (Options)s.SessionObject;
    	
        var domTree = s.Analysis.DominatorTree;
        var refs = s.Analysis.References;
        bool failed = false;

        foreach(var block in func.Blocks) {
            var visitedInstrs = new HashSet<InstructionIR>();

            foreach(var instr in block.Instructions) {
                int sourceOpIndex = -1;

                foreach(var sourceOp in instr.Sources) {
                    sourceOpIndex++;
                    var defOp = ReferenceFinder.GetSSADefinition(sourceOp);
                    if(defOp == null) continue; // No SSA info.

                    var defInstr = defOp.ParentInstruction;
                    
                    if(defInstr == null) {
						continue; // Constants, params, etc. dominate.
                    }

                    var checkedBlock = block;
                    var defBlock = defInstr.ParentBlock;
                    bool isPhiIncomingBlock = false;

                    if (s.IR.IsPhiInstruction(instr)) {
                        // For a PHI, the incoming operand must dominate the corresponding predecessor.
                        checkedBlock = s.IR.GetIncomingPhiOperandBlock(instr, sourceOpIndex);
                        isPhiIncomingBlock = true;
                    }
                                            
                    if(options.CheckDominance)
                    {
                        if(!CheckDominance(checkedBlock, block, instr, isPhiIncomingBlock,
                                           defInstr, defBlock, visitedInstrs, domTree, options, s)) {
                            ReportDomFailure(s, checkedBlock, instr, sourceOp, defOp, defBlock, options);
                            failed = true;
                        }
                    }

                    if (options.CheckLiveRangeOverlap) {
                        // Walk dominator tree between def. block and current block
                        // and check if there is any other definition of the same symbol.
                        InstructionIR redefinitionInstr = null;

                        if(defBlock == checkedBlock) {
                            CheckRedefinition(instr, checkedBlock, defInstr, defBlock, refs, out redefinitionInstr);
                        }
                        else {
                            BlockIR domBlock = checkedBlock;

                            do
                            {
                                domBlock = domTree.GetImmediateDominator(domBlock);
                                
                                if(!CheckRedefinition(instr, domBlock, defInstr, defBlock, refs, out redefinitionInstr)) {
                                    break;
                                }

                            } while (domBlock != null && domBlock != defBlock);
                        }

                        if(redefinitionInstr != null) {
                            ReportOverlapFailure(s, checkedBlock, instr, defOp, defBlock, redefinitionInstr, options);
                            failed = true;
                        }
                    }
                }

                visitedInstrs.Add(instr);
            }
    	}

        if(failed) {
            s.SetSessionResult(false, "SSA correctness errors found");
        }
        else {
            s.SetSessionResult(true, "No problems found");
        }

        return true;
    }

    private bool CheckDominance(BlockIR checkedBlock, BlockIR userBlock, InstructionIR userInstr,
                                bool isPhiIncomingBlock, InstructionIR defInstr, BlockIR defBlock,   
                                HashSet<InstructionIR> visitedInstrs, DominatorAlgorithm domTree, 
                                Options options, ScriptSession s) {
        if (!domTree.Dominates(defBlock, checkedBlock)) {
            //s.WriteLine($"Dom failure for defBlock {defBlock.Number} and {checkedBlock.Number}");
            return false;
        }
        else if (defBlock == userBlock) {
            // Check that the def is defined before the user in the same block.
            // If the instr. being checked is a PHI, with a loop back-edge the def.
            // would be found after the PHI at the top of the block, which is expected.
            if(isPhiIncomingBlock) {
                return true;
            }

            if (!visitedInstrs.Contains(defInstr)) {
                //s.WriteLine($"Same block failure for defBlock {defBlock.Number} and userBlock {checkedBlock.Number} and {checkedBlock.Number} ");
                return false;
            }
        }

        return true;
    }

    private bool CheckRedefinition(InstructionIR userInstr, BlockIR domBlock, InstructionIR defInstr, 
                                    BlockIR defBlock, ReferenceFinder refs, out InstructionIR redefinitionInstr) {
        bool defInstrSeen = domBlock != defBlock;
        var defDestOp = defInstr.Destinations[0];

        foreach(var instr in domBlock.Instructions) {
            if(!defInstrSeen) {
                defInstrSeen = instr == defInstr;
            }
            else {
                if(instr == userInstr) {
                    break;
                }

                // Check if the def. operand is overwritten.
                foreach(var destOp in instr.Destinations) {
                    if(refs.IsSameSymbolOperand(destOp, defDestOp, checkType:false, exactCheck:true)) {
                        redefinitionInstr = instr;
                        return false; // Redefinition.
                    }
                }
            }
        }

        redefinitionInstr = null;
        return true;
    }

    private void ReportDomFailure(ScriptSession s, BlockIR block, InstructionIR instr, 
                                  OperandIR sourceOp, IRElement defOp, BlockIR defBlock, Options options) {
        s.Mark(instr, options.MarkerColor);
        s.Mark(defOp, options.DefinitionMarkerColor);
        s.Write($"Dominance issue for instr in block {block.Number}: "); s.WriteLine(instr);
        s.Write("    for source "); s.WriteLine(sourceOp);
        s.Write($"    defined in block {defBlock.Number}: "); s.WriteLine(defOp);
    }

      private void ReportOverlapFailure(ScriptSession s, BlockIR block, InstructionIR instr, 
                                        IRElement defOp, BlockIR defBlock, 
                                        InstructionIR redefinitionInstr, Options options) {
        s.Mark(instr, options.LiveRangeMarkerColor);
        s.Mark(redefinitionInstr, options.DefinitionMarkerColor);
        s.Write($"Live-range overlap issue for instr in block {block.Number}: "); s.WriteLine(instr);
        s.Write($"    defined in block {defBlock.Number}: "); s.WriteLine(defOp);
        s.Write($"    redefined in block {redefinitionInstr.ParentBlock.Number}: "); s.WriteLine(redefinitionInstr);
    }
}
