// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Core;
using System;
using System.Collections.Generic;
using System.Text;
using Core.IR;
using Core.Analysis;
using Core.UTC;
using Humanizer;

namespace Client {
    public class UTCRemarkParser
    {
        public static string ExtractVN(IRElement element)
        {
            var tag = element.GetTag<RemarkTag>();

            if (tag == null)
            {
                return null;
            }

            foreach (var remark in tag.Remarks)
            {
                if (remark.RemarkText.StartsWith("VN "))
                {
                    var tokens = remark.RemarkText.Split(new char[] { ' ', ':' });
                    var number = tokens[1];
                    return number;
                }
            }

            return null;
        }
    }

    public class UTCRemarkProvider : IRRemarkProvider {
        public List<PassRemark> ExtractRemarks(string text, FunctionIR function, IRTextSection section) {
            var (fakeTuple, fakeBlock) = CreateFakeIRElements();
            var remarks = new List<PassRemark>();

            ExtractInstructionRemarks(text, function, section, fakeBlock, remarks);
            ExtractOperandRemarks(text, function, section, fakeTuple, remarks);
            return remarks;
        }

        void ExtractInstructionRemarks(string text, FunctionIR function, IRTextSection section,
                                       BlockIR fakeBlock, List<PassRemark> remarks) {
            var lines = text.Split(new[] { '\r', '\n' });
            var similarValueFinder = new SimilarValueFinder(function);
            int lineStartOffset = 0;
            int emptyLines = 0;

            for (int i = 0; i < lines.Length; i++) {
                int index = 0;
                var line = lines[i];

                if (line.Length == 0) {
                    lineStartOffset++;
                    emptyLines++;
                    continue;
                }

                while (index < line.Length) {
                    // Find next chunk delimited by whitespace.
                    if (index > 0) {
                        int next = line.IndexOf(' ', index);

                        if (next != -1) {
                            index = next + 1;
                        }
                    }

                    // Skip all whitespace.
                    while (index < line.Length && char.IsWhiteSpace(line[index])) {
                        index++;
                    }

                    if (index == line.Length) {
                        break;
                    }

                    var lineChunk = line.Substring(index);
                    var lineParser = new UTCParser(lineChunk, null, null);
                    var tuple = lineParser.ParseTuple(fakeBlock);

                    if (tuple is InstructionIR instr) {
                        var similarInstr = similarValueFinder.Find(instr);

                        if (similarInstr != null) {
                            var remarkLocation = new TextLocation(lineStartOffset, i - emptyLines, 0);
                            var location = new TextLocation(instr.TextLocation.Offset + index + lineStartOffset,
                                                            i - emptyLines, 0);
                            instr.TextLocation = location; // Set actual location in output text.
                            var remark = new PassRemark(FindRemarkKind(line), section, line.Trim(), remarkLocation);
                            remark.ReferencedElements.Add(similarInstr);
                            remark.OutputElements.Add(instr);
                            remarks.Add(remark);

                            index += instr.TextLength;
                            continue;
                        }
                    }

                    index++;
                }

                lineStartOffset += line.Length + 1;
            }
        }

        void ExtractOperandRemarks(string text, FunctionIR function, IRTextSection section,
                                   TupleIR fakeTuple, List<PassRemark> remarks) {
            var lines = text.Split(new[] { '\r', '\n' });
            var lineOffsets = new int[lines.Length];
            var lineMapping = new int[lines.Length];
            int offset = 0;
            int emptyLines = 0;

            for(int i = 0; i < lines.Length; i++)
            {
                var actualLine = i - emptyLines;
                lineMapping[actualLine] = i;

                if(lines[i].Length == 0)
                {
                    emptyLines++;
                }

                lineOffsets[i] = offset;
                offset += lines[i].Length + 1;
            }


            var refFinder = new ReferenceFinder(function);
            var parser = new UTCParser(text, null, null);

            while (!parser.IsDone()) {
                var op = parser.ParseOperand(fakeTuple, isIndirBaseOp: false,
                                             isBlockLabelRef: false, disableSkipToNext: true);

                if (op != null) {
                    var value = refFinder.FindEquivalentValue(op, onlySSA: true);

                    if (value != null) {
                        var parentInstr = value.ParentInstruction;

                        if (op.TextLocation.Line < lines.Length) {
                            var line = lineMapping[op.TextLocation.Line];
                            var lineText = lines[line];
                            var lineOffset = lineOffsets[line];
                            var location = new TextLocation(lineOffset, op.TextLocation.Line, 0);


                            var remark = new PassRemark(FindRemarkKind(lineText), section, lineText.Trim(), location);
                            remark.ReferencedElements.Add(value);
                            remark.OutputElements.Add(op);
                            remarks.Add(remark);
                        }
                    }
                }
                else {
                    parser.SkipCurrentToken();
                }
            }
        }

        public List<IRTextSection> GetSectionList(IRTextSection currentSection) {
            var list = new List<IRTextSection>();
            var function = currentSection.ParentFunction;

            //? Example for SSAOpt sections
            //?  - consider only other SSaopt sections
            //?  - if in second pass, stop at section that separates first/second

            for (int i = currentSection.Number - 1, count = 0; i >= 0 && count < 5; i--, count++) {
                list.Add(function.Sections[i]);
            }

            list.Reverse();
            return list;
        }

        public List<PassRemark> ExtractAllRemarks(List<IRTextSection> sections, FunctionIR function, LoadedDocument document) {
            var remarks = new List<PassRemark>();

            foreach (var section in sections) {
                var sectionText = document.Loader.LoadSectionPassOutput(section.OutputBefore);
                var sectionRemarks = ExtractRemarks(sectionText, function, section);
                remarks.AddRange(sectionRemarks);
            }

            return remarks;
        }

        public OptimizationRemark GetOptimizationRemarkInfo(PassRemark remark) {
            return null;
        }

        private (TupleIR, BlockIR) CreateFakeIRElements() {
            var func = new FunctionIR();
            var block = new BlockIR(IRElementId.FromLong(0), 0, func);
            var tuple = new TupleIR(IRElementId.FromLong(1), TupleKind.Other, block);
            return (tuple, block);
        }

        //? Have option to get style used to display remarks, like [V] in a diff color,
        // which parts are bold?

        public RemarkKind FindRemarkKind(string text) {
            text = text.Trim();

            if (text.StartsWith("[") && text.Contains("PEEP"))
            {
                return RemarkKind.Optimization;
            }
            else if (text.Contains("CSE of instr"))
            {
                return RemarkKind.Optimization;
            }
            else if (text.StartsWith("[V]")) {
                return RemarkKind.Verbose;
            }
            else if (text.StartsWith("[T]")) {
                return RemarkKind.Trace;
            }
            else if (text.StartsWith("VN ")) {
                return RemarkKind.Analysis;
            }

            return RemarkKind.Default;
        }
    }
}
