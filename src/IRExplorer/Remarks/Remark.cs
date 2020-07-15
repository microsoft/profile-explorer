// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorer {
    [Flags]
    public enum RemarkKind {
        Default,
        Verbose,
        Trace,
        Warning,
        Error,
        Optimization,
        Analysis
    }

    public class RemarkCategory {
        public RemarkKind Kind { get; set; }
        public string Title { get; set; } // VN,CE/BE/ALIAS, etc
        public bool HasTitle => !string.IsNullOrEmpty(Title);
        public string SearchedText { get; set; }
        public TextSearchKind SearchKind { get; set; }
        public bool AddTextMark { get; set; }
        public Color TextMarkBorderColor { get; set; }
        public double TextMarkBorderWeight { get; set; }
        public bool AddLeftMarginMark { get; set; }
        public Color MarkColor { get; set; }
        public int MarkIconIndex { get; set; }
    }

    public class RemarkSectionBoundary {
        public string SearchedText { get; set; }
        public TextSearchKind SearchKind { get; set; }
    }

    public class RemarkContext {
        public RemarkContext(string name, RemarkContext parent = null) {
            Name = name;
            Parent = parent;
            Remarks = new List<Remark>();
            Children = new List<RemarkContext>();
        }

        public string Name { get; set; }
        public List<Remark> Remarks { get; set; }
        public RemarkContext Parent { get; set; }
        public List<RemarkContext> Children { get; set; }
    }

    public class Remark {
        public Remark(RemarkCategory category, IRTextSection section, string remarkText,
                          TextLocation remarkLocation) {
            Category = category;
            Section = section;
            RemarkText = remarkText;
            RemarkLocation = remarkLocation;
            ReferencedElements = new List<IRElement>(1);
            OutputElements = new List<IRElement>(1);
        }

        public RemarkCategory Category { get; set; }
        public RemarkKind Kind => Category.Kind;
        public IRTextSection Section { get; set; }
        public string RemarkText { get; set; }
        public TextLocation RemarkLocation { get; set; }
        public List<IRElement> ReferencedElements { get; set; }
        public List<IRElement> OutputElements { get; set; }
        public RemarkContext Context;

        public int Priority => Kind switch
        {
            RemarkKind.Default => 2,
            RemarkKind.Verbose => 3,
            RemarkKind.Trace => 4,
            RemarkKind.Warning => 5,
            RemarkKind.Error => 6,
            RemarkKind.Optimization => 0, // 0 is most important
            RemarkKind.Analysis => 1,
            _ => throw new ArgumentOutOfRangeException()
        };

        public override string ToString() {
            return $"kind: {Kind}, text: {RemarkText}, section: {Section}";
        }
    }

    public class RemarkLineGroup {
        public RemarkLineGroup(int line) {
            LineNumber = line;
            Remarks = new List<Remark>();
        }

        public RemarkLineGroup(int line, Remark remark) : this(line) {
            Add(remark, null);
        }

        public int LineNumber { get; set; }
        public List<Remark> Remarks { get; set; }
        public Remark LeaderRemark { get; set; }

        public void Add(Remark remark, IRTextSection currentSection) {
            // Don't add multiple remarks referencing the same output text location,
            // can happen when both an instruction and its operands are marked.
            if (Remarks.Find((item) => item.RemarkLocation == remark.RemarkLocation) != null) {
                return;
            }

            Remarks.Add(remark);

            if (LeaderRemark == null || remark.Priority < LeaderRemark.Priority) {
                LeaderRemark = remark;
            }
            else if (remark.Priority == LeaderRemark.Priority &&
                     remark.Section == currentSection) {
                LeaderRemark = remark;
            }
        }
    }
}
