// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using ICSharpCode.AvalonEdit.Document;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public class DiffMarkingResult {
        public TextDocument DiffDocument;
        internal FunctionIR DiffFunction;
        public List<DiffTextSegment> DiffSegments;
        public string DiffText;
        public bool FunctionReparsingRequired;
        public int CurrentSegmentIndex;

        public DiffMarkingResult(TextDocument diffDocument) {
            DiffDocument = diffDocument;
            DiffSegments = new List<DiffTextSegment>();
        }
    }

    public class DiffModeInfo {
        public ManualResetEvent DiffModeChangeCompleted;

        public DiffModeInfo() {
            DiffModeChangeCompleted = new ManualResetEvent(true);
        }

        public bool IsEnabled { get; set; }
        public IRDocumentHost LeftDocument { get; set; }
        public IRDocumentHost RightDocument { get; set; }
        public IRTextSection LeftSection { get; set; }
        public IRTextSection RightSection { get; set; }
        public IRDocumentHost IgnoreNextScrollEventDocument { get; set; }
        public DiffMarkingResult LeftDiffResults { get; set; }
        public DiffMarkingResult RightDiffResults { get; set; }

        public void StartModeChange() {
            // If a diff-mode change is in progress, wait until it's done.
            DiffModeChangeCompleted.WaitOne();
            DiffModeChangeCompleted.Reset();
        }

        public void EndModeChange() {
            DiffModeChangeCompleted.Set();
        }

        public void End() {
            IsEnabled = false;
            LeftDocument = RightDocument = null;
            LeftSection = RightSection = null;
            IgnoreNextScrollEventDocument = null;
        }

        public bool IsDiffDocument(IRDocumentHost docHost) {
            return docHost == LeftDocument || docHost == RightDocument;
        }

        public bool IsDiffFunction(FunctionIR function) {
            return function == LeftDiffResults.DiffFunction ||
                   function == RightDiffResults.DiffFunction;
        }

        public IRDocumentHost GetOtherDocument(IRDocumentHost docHost) {
            if(docHost == LeftDocument) {
                return RightDocument;
            }
            else if(docHost == RightDocument) {
                return LeftDocument;
            }

            throw new InvalidOperationException("Check IsDiffDocument first");
        }

        public void UpdateResults(DiffMarkingResult leftResults, IRTextSection leftSection,
                                  DiffMarkingResult rightResults, IRTextSection rightSection) {
            LeftDiffResults = leftResults;
            LeftSection = leftSection;
            RightDiffResults = rightResults;
            RightSection = rightSection;
        }
    }
}
