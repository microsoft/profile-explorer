// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IRExplorerCore {
    public class IRTextSection {
        private CompressedObject<Dictionary<int, string>> lineMetadata_;

        public IRTextSection() { }

        public IRTextSection(IRTextFunction parent, string name,
                             IRPassOutput sectionOutput, int blocks = 0) {
            ParentFunction = parent;
            Name = name;
            Output = sectionOutput;
            BlockCount = blocks;
        }

        public int Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        public int BlockCount { get; set; }
        public IRTextFunction ParentFunction { get; set; }
        public int LineCount => Output.LineCount;
        public IRPassOutput Output { get; set; }
        public IRPassOutput OutputAfter { get; set; }
        public IRPassOutput OutputBefore { get; set; }

        public Dictionary<int, string> LineMetadata {
            get => lineMetadata_?.GetValue();
            set => lineMetadata_ = new CompressedObject<Dictionary<int, string>>(value);
        }

        //? TODO: Metadata is large and multiplies N times with multiple versions of the same function,
        //? while most lines are likely identical. Find a way to share identical lines, or at least
        //? have one big string that can be compressed.
        public void AddLineMetadata(int lineNumber, string metadata) {
            LineMetadata ??= new Dictionary<int, string>();
            LineMetadata[lineNumber] = metadata;
        }

        public string GetLineMetadata(int lineNumber) {
            if (LineMetadata != null &&
                LineMetadata.TryGetValue(lineNumber, out string value)) {
                return value;
            }

            return null;
        }

        public void CompressLineMetadata() {
            if (lineMetadata_ != null) {
                lineMetadata_.Compress();
            }
        }

        public bool IsSectionTextDifferent(IRTextSection other) {
            // If there is a signature, assume that same signature means same text.
            if (Output?.Signature != null &&
                other?.Output.Signature != null) {
                return !Output.Signature.AsSpan().SequenceEqual(other.Output.Signature.AsSpan());
            }

            return true;
        }

        public override bool Equals(object obj) {
            return obj is IRTextSection section &&
                   Id == section.Id &&
                   Number == section.Number &&
                   Name == section.Name;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Id, Number);
        }

        public override string ToString() {
            return $"({Number}) {Name}";
        }
    }
}