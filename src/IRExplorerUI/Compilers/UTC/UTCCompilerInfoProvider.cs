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
using System.ComponentModel;
using System.IO;

namespace IRExplorerUI.Compilers.UTC {
    public class UTCCompilerInfoProvider : ICompilerInfoProvider {
        private UTCCompilerIRInfo ir_;
        private UTCNameProvider names_;
        private UTCRemarkProvider remarks_;
        private UTCSectionStyleProvider styles_;
        private List<FunctionTaskDefinition> scriptFuncTasks_;

        public UTCCompilerInfoProvider() {
            ir_ = new UTCCompilerIRInfo();
            styles_ = new UTCSectionStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new UTCRemarkProvider(this);
        }

        public string CompilerIRName => "UTC";
        public ICompilerIRInfo IR => ir_;
        public INameProvider NameProvider => names_;
        public ISectionStyleProvider SectionStyleProvider => styles_;
        public IRRemarkProvider RemarkProvider => remarks_;

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new BaseBlockFoldingStrategy(function);
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


        class UnusedInstructionsTaskOptions : IFunctionTaskOptions {
            [DisplayName("Consider only SSA values")]
            [Description("Consider only instructions that have a destination operand in SSA form")]
            public bool HandleOnlySSA { get; set; }

            [DisplayName("Marker color")]
            [Description("Color to be used for marking unused instructions")]
            public Color MarkerColor { get; set; }

            public UnusedInstructionsTaskOptions() {
                Reset();
            }

            public void Reset() {
                HandleOnlySSA = true;
                MarkerColor = Colors.Pink;
            }
        }

        public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>() {
            BuiltinFunctionTask.GetDefinition(
                new FunctionTaskInfo("Unused instructions", "Some description") {
                    HasOptionsPanel = true,
                    OptionsType = typeof(UnusedInstructionsTaskOptions)
                },
                MarkUnusedInstructions)
        };

        public List<FunctionTaskDefinition> ScriptFunctionTasks {
            get {
                if (scriptFuncTasks_ == null) {
                    LoadScriptFunctionTasks();
                }

                return scriptFuncTasks_;
            }
        }

        private void LoadScriptFunctionTasks() {
            scriptFuncTasks_ = new List<FunctionTaskDefinition>();
            var files = App.GetFunctionTaskScripts();

            foreach (var file in files) {
                var text = File.ReadAllText(file);
                var scriptDef = ScriptFunctionTask.GetDefinition(text);

                if (scriptDef != null) {
                    if (string.IsNullOrEmpty(scriptDef.TaskInfo.TargetCompilerIR) ||
                        scriptDef.TaskInfo.TargetCompilerIR == CompilerIRName) {
                        scriptFuncTasks_.Add(scriptDef);
                    }
                }
            }
        }

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
            }

            foreach (var user in ssaDefTag.Users) {
                if (!unusedInstrs.Contains(user.OwnerInstruction)) {
                    return false;
                }
            }

            return true;
        }

        private bool MarkUnusedInstructions(FunctionIR function, IRDocument document, IFunctionTaskOptions options,
                                            ISession session, CancelableTask cancelableTask) {
            var taskOptions = options as UnusedInstructionsTaskOptions;
            var unusedInstr = new HashSet<InstructionIR>();
            var walker = new CFGBlockOrdering(function);

            walker.PostorderWalk((block, index) => {
                foreach (var instr in block.InstructionsBack) {
                    if (IsUnusedInstruction(GetSSADefinitionTag(instr), unusedInstr)) {
                        document.Dispatcher.BeginInvoke((Action)(() => {
                            document.MarkElement(instr, taskOptions.MarkerColor);
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
