// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public class InlineeSourceLocation {
        public InlineeSourceLocation(string function, string filePath, int line, int column) {
            Function = function;
            FilePath = filePath;
            Line = line;
            Column = column;
        }

        public string Function { get; set; }
        public string FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public class SourceLocationTag : ITag {
        public SourceLocationTag() { }
        
        public SourceLocationTag(int line, int column) {
            Line = line;
            Column = column;
        }
        
        public List<InlineeSourceLocation> Inlinees { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public bool HasInlinees => Inlinees != null && Inlinees.Count > 0;

        public void AddInlinee(string function, string filePath, int line, int column) {
            Inlinees ??= new List<InlineeSourceLocation>();
            Inlinees.Add(new InlineeSourceLocation(function, filePath, line, column));
        }

        public string Name => "Source location";
        public TaggedObject Owner { get; set; }

        public override bool Equals(object obj) {
            return obj is SourceLocationTag tag && Line == tag.Line && Column == tag.Column;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Line, Column);
        }

        public override string ToString() {
            var builder = new StringBuilder();
            builder.Append($"source location: {Line};{Column}");

            if (Inlinees != null) {
                builder.AppendLine($"\n  inlinees: {Inlinees.Count}");

                foreach (var item in Inlinees) {
                    builder.AppendLine($"    {item.Line};{item.Column}: {item.Function}");
                }
            }

            return builder.ToString();
        }
    }
}
