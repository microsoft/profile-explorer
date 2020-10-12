// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public class OptimizationRemark //: PassRemark
    {
        public string OptimizationName { get; set; }
        public object Info { get; set; }
    }

    public class RemarkProviderOptions {
        public bool FindInstructionRemarks { get; set; }
        public bool FindOperandRemarks { get; set; }
        public bool IgnoreOverlappingOperandRemarks { get; set; }

        public RemarkProviderOptions() {
            FindInstructionRemarks = true;
            FindOperandRemarks = true;
            IgnoreOverlappingOperandRemarks = false;
        }

        //? multi-threading, max cores
    }

    public interface IRRemarkProvider {
        // per-section cache of remarks, don't have to re-parse all output
        // when switching sections

        string SettingsFilePath { get; }

        bool SaveSettings();
        bool LoadSettings();
        List<RemarkCategory> LoadRemarkCategories();
        List<RemarkSectionBoundary> LoadRemarkSectionBoundaries();
        List<RemarkTextHighlighting> LoadRemarkTextHighlighting();

        List<IRTextSection> GetSectionList(IRTextSection currentSection, int maxDepth, bool stopAtSectionBoundaries);
        List<Remark> ExtractAllRemarks(List<IRTextSection> sections, FunctionIR function, LoadedDocument document,
                                       RemarkProviderOptions options);

        //? TODO: Should use a CancelableTaskInfo to support fast canceling when section switches
        List<Remark> ExtractRemarks(string text, FunctionIR function, IRTextSection section, RemarkProviderOptions options);
        OptimizationRemark GetOptimizationRemarkInfo(Remark remark);
    }
}
