// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.Utilities;

namespace IRExplorerUI {
    public class RemarkContext {
        public RemarkContext(string id, string name, RemarkContext parent = null) {
            Id = id;
            Name = name;
            Parent = parent;
            Remarks = new List<Remark>();
            Children = new List<RemarkContext>();
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public List<Remark> Remarks { get; set; }
        public RemarkContext Parent { get; set; }
        public List<RemarkContext> Children { get; set; }
        public int ContextTreeLevel => Parent != null ? Parent.ContextTreeLevel + 1 : 0;

        public override string ToString() {
            var builder = new StringBuilder();
            builder.AppendLine($"remark context name: {Name}, id: {Id}, has parent: {Parent != null}");
            builder.AppendLine($"> {Remarks.Count} remarks:");

            foreach (var remark in Remarks) {
                builder.AppendLine($"  o \"{remark.RemarkText}\"".Indent(4));
            }

            builder.AppendLine($"> {Children.Count} children:");

            foreach (var child in Children) {
                builder.AppendLine($"  o {child}".Indent(4));
            }

            return builder.ToString();
        }
    }

    public class Remark {
        public Remark(RemarkCategory category, IRTextSection section, 
                      string remarkText, string remarkLine,
                      TextLocation remarkLocation) {
            Category = category;
            Section = section;
            RemarkText = remarkText;
            RemarkLine = remarkLine;
            RemarkLocation = remarkLocation;
            ReferencedElements = new List<IRElement>(1);
            OutputElements = new List<IRElement>(1);
        }

        public RemarkCategory Category { get; set; }
        public RemarkKind Kind => Category.Kind;
        public IRTextSection Section { get; set; }
        public string RemarkText { get; set; }
        public string RemarkLine { get; set; }
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
            var text = $"remark kind: {Kind}, text: \"{RemarkText}\", section: \"{Section}\"";

            if (Context != null) {
                text += $"\n  {Context.ToString().Indent(2)}";
            }

            return text;
        }
    }
}
