// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using CoreLib;
using CoreLib.IR;
using CoreLib.UTC;

namespace Client {
    public class ParsedSection {
        public IRTextSection Section;

        public ParsedSection(IRTextSection section, string text, FunctionIR function) {
            Section = section;
            Text = text;
            Function = function;
        }

        public string Text { get; set; }
        public FunctionIR Function { get; set; }
        public List<IRParsingError> ParsingErrors { get; set; }
        public bool HadParsingErrors => ParsingErrors != null && ParsingErrors.Count > 0;

        public override string ToString() {
            return Section.ToString();
        }
    }

    public abstract class SectionLoader {
        protected UTCParsingErrorHandler errorHandler_;
        protected Dictionary<IRTextSection, ParsedSection> sectionCache_;
        protected UTCSectionParser sectionParser_;

        protected void Initialize() {
            sectionCache_ = new Dictionary<IRTextSection, ParsedSection>();
            errorHandler_ = new UTCParsingErrorHandler();
            sectionParser_ = new UTCSectionParser(errorHandler_);
        }

        public abstract IRTextSummary LoadDocument();
        public abstract byte[] GetDocumentText();
        public abstract ParsedSection LoadSection(IRTextSection section);
        public abstract string GetSectionText(IRTextSection section);
        public abstract string LoadSectionPassOutput(IRPassOutput output);

        public ParsedSection TryGetLoadedSection(IRTextSection section) {
            lock (this) {
                return sectionCache_.TryGetValue(section, out var result) ? result : null;
            }
        }

        public string LoadSectionText(IRTextSection section, bool useCache = true) {
            if (useCache) {
                lock (this) {
                    return sectionCache_.TryGetValue(section, out var result)
                        ? result.Text
                        : GetSectionText(section);
                }
            }
            else {
                return GetSectionText(section);
            }
        }
    }

    public class DocumentSectionLoader : SectionLoader, IDisposable {
        private UTCReader documentReader_;

        public DocumentSectionLoader() {
            Initialize();
        }

        public DocumentSectionLoader(string filePath) {
            Initialize();
            documentReader_ = new UTCReader(filePath);
        }

        public DocumentSectionLoader(byte[] textData) {
            Initialize();
            documentReader_ = new UTCReader(textData);
        }

        public override IRTextSummary LoadDocument() {
            return documentReader_.GenerateSummary();
        }

        public override byte[] GetDocumentText() {
            return documentReader_.GetDocumentTextData();
        }

        public override ParsedSection LoadSection(IRTextSection section) {
            lock (this) {
                Trace.TraceInformation(
                    $"Section loader {ObjectTracker.Track(this)}: ({section.Number}) {section.Name}");

                if (sectionCache_.TryGetValue(section, out var result)) {
                    Trace.TraceInformation($"Section loader {ObjectTracker.Track(this)}: found in cache");
                    return result;
                }

                string text = GetSectionText(section);
                var function = sectionParser_.ParseSection(section, text);
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

        public override string GetSectionText(IRTextSection section) {
            return documentReader_.GetSectionText(section);
        }

        public override string LoadSectionPassOutput(IRPassOutput output) {
            return documentReader_.GetPassOutputText(output);
        }

        #region IDisposable Support

        private bool disposed_;

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

    public class DebugSectionLoader : SectionLoader {
        private Dictionary<IRTextSection, string> sectionTextMap_;
        private IRTextSummary summary_;

        public DebugSectionLoader() {
            Initialize();
            sectionTextMap_ = new Dictionary<IRTextSection, string>();
        }

        public override IRTextSummary LoadDocument() {
            if (summary_ == null) {
                summary_ = new IRTextSummary();
            }

            return summary_;
        }

        public override byte[] GetDocumentText() {
            //? TODO: Merge all sections
            return null;
        }

        public void AddSection(IRTextSection section, string text) {
            sectionTextMap_.Add(section, text);
        }

        public override ParsedSection LoadSection(IRTextSection section) {
            lock (this) {
                Trace.TraceInformation(
                    $"Debug section loader {ObjectTracker.Track(this)}: ({section.Number}) {section.Name}");

                if (sectionCache_.TryGetValue(section, out var result)) {
                    Trace.TraceInformation(
                        $"Debug section loader {ObjectTracker.Track(this)}: found in cache");

                    return result;
                }

                string text = GetSectionText(section);
                var function = sectionParser_.ParseSection(section, text);
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

        public override string GetSectionText(IRTextSection section) {
            return sectionTextMap_[section];
        }

        public override string LoadSectionPassOutput(IRPassOutput output) {
            return "";
        }
    }
}
