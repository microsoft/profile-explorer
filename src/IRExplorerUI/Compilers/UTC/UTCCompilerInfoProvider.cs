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
using System;
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
        public string DefaultSyntaxHighlightingFile => "UTC IR";
        public string OpenFileFilter => "IR Files|*.txt;*.log;*.ir;*.tup;*.out;*.irx|IR Explorer Session Files|*.irx|All Files|*.*";
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
            UTCBuiltinInterferenceQuery.GetDefinition(),
            UTCRegisterQuery.GetDefinition(),
            UTCValueNumberQuery.GetDefinition(),
            InstructionSSAInfoQuery.GetDefinition(),
            OperandSSAInfoQuery.GetDefinition()
        };

        public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>() {
            BuiltinFunctionTask.GetDefinition(
                //? TODO: Make it a script
                new FunctionTaskInfo(Guid.Parse("B3B91F6E-5A00-4E47-9B25-DB31F6E2395C"),
                                   "Unused instructions", "Some description") {
                    HasOptionsPanel = true,
                    OptionsType = typeof(UnusedInstructionsTaskOptions)
                },
                UnusedInstructionsFunctionTask.MarkUnusedInstructions)
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

                    if (scriptDef != null && scriptDef.IsCompatibleWith(CompilerIRName)) {
                        scriptFuncTasks_.Add(scriptDef);
                    }
                }

                return scriptFuncTasks_;
            }
        }

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            lock (lockObject_) {
                UTCBuiltinInterferenceQuery.CreateInterferenceTag(function, section, session_);
            }

            var loopGraph = new LoopGraph(function);
            loopGraph.FindLoops();
            return true;
        }

        public void ReloadSettings() {
            // Set the IR parsing mode (target architecture)
            // based on the syntax highlighting file selected.
            var path = App.GetSyntaxHighlightingFilePath();

            if (!string.IsNullOrEmpty(path) && 
                path.Contains("arm64", StringComparison.OrdinalIgnoreCase)) {
                ir_.IRMode = IRMode.ARM64;
            }
            else {
                ir_.IRMode = IRMode.x86;
            }
        }
    }
}
