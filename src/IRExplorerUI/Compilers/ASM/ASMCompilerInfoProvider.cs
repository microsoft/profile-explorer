// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.ASM;
using System;
using System.Windows;
using System.Windows.Media;

namespace IRExplorerUI.Compilers.ASM {
    public class ASMCompilerInfoProvider : ICompilerInfoProvider {
        private readonly ISession session_;
        private readonly UTCNameProvider names_ = new UTCNameProvider();
        private readonly UTCSectionStyleProvider styles_ = new UTCSectionStyleProvider();
        private readonly UTCRemarkProvider remarks_;
        private readonly ASMCompilerIRInfo ir_;

        public ASMCompilerInfoProvider(ISession session) {
            session_ = session;
            remarks_ = new UTCRemarkProvider(this);
            ir_ = new ASMCompilerIRInfo();
        }

        public string CompilerIRName => "ASM";

        public string OpenFileFilter => "Asm Files|*.asm|All Files|*.*";

        public string DefaultSyntaxHighlightingFile => "ASM";

        public ISession Session => session_;

        public ICompilerIRInfo IR => ir_;

        public INameProvider NameProvider => names_;

        public ISectionStyleProvider SectionStyleProvider => styles_;

        public IRRemarkProvider RemarkProvider => remarks_;

        public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>();

        public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>();

        public List<FunctionTaskDefinition> ScriptFunctionTasks => new List<FunctionTaskDefinition>();

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            return true;
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            return new BasicDiffOutputFilter();
        }

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new BasicBlockFoldingStrategy(function);
        }

        public bool HandleLoadedDocument(IRDocument document, FunctionIR function, IRTextSection section) {
            // Since the ASM blocks don't have a number in the text,
            // attach an overlay label next to the first instr. in the block.
            var settings = document.Settings;
            var overlayHeight = document.TextArea.TextView.DefaultLineHeight - 2;

            foreach (var block in function.Blocks) {
                if (block.Tuples.Count > 0) {
                    var firstTuple = block.Tuples[0];
                    var tooltip = $"B{block.Number}";
                    var overlay = document.AddIconElementOverlay(firstTuple, null, 0, overlayHeight, tooltip,
                                                                 HorizontalAlignment.Left, VerticalAlignment.Center, -6, -1);
                    overlay.ShowOnMarkerBar = false;
                    overlay.IsToolTipPinned = true;
                    overlay.DefaultOpacity = 1;
                    overlay.TextWeight = FontWeights.DemiBold;
                    overlay.TextColor = Brushes.DarkBlue;
                    overlay.ShowBackgroundOnMouseOverOnly = false;
                    overlay.UseToolTipBackground = true;
                    overlay.Padding = 2;
                }
            }

            return true;
        }

        public void ReloadSettings() {
            IRModeUtilities.SetIRModeFromSettings(ir_);
        }
    }
}
