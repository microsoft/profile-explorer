// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using Core.IR;
using Core;

namespace Client
{
    public enum RemarkKind
    {
        Default,
        Verbose,
        Trace,
        Warning,
        Error,
        Optimization,
        Analysis
    }

    //? [V] Unsafe threading over loop PHI:  [IsSafeToOptimizeOperationOnPhi:575]
    //? Showing the UTC source code mentioned could be useful

    public class PassRemark
    {
        public PassRemark(RemarkKind kind, IRTextSection section, 
                          string remarkText, TextLocation remarkLocation)
        {
            Kind = kind;
            Section = section;
            RemarkText = remarkText;
            RemarkLocation = remarkLocation;
            ReferencedElements = new List<IRElement>(1);
            OutputElements = new List<IRElement>(1);
        }

        public RemarkKind Kind { get; set; }
        public IRTextSection Section { get; set; }
        public string RemarkText { get; set; }
        public TextLocation RemarkLocation { get; set; }
        public List<IRElement> ReferencedElements { get; set; }
        public List<IRElement> OutputElements { get; set; }

        public override string ToString()
        {
            return $"kind: {Kind}, text: {RemarkText}, section: {Section}";
        }
    }

    public class OptimizationRemark //: PassRemark
    {
        public string OptimizationName { get; set; }
        public object Info { get; set; }
    }

    public interface IRRemarkProvider
    {
        // per-section cache of remarks, don't have to re-parse all output
        // when switching sections

        //? Should be async

        public List<PassRemark> ExtractRemarks(string text, FunctionIR function, IRTextSection section);
        public OptimizationRemark GetOptimizationRemarkInfo(PassRemark remark);
    }
}
