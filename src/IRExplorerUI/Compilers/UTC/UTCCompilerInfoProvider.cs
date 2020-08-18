// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using IRExplorerUI.Query;
using IRExplorerUI.Query.Builtin;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;
using System.Collections.Generic;
using IRExplorerUI.UTC;
using System.Windows.Media;
using System;

namespace IRExplorerUI.Compilers.UTC {
    public class UTCCompilerInfoProvider : ICompilerInfoProvider {
        private UTCCompilerIRInfo ir_;
        private UTCNameProvider names_;
        private UTCRemarkProvider remarks_;
        private UTCSectionStyleProvider styles_;

        public UTCCompilerInfoProvider() {
            ir_ = new UTCCompilerIRInfo();
            styles_ = new UTCSectionStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new UTCRemarkProvider();
        }

        public string CompilerIRName => "UTC";
        public ICompilerIRInfo IR => ir_;
        public INameProvider NameProvider => names_;
        public ISectionStyleProvider SectionStyleProvider => styles_;
        public IRRemarkProvider RemarkProvider => remarks_;

        public IRFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new UTCFoldingStrategy(function);
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            return new UTCDiffOutputFilter();
        }

        public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>() {
            InstructionSSAInfoQuery.GetDefinition(),
            OperandSSAInfoQuery.GetDefinition(),
            UTCValueNumberQuery.GetDefinition(),
            UTCBuiltinInterferenceActions.GetDefinition(),
            UTCBuiltinInterferenceQuery.GetDefinition()
        };

        public List<DocumentTaskDefinition> BuiltinScripts => new List<DocumentTaskDefinition>() {
            BuiltinDocumentTask.GetDefinition("Unused instructions", "Smth", MarkUnusedInstructions)
        };

        public bool AnalyzeLoadedFunction(FunctionIR function) {
            var loopGraph = new LoopGraph(function);
            loopGraph.FindLoops();
            return true;
        }

        //? TODO: Extract this into it's own class
        //? Add hooks for quierying IR if an instr is DCE candidate (reject calls for ex)
        private SSADefinitionTag GetSSADefinitionTag(InstructionIR instr) {
            if (instr.Destinations.Count == 0) {
                return null;
            }

            var destOp = instr.Destinations[0];

            if (destOp.IsTemporary) {
                return destOp.GetTag<SSADefinitionTag>();
            }

            return null;
        }

        private bool IsUnusedInstruction(SSADefinitionTag ssaDefTag, HashSet<InstructionIR> unusedInstrs) {
            if (ssaDefTag == null) {
                return false;
            }

            if (!ssaDefTag.HasUsers) {
                return true;
                ;
            }

            foreach (var user in ssaDefTag.Users) {
                if (!unusedInstrs.Contains(user.OwnerInstruction)) {
                    return false;
                }
            }

            return true;
        }

        private bool MarkUnusedInstructions(FunctionIR function, IRDocument document,
                                            ISessionManager session, CancelableTask cancelableTask) {
            var unusedInstr = new HashSet<InstructionIR>();
            var walker = new CFGBlockOrdering(function);

            walker.PostorderWalk((block, index) => {
                foreach (var instr in block.InstructionsBack) {
                    if (IsUnusedInstruction(GetSSADefinitionTag(instr), unusedInstr)) {
                        document.Dispatcher.BeginInvoke((Action)(() => {
                            document.MarkElement(instr, Colors.Pink);
                        }));

                        unusedInstr.Add(instr);
                    }
                }

                return !cancelableTask.IsCanceled;
            });


            return true;
        }
    }
}
