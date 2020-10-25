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
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace IRExplorerUI.Compilers.UTC {
    public class UTCCompilerInfoProvider : ICompilerInfoProvider {
        private UTCCompilerIRInfo ir_;
        private ISession session_;
        private UTCNameProvider names_;
        private UTCRemarkProvider remarks_;
        private UTCSectionStyleProvider styles_;
        private List<FunctionTaskDefinition> scriptFuncTasks_;
        private object lockObject_;

        public UTCCompilerInfoProvider(ISession session) {
            session_ = session;
            ir_ = new UTCCompilerIRInfo();
            styles_ = new UTCSectionStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new UTCRemarkProvider(this);
            lockObject_ = new object();

            // Load the list of script tasks in the background to reduce UI delay.
            Task.Run(() => LoadScriptFunctionTasks());
        }

        public string CompilerIRName => "UTC";
        public ISession Session => session_;
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
                //? TODO: Make it a script
                new FunctionTaskInfo("Unused instructions", "Some description") {
                    HasOptionsPanel = true,
                    OptionsType = typeof(UnusedInstructionsTaskOptions)
                },
                MarkUnusedInstructions)
        };

        public List<FunctionTaskDefinition> ScriptFunctionTasks {
            get {
                return LoadScriptFunctionTasks();
            }
        }

        private List<FunctionTaskDefinition> LoadScriptFunctionTasks() {
            lock (lockObject_) {
                if (scriptFuncTasks_ != null) {
                    return scriptFuncTasks_;
                }

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

                return scriptFuncTasks_;
            }
        }

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            CreateInterferenceTag(function, section);

            var loopGraph = new LoopGraph(function);
            loopGraph.FindLoops();
            return true;
        }

        private void CreateInterferenceTag(FunctionIR function, IRTextSection section) {
            //? TODO: Reuse tags for the same IRTextFunction, they don't reference elements
            //? TODO: Not thread-safe

            if (function.HasTag<InterferenceTag>()) {
                return;
            }

            var interfSections = section.ParentFunction.FindAllSections("Tuples after Build Interferences");

            if (interfSections.Count == 0) {
                return;
            }

            var interfSection = interfSections[0];
            var textLines = session_.GetSectionOutputTextLinesAsync(interfSection.OutputBefore, interfSection).Result; //? TODO: await

            var tag = function.GetOrAddTag<InterferenceTag>();
            bool seenInterferingPas = false;

            foreach (var line in textLines) {
                var symPasMatch = Regex.Match(line, @"(\d+):(.*)");

                if (symPasMatch.Success && !seenInterferingPas) {
                    int pas = int.Parse(symPasMatch.Groups[1].Value);
                    var interferingSyms = new List<string>();
                    var other = symPasMatch.Groups[2].Value;

                    other = other.Replace("<Unknown Mem>", "");
                    other = other.Replace("<Untrackable locals:", "");
                    other = other.Replace(">", "");

                    var symbols = other.Split(" ", StringSplitOptions.RemoveEmptyEntries);

                    for (int i = 0; i < symbols.Length; i++) {
                        var symbolName = symbols[i];
                        interferingSyms.Add(symbolName);

                        if (!tag.SymToPasMap.ContainsKey(symbolName)) {
                            tag.SymToPasMap[symbolName] = pas;
                        }
                    }

                    tag.PasToSymMap[pas] = interferingSyms;
                    continue;
                }

                var interferingIndicesMatch = Regex.Match(line, @"(\d+) interferes with: \{ ((\d+)\s)+");

                if (interferingIndicesMatch.Success) {
                    seenInterferingPas = true;
                    var interferingPAS = new HashSet<int>();
                    int basePAS = int.Parse(interferingIndicesMatch.Groups[1].Value);

                    foreach (Capture capture in interferingIndicesMatch.Groups[2].Captures) {
                        interferingPAS.Add(int.Parse(capture.Value));
                    }

                    if (interferingPAS.Count > 0) {
                        tag.InterferingPasMap[basePAS] = interferingPAS;
                    }
                    continue;
                }
            }

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
