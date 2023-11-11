// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
        List<RemarkCategory> RemarkCategories { get; }

        List<RemarkSectionBoundary> RemarkSectionBoundaries { get; }

        List<RemarkTextHighlighting> RemarkTextHighlighting { get; }

        List<IRTextSection> GetSectionList(IRTextSection currentSection, int maxDepth, bool stopAtSectionBoundaries);

        List<Remark> ExtractAllRemarks(List<IRTextSection> sections, FunctionIR function, LoadedDocument document,
                                       RemarkProviderOptions options, CancelableTask cancelableTask);

        List<Remark> ExtractRemarks(string text, FunctionIR function,
                                    IRTextSection section, RemarkProviderOptions options,
                                    CancelableTask cancelableTask);
        List<Remark> ExtractRemarks(List<string> textLines, FunctionIR function, 
                                    IRTextSection section, RemarkProviderOptions options, 
                                    CancelableTask cancelableTask);
        OptimizationRemark GetOptimizationRemarkInfo(Remark remark);
    }
}
