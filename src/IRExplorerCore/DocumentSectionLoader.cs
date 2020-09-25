// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Text;
using IRExplorerCore.IR;

namespace IRExplorerCore {
    public class DocumentSectionLoader : IRTextSectionLoader {
        private IRSectionReader documentReader_;

        public DocumentSectionLoader(ICompilerIRInfo irInfo, bool useCache = true) {
            Initialize(irInfo, useCache);
        }

        public DocumentSectionLoader(string filePath, ICompilerIRInfo irInfo, bool useCache = true) {
            Initialize(irInfo, useCache);
            documentReader_ = irInfo.CreateSectionReader(filePath);
        }

        public DocumentSectionLoader(byte[] textData, ICompilerIRInfo irInfo, bool useCache = true) {
            Initialize(irInfo, useCache);
            documentReader_ = irInfo_.CreateSectionReader(textData);
        }

        public override IRTextSummary LoadDocument(ProgressInfoHandler progressHandler) {
            return documentReader_.GenerateSummary(progressHandler);
        }

        public override string GetDocumentText() {
            var data = documentReader_.GetDocumentTextData();
            return Encoding.UTF8.GetString(data);
        }

        public override byte[] GetDocumentTextBytes() {
            return documentReader_.GetDocumentTextData();
        }

        public override ParsedIRTextSection LoadSection(IRTextSection section) {
            Trace.TraceInformation(
                $"Section loader {ObjectTracker.Track(this)}: ({section.Number}) {section.Name}");

            lock (lockObject_) {
                if (cacheEnabled_ && sectionCache_.TryGetValue(section, out var cachedResult)) {
                    Trace.TraceInformation($"Section loader {ObjectTracker.Track(this)}: found in cache");
                    return cachedResult;
                }
            }

            string text = GetSectionText(section);

            //? TODO: Workaround for not having an LLVM parser
            var (sectionParser, errorHandler) = InitializeParser();
            FunctionIR function;

            if (sectionParser == null) {
                function = new FunctionIR();
            }
            else {
                function = sectionParser.ParseSection(section, text);
            }

            var result = new ParsedIRTextSection(section, text, function);

            lock (lockObject_) {
                if (cacheEnabled_ && function != null) {
                    sectionCache_[section] = result;
                }
            }

            if (errorHandler.HadParsingErrors) {
                result.ParsingErrors = errorHandler.ParsingErrors;
            }

            return result;
        }

        public override string GetSectionText(IRTextSection section) {
            return documentReader_.GetSectionText(section);
        }

        public override string GetSectionPassOutput(IRPassOutput output) {
            if (output == null) {
                // With some documents there is no before/after text.
                return string.Empty;
            }

            return documentReader_.GetPassOutputText(output);
        }

        public override string GetRawSectionText(IRTextSection section) {
            return documentReader_.GetRawSectionText(section);
        }

        public override string GetRawSectionPassOutput(IRPassOutput output) {
            return documentReader_.GetRawPassOutputText(output);
        }

        protected override void Dispose(bool disposing) {
            if (!disposed_) {
                documentReader_?.Dispose();
                documentReader_ = null;
                disposed_ = true;
            }
        }
    }
}
