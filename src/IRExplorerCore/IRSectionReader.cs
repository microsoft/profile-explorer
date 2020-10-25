// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace IRExplorerCore {
    public class IRTextSummary {
        private Dictionary<string, IRTextFunction> functionMap_;
        private ulong nextSectionId_;
        private Dictionary<ulong, IRTextSection> sectionMap_;

        public List<IRTextFunction> Functions { get; set; }

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
            return sectionMap_.TryGetValue(id, out var value) ? value : null;
        }

        public IRTextFunction FindFunction(string name) {
            return functionMap_.TryGetValue(name, out var result) ? result : null;
        }

        public List<IRTextFunction> FindAllFunctions(string nameSubstring) {
            return Functions.FindAll((func) => func.Name.Contains(nameSubstring, StringComparison.Ordinal));
        }

        public IRTextFunction FindFunction(IRTextFunction function) {
            return functionMap_.TryGetValue(function.Name, out var result) ? result : null;
        }

        //? TODO: Compute for each section the SHA256 signature
        //? to speed up diffs and other tasks that would check the text for equality.
        public void ComputeSectionSignatures() {

        }
    }

    public class IRTextFunction {
        public List<IRTextSection> Sections { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        public IRTextSummary ParentSummary { get; set; }
        public int SectionCount => Sections != null ? Sections.Count : 0;

        public IRTextFunction(string name) {
            Name = name;
            Sections = new List<IRTextSection>();
        }

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

        public IRTextSection FindSection(string name) {
            return Sections.Find(item => item.Name == name);
        }

        public List<IRTextSection> FindAllSections(string nameSubstring) {
            return Sections.FindAll((section) => section.Name.Contains(nameSubstring));
        }
    }

    public class IRPassOutput {
        public IRPassOutput(long dataStartOffset, long dataEndOffset, int startLine,
                            int endLine) {
            DataStartOffset = dataStartOffset;
            DataEndOffset = dataEndOffset;
            StartLine = startLine;
            EndLine = endLine;
        }

        public long DataStartOffset { get; set; }
        public long DataEndOffset { get; set; }
        public long Size => DataEndOffset - DataStartOffset + 1;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int LineCount => EndLine - StartLine + 1;
        public byte[] Signature { get; set; } // SHA256 signature of the text.
        public bool HasPreprocessedLines { get; set; }

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
        private CompressedObject<Dictionary<int, string>> lineMetadata_;

        public IRTextSection() { }

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
        public IRTextFunction ParentFunction { get; set; }
        public int LineCount => Output.EndLine - Output.StartLine + 1;
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

        public override bool Equals(object obj) {
            return obj is IRTextSection section &&
                   Id == section.Id &&
                   Number == section.Number &&
                   Name == section.Name;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Name, Id, Number);
        }

        public override string ToString() {
            return $"({Number}) {Name}";
        }
    }

    public class SectionReaderProgressInfo {
        public SectionReaderProgressInfo(long bytesProcessed, long totalBytes) {
            BytesProcessed = bytesProcessed;
            TotalBytes = totalBytes;
        }

        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
    }

    public delegate void ProgressInfoHandler(IRSectionReader reader, SectionReaderProgressInfo info);

    public interface IRSectionReader {
        IRTextSummary GenerateSummary(ProgressInfoHandler progressHandler);
        string GetSectionText(IRTextSection section);
        List<string> GetSectionTextLines(IRTextSection section);
        string GetPassOutputText(IRPassOutput output);
        List<string> GetPassOutputTextLines(IRPassOutput output);
        string GetRawSectionText(IRTextSection section);
        string GetRawPassOutputText(IRPassOutput output);
        public byte[] GetDocumentTextData();
        void Dispose();
    }
}
