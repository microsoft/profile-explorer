// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Core {
    public class IRTextSummary {
        private Dictionary<string, IRTextFunction> functionMap_;
        private Dictionary<ulong, IRTextSection> sectionMap_;
        private ulong nextSectionId_;

        public List<IRTextFunction> Functions;

        public IRTextSummary() {
            Functions = new List<IRTextFunction>();
            functionMap_ = new Dictionary<string, IRTextFunction>();
            sectionMap_ = new Dictionary<ulong, IRTextSection>();
            nextSectionId_ = 1;
        }

        public void AddFunction(IRTextFunction function) {
            Functions.Add(function);
            functionMap_.Add(function.Name, function);
            function.ParentSummary = this;
        }

        public void AddSection(IRTextSection section) {
            sectionMap_.Add(nextSectionId_, section);
            section.Id = nextSectionId_;
            nextSectionId_++;
        }

        public IRTextSection GetSectionWithId(ulong id) {
            if (sectionMap_.TryGetValue(id, out var value)) {
                return value;
            }

            return null;
        }

        public IRTextFunction FindFunction(string name) {
            if (functionMap_.TryGetValue(name, out var result)) {
                return result;
            }

            return null;
        }

        public IRTextFunction FindFunction(IRTextFunction function) {
            if (functionMap_.TryGetValue(function.Name, out var result)) {
                return result;
            }

            return null;
        }
    }

    public class IRTextFunction {
        public int Number { get; set; }
        public string Name { get; set; }
        public IRTextSummary ParentSummary { get; set; }
        public List<IRTextSection> Sections;
        public int SectionCount => Sections != null ? Sections.Count : 0;

        public int MaxBlockCount {
            get {
                IRTextSection maxSection = null;

                foreach (var section in Sections) {
                    if (maxSection == null) {
                        maxSection = section;
                    }
                    else if (section.BlockCount > maxSection.BlockCount) {
                        maxSection = section;
                    }
                }

                return maxSection?.BlockCount ?? 0;
            }
        }

        public IRTextFunction(string name) {
            Name = name;
            Sections = new List<IRTextSection>();
        }

        public IRTextSection FindSection(string name) {
            return Sections.Find((item) => item.Name == name);
        }
    }

    public class IRPassOutput {
        public IRPassOutput(long dataStartOffset, long dataEndOffset,
                            int startLine, int endLine) {
            DataStartOffset = dataStartOffset;
            DataEndOffset = dataEndOffset;
            StartLine = startLine;
            EndLine = endLine;
        }

        public long DataStartOffset { get; set; }
        public long DataEndOffset { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }

        public override bool Equals(object obj) {
            return obj is IRPassOutput output &&
                   DataStartOffset == output.DataStartOffset &&
                   DataEndOffset == output.DataEndOffset &&
                   StartLine == output.StartLine &&
                   EndLine == output.EndLine;
        }

        public override int GetHashCode() {
            return HashCode.Combine(DataStartOffset, DataEndOffset);
        }
    }

    public class IRTextSection {
        public IRTextSection() {

        }

        public IRTextSection(IRTextFunction parent, ulong id, int number, string name,
                             IRPassOutput sectionOutput, int blocks = 0) {
            ParentFunction = parent;
            Id = id;
            Number = number;
            Name = name;
            Output = sectionOutput;
            BlockCount = blocks;
        }

        public ulong Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        public int BlockCount { get; set; }
        public IRPassOutput Output;
        public IRTextFunction ParentFunction { get; set; }
        public IRPassOutput OutputBefore;
        public IRPassOutput OutputAfter;

        public int LineCount {
            get {
                return Output.EndLine - Output.StartLine + 1;
            }
        }

        public override bool Equals(object obj) {
            return obj is IRTextSection section &&
                   Name == section.Name &&
                   Output == section.Output;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Name, Output.StartLine, Output.EndLine);
        }

        public override string ToString() {
            return $"({Number}) {Name}";
        }
    }

    public interface IRSectionReader {
        IRTextSummary GenerateSummary();
        string GetSectionText(IRTextSection section);
        string GetPassOutputText(IRPassOutput output);
    }
}
