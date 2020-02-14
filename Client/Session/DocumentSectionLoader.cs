// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Core;
using Core.IR;
using Core.UTC;

namespace Client {
    public class ParsedSection {
        public IRTextSection Section;
        public string Text { get; set; }
        public FunctionIR Function { get; set; }
        public List<IRParsingError> ParsingErrors { get; set; }
        public bool HadParsingErrors => ParsingErrors != null && ParsingErrors.Count > 0;

        public ParsedSection(IRTextSection section, string text, FunctionIR function) {
            Section = section;
            Text = text;
            Function = function;
        }

        public override string ToString() {
            return Section.ToString();
        }
    }


    public class DocumentSectionLoader : IDisposable {
        private Dictionary<IRTextSection, ParsedSection> sectionCache_;
        private UTCReader documentReader_;
        private UTCSectionParser sectionParser_;
        private UTCParsingErrorHandler errorHandler_;

        public DocumentSectionLoader(string filePath) {
            Initialize();
            documentReader_ = new UTCReader(filePath);
        }

        public DocumentSectionLoader(byte[] textData) {
            Initialize();
            documentReader_ = new UTCReader(textData);
        }

        private void Initialize() {
            sectionCache_ = new Dictionary<IRTextSection, ParsedSection>();
            errorHandler_ = new UTCParsingErrorHandler();
            sectionParser_ = new UTCSectionParser(errorHandler_);
        }

        public IRTextSummary LoadDocument() {
            return documentReader_.GenerateSummary();
        }

        public byte[] GetDocumentText() {
            return documentReader_.GetDocumentTextData();
        }

        public ParsedSection LoadSection(IRTextSection section) {
            lock (this) {
                Trace.TraceInformation($"Section loader {ObjectTracker.Track(this)}: ({section.Number}) {section.Name}");

                if (sectionCache_.TryGetValue(section, out var result)) {
                    Trace.TraceInformation($"Section loader {ObjectTracker.Track(this)}: found in cache");
                    return result;
                }

                var text = documentReader_.GetSectionText(section);
                var function = sectionParser_.ParseSection(text);
                result = new ParsedSection(section, text, function);

                if (function != null) {
                    sectionCache_.Add(section, result);
                }

                if (errorHandler_.HadParsingErrors) {
                    result.ParsingErrors = errorHandler_.ParsingErrors;
                }

                return result;
            }
        }

        public ParsedSection TryGetLoadedSection(IRTextSection section) {
            lock(this) {
                if(sectionCache_.TryGetValue(section, out var result)) {
                    return result;
                }

                return null;
            }
        }

        public string LoadSectionText(IRTextSection section, bool useCache = true) {
            if (useCache) {
                lock (this) {
                    if (sectionCache_.TryGetValue(section, out var result)) {
                        return result.Text;
                    }

                    return documentReader_.GetSectionText(section);
                }
            }
            else {
                return documentReader_.GetSectionText(section);
            }
        }

        public string LoadSectionPassOutput(IRPassOutput output) {
            return documentReader_.GetPassOutputText(output);
        }

        #region IDisposable Support
        private bool disposed_ = false;

        protected virtual void Dispose(bool disposing) {
            if (!disposed_) {
                documentReader_?.Dispose();
                documentReader_ = null;
                disposed_ = true;
            }
        }

        ~DocumentSectionLoader() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
