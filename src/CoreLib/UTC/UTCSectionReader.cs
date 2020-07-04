// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace IRExplorerCore.UTC {
    public sealed class UTCSectionReader : IRSectionReader, IDisposable {
        private const string SEPARATOR_STYLE1 =
            "# # # # # # # # # # # # # # # # # # # # # # # # # # # # # #";
        private const string SEPARATOR_STYLE2 =
            "***********************************************************";
        public static readonly string SECTION_START = "*********************";
        private static readonly char[] WHITESPACE_CHARS = {' ', '\t'};
        private static readonly int FILE_BUFFER_SIZE = 512 * 1024;
        private static readonly int STREAM_BUFFER_SIZE = 16 * 1024;
        public static readonly long MAX_PRELOADED_FILE_SIZE = 500 * 1024 * 1024;
        private static readonly int MAX_LINE_LENGTH = 200;
        private StreamReader dataReader_;

        private Stream dataStream_;
        private long dataStreamSize_;
        private bool expectSectionHeaders_;

        private Dictionary<string, IRTextFunction> functionMap_;
        private int lineIndex_;
        private IRPassOutput optionalOutput_;
        private bool optionalOutputNeeded_;
        private byte[] preloadedData_;
        private MemoryStream preloadedDataStream_;
        private int prevLineCount_;
        private string[] prevLines_;
        private IRTextSummary summary_;

        public UTCSectionReader(string filePath, bool expectSectionHeaders = true) {
            expectSectionHeaders_ = expectSectionHeaders;
            dataStreamSize_ = new FileInfo(filePath).Length;

            if (dataStreamSize_ < MAX_PRELOADED_FILE_SIZE) {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read,
                                             FileShare.ReadWrite);

                preloadedData_ = new byte[dataStreamSize_];
                stream.Read(preloadedData_, 0, (int)dataStreamSize_);
                preloadedDataStream_ = new MemoryStream(preloadedData_, true);
                dataStream_ = preloadedDataStream_;
            }
            else {
                dataStream_ = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                             FileShare.Read, FILE_BUFFER_SIZE,
                                             FileOptions.SequentialScan);
            }

            Initialize();
        }

        public UTCSectionReader(byte[] textData, bool expectSectionHeaders = true) {
            expectSectionHeaders_ = expectSectionHeaders;
            dataStream_ = new MemoryStream(textData);
            dataStreamSize_ = textData.Length;
            Initialize();
        }

        public IRTextSummary GenerateSummary(ProgressInfoHandler progressHandler) {
            var summary = GenerateSummaryImpl(progressHandler);

            if (summary.Functions.Count == 0 && expectSectionHeaders_) {
                // Try parsing again, but without looking for section headers.
                // Useful when handling a single section copy-pasted from somewhere.
                expectSectionHeaders_ = false;
                dataStream_.Seek(0, SeekOrigin.Begin);
                dataReader_.DiscardBufferedData();
                summary = GenerateSummaryImpl(progressHandler);
            }

            return summary;
        }

        public string GetSectionText(IRTextSection section) {
            if (section.Output == null) {
                throw new NullReferenceException();
            }

            return GetPassOutputText(section.Output, false);
        }

        public string GetPassOutputText(IRPassOutput output) {
            return GetPassOutputText(output, true);
        }

        private void Initialize() {
            dataReader_ = new StreamReader(dataStream_, Encoding.Default,
                                           true, STREAM_BUFFER_SIZE);

            prevLines_ = new string[3];
            summary_ = new IRTextSummary();
            functionMap_ = new Dictionary<string, IRTextFunction>();
        }

        public byte[] GetDocumentTextData() {
            lock (this) {
                dataStream_.Seek(0, SeekOrigin.Begin);
                using var binaryReader = new BinaryReader(dataStream_, Encoding.Default, true);
                return binaryReader.ReadBytes((int) dataStream_.Length);
            }
        }

        private IRTextSummary GenerateSummaryImpl(ProgressInfoHandler progressHandler) {
            IRTextSection previousSection = null;
            (var section, string functionName) = FindNextSection();

            while (section != null) {
                var textFunc = GetOrCreateFunction(functionName);
                textFunc.Sections.Add(section);
                summary_.AddSection(section);
                section.ParentFunction = textFunc;
                section.Number = textFunc.Sections.Count;

                if (previousSection != null) {
                    previousSection.OutputAfter = GetAdditionalOutput();
                }

                section.OutputBefore = GetAdditionalOutput();
                ResetAdditionalOutput();
                previousSection = section;
                (section, functionName) = FindNextSection();

                if(progressHandler != null) {
                    var info = new SectionReaderProgressInfo(TextOffset(), dataStreamSize_);
                    progressHandler(this, info);
                }
            }

            if (previousSection != null) {
                previousSection.OutputAfter = GetAdditionalOutput();
            }

            return summary_;
        }

        private IRTextFunction GetOrCreateFunction(string name) {
            if (functionMap_.TryGetValue(name, out var textFunc)) {
                return textFunc;
            }

            textFunc = new IRTextFunction(name) {Number = summary_.Functions.Count};
            summary_.AddFunction(textFunc);
            functionMap_.Add(name, textFunc);
            return textFunc;
        }

        private string GetPassOutputText(IRPassOutput output, bool isOptionalOutput) {
            if (output == null) {
                return "";
            }

            // If the file was preloaded in memory, create a new stream 
            // to allow parallel loading of text.
            if (preloadedData_ != null) {
                var reader = new MemoryStream(preloadedData_, true);

                var streamReader = new StreamReader(reader, Encoding.ASCII,
                                                    true, STREAM_BUFFER_SIZE);

                return ReadPassOutputText(streamReader, output, isOptionalOutput);
            }

            lock (this) {
                return ReadPassOutputText(dataReader_, output, isOptionalOutput);
            }
        }

        private string ReadPassOutputText(StreamReader reader, IRPassOutput output,
                                          bool isOptionalOutput) {
            long size = output.DataEndOffset - output.DataStartOffset + 1;
            var builder = new StringBuilder((int) size);
            reader.BaseStream.Position = output.DataStartOffset;
            reader.DiscardBufferedData();

            for (int i = output.StartLine; i <= output.EndLine; i++) {
                string line = NextLine(reader, false);

                if (isOptionalOutput) {
                    if (string.IsNullOrWhiteSpace(line)) {
                        continue;
                    }
                    else if (line.StartsWith(SECTION_START)) {
                        continue;
                    }
                }

                if (line.Length > MAX_LINE_LENGTH) {
                    line = line.Substring(0, MAX_LINE_LENGTH);
                }

                builder.AppendLine(line);
            }

            return builder.ToString();
        }

        public static string ExtractFunctionName(string line) {
            var parts = line.Split(WHITESPACE_CHARS, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[1].Trim() : string.Empty;
        }

        private string ExtractSectionName() {
            string sectionName = PreviousLine(1);

            if (sectionName == null) {
                return string.Empty;
            }

            if (sectionName.StartsWith(SEPARATOR_STYLE1, StringComparison.Ordinal) ||
                sectionName.StartsWith(SEPARATOR_STYLE2, StringComparison.Ordinal)) {
                sectionName = PreviousLine(2);
            }

            return sectionName != null ? sectionName.Trim() : string.Empty;
        }

        private void AddOptionalOutputLine(string line, long initialOffset) {
            if (optionalOutput_ == null) {
                // Start a new optional section.
                long offset = TextOffset();

                optionalOutput_ =
                    new IRPassOutput(initialOffset, offset, lineIndex_, lineIndex_);

                optionalOutputNeeded_ = false;
            }

            optionalOutput_.DataEndOffset = TextOffset();
            optionalOutput_.EndLine = lineIndex_;

            if (!string.IsNullOrWhiteSpace(line)) {
                optionalOutputNeeded_ = true;
            }
        }

        private IRPassOutput GetAdditionalOutput() {
            if (optionalOutput_ != null && optionalOutputNeeded_) {
                return optionalOutput_;
            }

            return null;
        }

        private void ResetAdditionalOutput() {
            optionalOutput_ = null;
        }

        private (IRTextSection, string) FindNextSection() {
            prevLineCount_ = 0;

            while (true) {
                long initialOffset = TextOffset();
                string line = NextLine();

                if (line == null) {
                    break;
                }

                // Each section is expected to start with a name,
                // followed by an ASCII "line", which is searched for here,
                // unless the client indicates that the name may be missing.
                bool hasName = true;

                if (!line.Equals(SECTION_START, StringComparison.Ordinal)) {
                    if (!expectSectionHeaders_ &&
                        line.StartsWith("BLOCK", StringComparison.Ordinal)) {
                        hasName = false;
                    }
                    else {
                        // Skip over line.
                        AddOptionalOutputLine(line, initialOffset);
                        lineIndex_++;
                        continue;
                    }
                }

                // Go back and find the name of the section.
                int sectionStartLine = lineIndex_ + (hasName ? 1 : 0);
                string sectionName = hasName ? ExtractSectionName() : string.Empty;

                // Find the end of the section and extract the function name.
                long startOffset = hasName ? TextOffset() : initialOffset;
                long endOffset = startOffset;
                string funcName = string.Empty;
                int sectionEndLine = 0;
                int blockCount = 0;
                bool exitBlockRequired = true;

                while (true) {
                    line = NextLine();

                    if (line == null) {
                        break;
                    }

                    if (line.StartsWith("BLOCK", StringComparison.Ordinal)) {
                        if (PreviousLine(1).StartsWith("EXIT", StringComparison.Ordinal)) {
                            // Found function end.
                            endOffset = TextOffset();
                            sectionEndLine = lineIndex_ + 1;
                            break;
                        }
                        else {
                            blockCount++;
                        }
                    }
                    else if (line.StartsWith("ENTRY", StringComparison.Ordinal)) {
                        // Extract function name.
                        if (string.IsNullOrEmpty(funcName)) {
                            funcName = ExtractFunctionName(line);
                        }
                    }
                    else if (line.StartsWith("EXIT", StringComparison.Ordinal)) {
                        if (!exitBlockRequired) {
                            // Found function end.
                            endOffset = TextOffset();
                            sectionEndLine = lineIndex_ + 1;
                            break;
                        }
                    }
                    else if (line.StartsWith("=== Block", StringComparison.Ordinal)) {
                        //? TODO: See similar issue in UTCParser. 
                        //? UTC tuple dumps should be changed.
                        exitBlockRequired = false;
                        blockCount++;
                    }

                    lineIndex_++;
                }

                sectionEndLine = Math.Max(sectionEndLine, sectionStartLine);
                int lines = sectionEndLine - sectionStartLine;

                // Ignore empty sections.
                if (lines == 0) {
                    lineIndex_++;
                    continue;
                }

                var output = new IRPassOutput(startOffset, endOffset,
                                              sectionStartLine, sectionEndLine);

                var section = new IRTextSection(null, 0, 0, sectionName, output, blockCount);
                return (section, funcName);
            }

            return (null, null);
        }

        private string NextLine() {
            return NextLine(dataReader_, true);
        }

        private string NextLine(StreamReader reader, bool recordPreviousLines) {
            if (!reader.EndOfStream) {
                string line = reader.ReadLine();

                // Sometimes a line starts with a number followed by > like
                // 32>actual line text
                if (!string.IsNullOrEmpty(line) && char.IsDigit(line[0])) {
                    for (int i = 1; i < line.Length; i++) {
                        if (line[i] == '>') {
                            line = line.Substring(i + 1);
                            break;
                        }
                        else if (!char.IsDigit(line[i])) {
                            break;
                        }
                    }
                }

                if (recordPreviousLines) {
                    RecordPreviousLine(line);
                }

                return line;
            }

            return null;
        }

        private string PreviousLine(int offset) {
            return offset < prevLineCount_ ? prevLines_[offset] : null;
        }

        private void RecordPreviousLine(string line) {
            for (int i = prevLines_.Length - 1; i >= 1; i--) {
                prevLines_[i] = prevLines_[i - 1];
            }

            prevLines_[0] = line;
            prevLineCount_++;
        }

        private long TextOffset() {
            return TextOffset(dataReader_);
        }

        private long TextOffset(StreamReader reader) {
            //? TODO: This is a hack needed to get the proper offset in the stream.
            // https://stackoverflow.com/questions/5404267/streamreader-and-seeking
            var flags = BindingFlags.DeclaredOnly |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance |
                        BindingFlags.GetField;

            // The current buffer of decoded characters
            var charBuffer = (char[]) reader
                                      .GetType()
                                      .InvokeMember("_charBuffer", flags, null, reader, null);

            // The index of the next char to be read from charBuffer
            int charPos =
                (int) reader.GetType().InvokeMember("_charPos", flags, null, reader, null);

            // The number of decoded chars presently used in charBuffer
            int charLen =
                (int) reader.GetType().InvokeMember("_charLen", flags, null, reader, null);

            // The current buffer of read bytes (byteBuffer.Length = 1024; this is critical).
            var byteBuffer = (byte[]) reader
                                      .GetType()
                                      .InvokeMember("_byteBuffer", flags, null, reader, null);

            // The number of bytes read while advancing reader.BaseStream.Position to (re)fill charBuffer
            int byteLen =
                (int) reader.GetType().InvokeMember("_byteLen", flags, null, reader, null);

            // The number of bytes the remaining chars use in the original encoding.
            int numBytesLeft =
                reader.CurrentEncoding.GetByteCount(charBuffer, charPos, charLen - charPos);

            // For variable-byte encodings, deal with partial chars at the end of the buffer
            int numFragments = 0;

            if (byteLen > 0 && !reader.CurrentEncoding.IsSingleByte) {
                if (reader.CurrentEncoding.CodePage == 65001) { // UTF-8 
                    byte byteCountMask = 0;

                    while (byteBuffer[byteLen - numFragments - 1] >> 6 == 2
                    ) // if the byte is "10xx xxxx", it's a continuation-byte
                    {
                        byteCountMask |=
                            (byte) (1 <<
                                    ++numFragments
                            ); // count bytes & build the "complete char" mask
                    }

                    if (byteBuffer[byteLen - numFragments - 1] >> 6 == 3
                    ) // if the byte is "11xx xxxx", it starts a multi-byte char.
                    {
                        byteCountMask |=
                            (byte) (1 <<
                                    ++numFragments
                            ); // count bytes & build the "complete char" mask
                    }

                    // see if we found as many bytes as the leading-byte says to expect
                    if (numFragments > 1 &&
                        byteBuffer[byteLen - numFragments] >> (7 - numFragments) ==
                        byteCountMask) {
                        numFragments = 0; // no partial-char in the byte-buffer to account for
                    }
                }
                else if (reader.CurrentEncoding.CodePage == 1200) { // UTF-16LE
                    if (byteBuffer[byteLen - 1] >= 0xd8)            // high-surrogate
                    {
                        numFragments = 2; // account for the partial character
                    }
                }
                else if (reader.CurrentEncoding.CodePage == 1201) { // UTF-16BE
                    if (byteBuffer[byteLen - 2] >= 0xd8)            // high-surrogate
                    {
                        numFragments = 2; // account for the partial character
                    }
                }
            }

            return reader.BaseStream.Position - numBytesLeft - numFragments;
        }

        #region IDisposable Support

        private bool disposed_;

        private void Dispose(bool disposing) {
            if (!disposed_) {
                dataReader_?.Dispose();
                dataStream_?.Dispose();
                dataReader_ = null;
                dataStream_ = null;
                disposed_ = true;
            }
        }

        ~UTCSectionReader() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
