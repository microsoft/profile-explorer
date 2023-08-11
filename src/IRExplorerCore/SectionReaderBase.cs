// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace IRExplorerCore {
    public abstract class SectionReaderBase : IRSectionReader, IDisposable {
        // Helper to quickly read a couple of private fields of a StreamReader
        // in order to compute the proper offset in the stream.
        // This is much faster (up to 10x) than using reflection to get to each field,
        // which was the main bottleneck in reading text files.
        // https://tyrrrz.me/blog/expression-trees
        class StreamReaderFields {
            public char[] CharBuffer;
            public int CharPos;
            public int CharLen;
            public byte[] ByteBuffer;
            public int ByteLen;

            private static Action<StreamReader, StreamReaderFields> callback_;

            static StreamReaderFields() {
                // Create and compile to IL a function that reads the private fields
                // from a StreamReader and saves the values.
                var inputParam = Expression.Parameter(typeof(StreamReader));
                var outputParam = Expression.Parameter(typeof(StreamReaderFields));
                var block = Expression.Block(
                    Expression.Assign(Expression.Field(outputParam, "CharBuffer"),
                                      Expression.Field(inputParam, "_charBuffer")),
                    Expression.Assign(Expression.Field(outputParam, "CharPos"),
                                      Expression.Field(inputParam, "_charPos")),
                    Expression.Assign(Expression.Field(outputParam, "CharLen"),
                                      Expression.Field(inputParam, "_charLen")),
                    Expression.Assign(Expression.Field(outputParam, "ByteBuffer"),
                                      Expression.Field(inputParam, "_byteBuffer")),
                    Expression.Assign(Expression.Field(outputParam, "ByteLen"),
                                      Expression.Field(inputParam, "_byteLen"))
                );

                callback_ = Expression.Lambda<Action<StreamReader, StreamReaderFields>>
                    (block, inputParam, outputParam).Compile();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static StreamReaderFields Read(StreamReader reader) {
                var readerFields = new StreamReaderFields();
                callback_(reader, readerFields);
                return readerFields;
            }
        }

        // Simple TextReader that can be used with a span or a substring.
        class SpanStringReader : TextReader {
            private ReadOnlyMemory<char> data_;
            private int position_;
            private int endPosition_;

            public int Position => position_;

            public SpanStringReader(ReadOnlyMemory<char> value, int length, int position = 0) {
                data_ = value;
                endPosition_ = position + length;
                position_ = position;
            }

            public SpanStringReader(ReadOnlyMemory<char> value, int position = 0) :
                this(value, value.Length, position) { }

            public SpanStringReader(string value, int length, int position = 0) {
                data_ = value.AsMemory();
                endPosition_ = position + length;
                position_ = position;
            }

            public SpanStringReader(string value, int position = 0) :
                this(value, value.Length, position) { }

            public override int Read() {
                if (position_ == endPosition_) {
                    return -1;
                }

                return (int)data_.Span[position_++];
            }

            public override int Peek() {
                if (position_ == endPosition_) {
                    return -1;
                }

                return (int)data_.Span[position_];
            }
        }


        private static readonly int FILE_BUFFER_SIZE = 512 * 1024;
        private static readonly int STREAM_BUFFER_SIZE = 16 * 1024;
        public static readonly long MAX_PRELOADED_FILE_SIZE = 256 * 1024 * 1024; // 256 MB
        private static readonly int MAX_LINE_LENGTH = 2000;

        private StreamReader dataReader_;
        private Stream dataStream_;
        private long dataStreamSize_;
        private Encoding dataStreamEncoding_;
        private bool expectSectionHeaders_;
        private object lockObject_;

        private Dictionary<string, IRTextFunction> functionMap_;
        private int lineIndex_;
        private IRPassOutput optionalOutput_;
        private string preloadedData_;
        private int prevLineCount_;
        private string[] prevLines_;
        private string currentLine_;
        private long nextInitialOffset_;
        private bool hasPreprocessedLines_;
        private bool hasMetadataLines_;
        private IRTextSummary summary_;
        private Stack<IRTextModule> moduleStack_;
        private List<IRTextSection> previousSections_;

        public SectionReaderBase(string filePath, bool expectSectionHeaders = true) {
            expectSectionHeaders_ = expectSectionHeaders;
            dataStreamSize_ = new FileInfo(filePath).Length;

            if (dataStreamSize_ < MAX_PRELOADED_FILE_SIZE) {
                dataStream_ = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                var textStream = new StreamReader(dataStream_);
                preloadedData_ = textStream.ReadToEnd();
                dataStream_.Seek(0, SeekOrigin.Begin);
            }
            else {
                dataStream_ = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            Initialize();
        }

        public SectionReaderBase(byte[] textData, bool expectSectionHeaders = true) {
            expectSectionHeaders_ = expectSectionHeaders;
            dataStream_ = new MemoryStream(textData);
            dataStreamSize_ = textData.Length;
            Initialize();
        }

        // Methods to be implemented by an IR reader implementation.
        protected abstract bool IsSectionStart(string line);

        protected virtual bool IsSectionEnd(string line) => false;

        protected abstract bool FunctionStartIsSectionStart(string line);

        protected abstract bool IsFunctionStart(string line);

        protected abstract bool IsFunctionEnd(string line);

        protected virtual bool IsBlockStart(string line) => false;

        protected virtual bool IsModuleStart(string line) => false;

        protected virtual bool IsModuleEnd(string line) => false;

        protected virtual string ExtractModuleName(string line) => null;

        protected abstract string ExtractSectionName(string line);

        protected abstract string ExtractFunctionName(string line);

        protected abstract string PreprocessLine(string line);

        protected abstract bool ShouldSkipOutputLine(string line);

        protected abstract bool IsMetadataLine(string line);

        protected abstract bool FunctionEndIsFunctionStart(string line);

        protected void MarkPreprocessedLine(int line) {
            hasPreprocessedLines_ = true;
        }

        // Main function for reading the text source and producing a summary
        // with all functions and their sections.
        public IRTextSummary GenerateSummary(ProgressInfoHandler progressHandler,
                                         SectionTextHandler sectionTextHandler) {
            var summary = GenerateSummaryImpl(progressHandler, sectionTextHandler);

            if (summary.Functions.Count == 0 && expectSectionHeaders_) {
                // Try parsing again, but without looking for section headers.
                // Useful when handling a single section copy-pasted from somewhere.
                expectSectionHeaders_ = false;
                dataStream_.Seek(0, SeekOrigin.Begin);
                dataReader_.DiscardBufferedData();

                ResetSummaryState();
                summary = GenerateSummaryImpl(progressHandler, sectionTextHandler);
            }

            return summary;
        }

        public string GetSectionText(IRTextSection section) {
            if (section.Output == null) {
                throw new NullReferenceException();
            }

            return GetPassOutputText(section.Output, false);
        }

        public ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section) {
            if (section.Output == null) {
                throw new NullReferenceException();
            }

            return GetPassOutputTextSpan(section.Output);
        }

        public List<string> GetSectionTextLines(IRTextSection section) {
            if (section.Output == null) {
                throw new NullReferenceException();
            }

            return GetPassOutputTextLines(section.Output, false);
        }

        public string GetPassOutputText(IRPassOutput output) {
            return GetPassOutputText(output, true);
        }

        public ReadOnlyMemory<char> GetPassOutputTextSpan(IRPassOutput output) {
            if (output == null) {
                throw new NullReferenceException();
            }

            if (!output.HasPreprocessedLines) {
                // Fast path that avoids reading text line by line.
                return GetRawPassOutputTextSpan(output, false);
            }

            var text = GetPassOutputText(output, false);
            return text.AsMemory();
        }

        public List<string> GetPassOutputTextLines(IRPassOutput output) {
            return GetPassOutputTextLines(output, true);
        }

        public string GetRawSectionText(IRTextSection section) {
            if (section.Output == null) {
                throw new NullReferenceException();
            }

            return GetRawPassOutputText(section.Output, false);
        }

        public ReadOnlyMemory<char> GetRawSectionTextSpan(IRTextSection section) {
            if (section.Output == null) {
                throw new NullReferenceException();
            }

            return GetRawPassOutputTextSpan(section.Output, false);
        }

        public string GetRawPassOutputText(IRPassOutput output) {
            return GetRawPassOutputText(output, true);
        }

        public ReadOnlyMemory<char> GetRawPassOutputTextSpan(IRPassOutput output) {
            return GetRawPassOutputTextSpan(output, true);
        }

        private Encoding DetectUTF8Encoding(Stream stream, Encoding defaultEncoding) {
            // Check if the stream uses UTF8, otherwise fall back
            // to a default encoding (ASCII) which is much faster to read.
            Encoding encoding = new UTF8Encoding(true);
            var preamble = encoding.GetPreamble();

            if (stream.Length < preamble.Length) {
                return defaultEncoding;
            }

            var position = stream.Position;
            Span<byte> buffer = stackalloc byte[preamble.Length];
            stream.Read(buffer);

            for (int i = 0; i < preamble.Length; i++) {
                if (buffer[i] != preamble[i]) {
                    encoding = defaultEncoding;
                    break;
                }
            }

            stream.Position = position;
            return encoding;
        }

        private void Initialize() {
            dataStreamEncoding_ = DetectUTF8Encoding(dataStream_, Encoding.ASCII);
            dataReader_ = new StreamReader(dataStream_, dataStreamEncoding_,
                                           false, STREAM_BUFFER_SIZE);
            prevLines_ = new string[3];
            summary_ = new IRTextSummary();
            functionMap_ = new Dictionary<string, IRTextFunction>();
            moduleStack_ = new Stack<IRTextModule>();
            lockObject_ = new object();
        }

        protected IRTextModule BeginModule(string name) {
            var module = new IRTextModule(name, GetCurrentModule());
            moduleStack_.Push(module);
            return module;
        }

        protected void EndModule() {
            if (moduleStack_.Count == 0) {
                return;
            }

            var module = moduleStack_.Pop();
            summary_.Modules.Add(module);
        }

        protected IRTextModule GetCurrentModule() {
            if (moduleStack_.Count == 0) {
                return null;
            }

            return moduleStack_.Peek();
        }

        public byte[] GetDocumentTextData() {
            lock (lockObject_) {
                dataStream_.Seek(0, SeekOrigin.Begin);
                var encoding = DetectUTF8Encoding(dataStream_, Encoding.ASCII);
                using var binaryReader = new BinaryReader(dataStream_, encoding, true);
                return binaryReader.ReadBytes((int)dataStream_.Length);
            }
        }

        private void ResetSummaryState() {
            optionalOutput_ = null;
            hasPreprocessedLines_ = false;
            hasMetadataLines_ = false;
            prevLineCount_ = 0;
            lineIndex_ = 0;
        }

        private IRTextSummary GenerateSummaryImpl(ProgressInfoHandler progressHandler,
                                              SectionTextHandler sectionTextHandler) {
            // Scan the document once to find the section boundaries,
            // any before/after text associated with the sections
            // and build the function -> sections hierarchy.
            ResetAdditionalOutput();

            Action updateProgress = () => {
                if (progressHandler != null) {
                    var info = new SectionReaderProgressInfo(TextOffset(), dataStreamSize_);
                    progressHandler(this, info);
                }
            };

            while(true) {
                if (!FindNextSection(sectionTextHandler)) {
                    break;
                }

                updateProgress();
            }

            return summary_;
        }

        private IRTextFunction GetOrCreateFunction(string name) {
            if (functionMap_.TryGetValue(name, out var textFunc)) {
                return textFunc;
            }

            textFunc = new IRTextFunction(name);
            summary_.AddFunction(textFunc);
            functionMap_.Add(name, textFunc);
            return textFunc;
        }

        private string GetPassOutputText(IRPassOutput output, bool isOptionalOutput) {
            if (output == null) {
                return "";
            }

            if (!output.HasPreprocessedLines) {
                // Fast path that avoids reading text line by line.
                return GetRawPassOutputText(output, isOptionalOutput);
            }

            // If the file was preloaded in memory, create a new stream
            // to allow parallel loading of text.
            if (preloadedData_ != null) {
                using var streamReader = CreatePreloadedDataReader(output);
                return ReadPassOutputText(streamReader, output, isOptionalOutput);
            }

            lock (lockObject_) {
                return ReadPassOutputText(dataReader_, output, isOptionalOutput);
            }
        }

        private List<string> GetPassOutputTextLines(IRPassOutput output, bool isOptionalOutput) {
            if (output == null) {
                return new List<string>();
            }

            // If the file was preloaded in memory, create a new stream
            // to allow parallel loading of text.
            if (preloadedData_ != null) {
                using var streamReader = CreatePreloadedDataReader(output);
                return ReadPassOutputTextLines(streamReader, output, isOptionalOutput);
            }

            lock (lockObject_) {
                return ReadPassOutputTextLines(dataReader_, output, isOptionalOutput);
            }
        }

        private SpanStringReader CreatePreloadedDataReader(IRPassOutput output) {
            return new SpanStringReader(preloadedData_, (int)output.Size, (int)output.DataStartOffset);
        }

        private string GetRawPassOutputText(IRPassOutput output, bool isOptionalOutput) {
            if (output == null) {
                return "";
            }

            if (preloadedData_ != null) {
                // For some use cases, such as text search on the whole document,
                // it is much faster to use the unfiltered text and avoid reading
                // the text line by line to form the final string.
                var span = preloadedData_.AsMemory((int)output.DataStartOffset, (int)output.Size);
                return span.ToString();
            }

            lock (lockObject_) {
                return ReadPassOutputText(dataReader_, output, isOptionalOutput);
            }
        }

        private ReadOnlyMemory<char> GetRawPassOutputTextSpan(IRPassOutput output, bool isOptionalOutput) {
            if (output == null) {
                return ReadOnlyMemory<char>.Empty;
            }

            if (preloadedData_ != null) {
                // For some use cases, such as text search on the whole document,
                // it is much faster to use the unfiltered text and avoid reading
                // the text line by line to form the final string.
                return preloadedData_.AsMemory((int)output.DataStartOffset, (int)output.Size);
            }

            lock (lockObject_) {
                var text = ReadPassOutputText(dataReader_, output, isOptionalOutput);
                return text.AsMemory();
            }
        }

        private string ReadPassOutputText(StreamReader reader, IRPassOutput output,
                                          bool isOptionalOutput) {
            reader.BaseStream.Position = output.DataStartOffset;
            reader.DiscardBufferedData();
            return ReadPassOutputText((TextReader)reader, output, isOptionalOutput);
        }

        private string ReadPassOutputText(TextReader reader, IRPassOutput output,
                                          bool isOptionalOutput) {
            var builder = new StringBuilder((int)output.Size);

            for (int i = output.StartLine; i <= output.EndLine; i++) {
                string line = NextLine(reader, false);

                if (isOptionalOutput && ShouldSkipOutputLine(line)) {
                    continue;
                }

                // Skip over metadata lines, they are not supposed to be part of the IR
                // and are already saved in the LineMetadata table of the IRTextSection.
                if (IsMetadataLine(line)) {
                    continue;
                }

                if (line.Length > MAX_LINE_LENGTH) {
                    line = line.Substring(0, MAX_LINE_LENGTH);
                }

                builder.AppendLine(line);
            }

            return builder.ToString();
        }

        private List<string> ReadPassOutputTextLines(StreamReader reader, IRPassOutput output,
                                                     bool isOptionalOutput) {
            reader.BaseStream.Position = output.DataStartOffset;
            reader.DiscardBufferedData();
            return ReadPassOutputTextLines((TextReader)reader, output, isOptionalOutput);
        }

        private List<string> ReadPassOutputTextLines(TextReader reader, IRPassOutput output,
                                                     bool isOptionalOutput) {
            var list = new List<string>(output.LineCount);

            for (int i = output.StartLine; i <= output.EndLine; i++) {
                string line = NextLine(reader, false);

                if (isOptionalOutput && ShouldSkipOutputLine(line)) {
                    continue;
                }

                if (line.Length > MAX_LINE_LENGTH) {
                    line = line.Substring(0, MAX_LINE_LENGTH);
                }

                list.Add(line);
            }

            return list;
        }

        private IRPassOutput AddOptionalOutputLine(string line, long initialOffset, IRPassOutput output) {
            if (output == null) {
                if (string.IsNullOrWhiteSpace(line)) {
                    return null; // Ignore empty lines at the start.
                }

                // Start a new optional section.
                long offset = TextOffset();
                output = new IRPassOutput(initialOffset, offset, lineIndex_, lineIndex_);
            }

            output.DataEndOffset = TextOffset();
            output.EndLine = lineIndex_;

            if (!string.IsNullOrWhiteSpace(line)) {
                if (IsMetadataLine(line)) {
                    hasMetadataLines_ = true;
                }
            }

            return output;
        }

        private IRPassOutput GetAdditionalOutput() {
            if (optionalOutput_ != null) {
                optionalOutput_.HasPreprocessedLines = hasPreprocessedLines_ || hasMetadataLines_;
                return optionalOutput_;
            }

            return null;
        }

        private void ResetAdditionalOutput() {
            optionalOutput_ = null;
            hasPreprocessedLines_ = false;
            hasMetadataLines_ = false;
        }

        private bool SkipToSectionStart(SectionTextHandler sectionTextHandler, out bool hasSectionName) {
            prevLineCount_ = 0;
            long initialOffset = nextInitialOffset_;

            while(true) {
                // Find the start of the next section.

                if (currentLine_ == null ||
                    (!IsSectionStart(currentLine_) && !IsFunctionEnd(currentLine_))) {
                    currentLine_ = NextLine();
                }

                if (currentLine_ == null) {
                    hasSectionName = false;
                    return false;
                }

                // Each section is expected to start with a name,
                // followed by an ASCII "currentLine_", which is searched for here,
                // unless the client indicates that the name may be missing.
                if (IsSectionStart(currentLine_)) {
                    hasSectionName = true;
                    return true;
                }
                else if (!expectSectionHeaders_ && IsFunctionStart(currentLine_)) {
                    // Function start without being in a named section.
                    hasSectionName = false;
                    return true;
                }
                else {
                    // Skip over text in-between sections.
                    optionalOutput_ = AddOptionalOutputLine(currentLine_, initialOffset, optionalOutput_);
                    lineIndex_++;
                }
            }

            return false;
        }

        private string BuildFunctionName(string name) {
            var module = GetCurrentModule();

            while (module != null) {
                if (module.HasName) {
                    name = $"{module.Name}.{name}";
                }

                module = module.ParentModule;
            }

            return name;
        }

        private bool FindNextSection(SectionTextHandler sectionTextHandler) {
            bool hasSectionName = true;

            if (!SkipToSectionStart(sectionTextHandler, out hasSectionName)) {
                return false;
            }

            // Extract section and function names.
            string sectionName = string.Empty;
            string funcName = string.Empty;

            if (hasSectionName) {
                sectionName = ExtractSectionName(currentLine_);
            }

            // Collect the text lines if the client wants to process
            // the text while first reading the document.
            List<string> sectionLines = null;

            if (sectionTextHandler != null) {
                sectionLines = new List<string>();
            }

            // Go back and find the name of the section.
            int sectionStartLine = lineIndex_ + (hasSectionName ? 1 : 0);
            int sectionEndLine = sectionStartLine;

            // A section can have metadata associated with the previous line.
            Dictionary<int, string> lineMetadata = null;
            int metadataLines = 0;

            Trace.WriteLine($"=> New section at line {lineIndex_}: {sectionName}, {currentLine_}");

            // Collect all functions in the section.
            bool foundFunctionStart = false;
            bool foundFunctionEnd = false;

            List<IRTextSection> sections = new List<IRTextSection>(1);
            long funcInitialOffset = hasSectionName ? TextOffset() : nextInitialOffset_;
            long funcStartOffset = 0;
            long funcEndOffset = 0;
            int funcStartLine = 0;
            int funcEndLine = 0;
            int blockCount = 0;
            int functionIndex = 0;
            bool foundNextFunction = false;

            IRPassOutput moduleOutput = null;

            if (hasSectionName) {
                currentLine_ = NextLine();
            }

            while (true) {
                foundNextFunction = false;

                if (currentLine_ == null) {
                    sectionEndLine = lineIndex_ + 1;
                    funcEndLine = lineIndex_ + 1;
                    funcEndOffset = TextOffset();
                    foundFunctionEnd = true;
                    Trace.WriteLine($"  - at doc end on line {lineIndex_}");
                }
                else {
                    if (sectionTextHandler != null) {
                        if (!IsFunctionEnd(currentLine_) || !FunctionEndIsFunctionStart(currentLine_)) {
                            sectionLines.Add(currentLine_);
                            Trace.WriteLine($"   - func line: {currentLine_}");
                        }
                    }

                    //? TODO: Wrong line numbers from MLIR, reason why preview popup shows up for uses
                    moduleOutput = AddOptionalOutputLine(currentLine_, funcInitialOffset, moduleOutput);

                    if (IsModuleStart(currentLine_)) {
                        BeginModule(ExtractModuleName(currentLine_));
                    }
                    else if (IsModuleEnd(currentLine_)) {
                        EndModule();
                    }

                    if (IsFunctionStart(currentLine_)) {
                        if (foundFunctionStart) {
                            foundNextFunction = true;
                        }
                        else {
                            // Extract function name.
                            funcName = ExtractFunctionName(currentLine_);
                            funcStartLine = lineIndex_;
                            funcStartOffset = previousOffset_; //? Or TextOffset() - currentLine.Length?
                            foundFunctionStart = true;

                            Trace.WriteLine($"  > start func at {lineIndex_}: {funcName}");
                        }
                    }

                    if (foundNextFunction || IsFunctionEnd(currentLine_)) {
                        // Found function end.
                        funcEndOffset = FunctionEndIsFunctionStart(currentLine_) ? previousOffset_ : TextOffset();
                        sectionEndLine = lineIndex_ + 1;
                        funcEndLine = lineIndex_ + 1;
                        nextInitialOffset_ = funcEndOffset;
                        foundFunctionEnd = true;

                        Trace.WriteLine($"  < end func at {lineIndex_}, lines {funcStartLine}-{funcEndLine}");
                    }
                    else if (IsBlockStart(currentLine_)) {
                        blockCount++;
                    }
                    else if (IsMetadataLine(currentLine_)) {
                        // Add line - metadata mapping to auxiliary table.
                        lineMetadata ??= new Dictionary<int, string>();
                        lineMetadata[lineIndex_ - sectionStartLine - metadataLines] = currentLine_;
                        metadataLines++;
                    }
                }

                if (foundFunctionStart && foundFunctionEnd) {
                    sectionEndLine = Math.Max(sectionEndLine, sectionStartLine);
                    int lines = funcEndLine - funcStartLine;

                    // Create the a new section and its corresponding function, if needed.
                    var output = new IRPassOutput(funcStartOffset, funcEndOffset,
                        funcStartLine, funcEndLine) {
                        HasPreprocessedLines = hasPreprocessedLines_ || lineMetadata != null,
                    };

                    var textFunc = GetOrCreateFunction(BuildFunctionName(funcName));

                    var section = new IRTextSection(textFunc, sectionName, output, blockCount);
                    section.IndexInModule = functionIndex++;
                    sections.Add(section);
                    summary_.AddSection(section);
                    textFunc.AddSection(section);

                    // Attach additional output text.
                    section.OutputBefore = GetAdditionalOutput();
                    section.ModuleOutput = moduleOutput;

                    if(section.ModuleOutput  != null) {
                        Trace.WriteLine($"  - section output before: {section.ModuleOutput}");
                    }

                    if (previousSections_ != null) {
                        foreach(var prevSection in previousSections_) {
                            prevSection.OutputAfter = GetAdditionalOutput();
                        }
                    }

                    // Notify client a new section has been read.
                    if (sectionTextHandler != null) {
                        sectionTextHandler(this, new SectionReaderText(output, sectionLines));
                    }

                    // Attach any metadata lines.
                    if (lineMetadata != null) {
                        section.LineMetadata = lineMetadata;
                        section.CompressLineMetadata();
                    }

                    funcStartOffset = TextOffset();
                    funcEndOffset = funcStartOffset;
                    funcStartLine = lineIndex_ + 1;
                    funcEndLine = funcStartLine;
                    foundFunctionStart = foundFunctionEnd = false;
                }

                if (!foundNextFunction) {
                    currentLine_ = NextLine();
                    lineIndex_++;
                }

                if (currentLine_ == null ||
                    IsSectionEnd(currentLine_) ||
                    IsSectionStart(currentLine_)) {
                    break;
                }
            }

            EndModule();
            previousSections_ = sections;
            return true;
        }

        protected string NextLine() {
            return NextLine(dataReader_, true);
        }

        protected string NextLine(TextReader reader, bool recordPreviousLines) {
            previousOffset_ = textOffset_;
            string line = reader.ReadLine();

            if (line == null) {
                return null;
            }

            line = PreprocessLine(line);

            if (recordPreviousLines) {
                RecordPreviousLine(line);
            }

            if (reader == dataReader_) {
                textOffset_ = ComputeTextOffset(dataReader_);
            }
            else {
                textOffset_ = ((SpanStringReader)reader).Position;
            }

            return line;
        }

        protected string PreviousLine(int offset) {
            return offset < prevLineCount_ ? prevLines_[offset] : null;
        }

        protected void RecordPreviousLine(string line) {
            for (int i = prevLines_.Length - 1; i >= 1; i--) {
                prevLines_[i] = prevLines_[i - 1];
            }

            prevLines_[0] = line;
            prevLineCount_++;
        }
        private long textOffset_ = 0;
        private long previousOffset_ = 0;

        protected long TextOffset() => textOffset_;

        private long ComputeTextOffset(StreamReader reader) {
            // This is a hack needed to get the proper offset in the stream, see
            // https://stackoverflow.com/questions/5404267/streamreader-and-seeking
            // Dynamic code generation is used as an efficient way of reading out the private fields.
            var fields = StreamReaderFields.Read(reader);

            // The number of bytes the remaining chars use in the original encoding.
            int numBytesLeft = reader.CurrentEncoding.GetByteCount(fields.CharBuffer, fields.CharPos,
                                                                   fields.CharLen - fields.CharPos);

            // For variable-byte encodings, deal with partial chars at the end of the buffer
            int numFragments = 0;

            if (fields.ByteLen > 0 && !reader.CurrentEncoding.IsSingleByte) {
                if (reader.CurrentEncoding.CodePage == 65001) { // UTF-8
                    byte byteCountMask = 0;

                    while (fields.ByteBuffer[fields.ByteLen - numFragments - 1] >> 6 == 2
                    ) // if the byte is "10xx xxxx", it's a continuation-byte
                    {
                        byteCountMask |=
                            (byte)(1 <<
                                    ++numFragments
                            ); // count bytes & build the "complete char" mask
                    }

                    if (fields.ByteBuffer[fields.ByteLen - numFragments - 1] >> 6 == 3
                    ) // if the byte is "11xx xxxx", it starts a multi-byte char.
                    {
                        byteCountMask |=
                            (byte)(1 <<
                                    ++numFragments
                            ); // count bytes & build the "complete char" mask
                    }

                    // see if we found as many bytes as the leading-byte says to expect
                    if (numFragments > 1 &&
                        fields.ByteBuffer[fields.ByteLen - numFragments] >> (7 - numFragments) ==
                        byteCountMask) {
                        numFragments = 0; // no partial-char in the byte-buffer to account for
                    }
                }
                else if (reader.CurrentEncoding.CodePage == 1200) { // UTF-16LE
                    if (fields.ByteBuffer[fields.ByteLen - 1] >= 0xd8)            // high-surrogate
                    {
                        numFragments = 2; // account for the partial character
                    }
                }
                else if (reader.CurrentEncoding.CodePage == 1201) { // UTF-16BE
                    if (fields.ByteBuffer[fields.ByteLen - 2] >= 0xd8)            // high-surrogate
                    {
                        numFragments = 2; // account for the partial character
                    }
                }
            }

            return reader.BaseStream.Position - numBytesLeft - numFragments;
        }

        #region IDisposable Support

        private bool disposed_;

        protected void Dispose(bool disposing) {
            if (!disposed_) {
                dataReader_?.Dispose();
                dataStream_?.Dispose();
                dataReader_ = null;
                dataStream_ = null;
                preloadedData_ = null;
                disposed_ = true;
            }
        }

        ~SectionReaderBase() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}