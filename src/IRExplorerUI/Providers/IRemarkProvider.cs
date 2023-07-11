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

    public class DummyIRRemarkProvider : IRRemarkProvider {
        public string SettingsFilePath { get; }

        public List<RemarkCategory> RemarkCategories { get; }
        public List<RemarkSectionBoundary> RemarkSectionBoundaries { get; }
        public List<RemarkTextHighlighting> RemarkTextHighlighting { get; }

        public bool SaveSettings() {
            return true;
        }

        public bool LoadSettings() {
            return true;
        }

        public List<IRTextSection> GetSectionList(IRTextSection currentSection, int maxDepth, bool stopAtSectionBoundaries) {
            return null;
        }

        public List<Remark> ExtractAllRemarks(List<IRTextSection> sections, FunctionIR function, LoadedDocument document, RemarkProviderOptions options, CancelableTask cancelableTask) {
            return null;
        }

        public List<Remark> ExtractRemarks(string text, FunctionIR function, IRTextSection section, RemarkProviderOptions options, CancelableTask cancelableTask) {
            return null;
        }

        public List<Remark> ExtractRemarks(List<string> textLines, FunctionIR function, IRTextSection section, RemarkProviderOptions options, CancelableTask cancelableTask) {
            return null;
        }

        public OptimizationRemark GetOptimizationRemarkInfo(Remark remark) {
            return null;
        }
    }

}
