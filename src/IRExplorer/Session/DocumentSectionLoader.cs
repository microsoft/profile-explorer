// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;

namespace IRExplorer {
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
        protected bool cacheEnabled_;

        protected void Initialize(bool cacheEnabled) {
            cacheEnabled_ = cacheEnabled;

            if (cacheEnabled) {
                sectionCache_ = new Dictionary<IRTextSection, ParsedSection>();
            }
        }

        protected void InitializeParser() {
            errorHandler_ = new UTCParsingErrorHandler();
            sectionParser_ = new UTCSectionParser(errorHandler_);
        }

        protected void FreeParser() {
            // Allow memory to be freed, for large functions can be fairly significant.
            errorHandler_ = null;
            sectionParser_ = null;
        }

        public abstract IRTextSummary LoadDocument(ProgressInfoHandler progressHandler);
        public abstract byte[] GetDocumentText();
        public abstract ParsedSection LoadSection(IRTextSection section);
        public abstract string GetSectionText(IRTextSection section);
        public abstract string LoadSectionPassOutput(IRPassOutput output);

        public ParsedSection TryGetLoadedSection(IRTextSection section) {
            if (!cacheEnabled_) {
                return null;
            }

            lock (this) {
                return sectionCache_.TryGetValue(section, out var result) ? result : null;
            }
        }

        public string LoadSectionText(IRTextSection section, bool useCache = true) {
            if (useCache && cacheEnabled_) {
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
        private UTCSectionReader documentReader_;

        public DocumentSectionLoader() {
            Initialize(true);
        }

        public DocumentSectionLoader(string filePath) {
            Initialize(true);
            documentReader_ = new UTCSectionReader(filePath);
        }

        public DocumentSectionLoader(byte[] textData) {
            Initialize(true);
            documentReader_ = new UTCSectionReader(textData);
        }

        public override IRTextSummary LoadDocument(ProgressInfoHandler progressHandler) {
            return documentReader_.GenerateSummary(progressHandler);
        }

        public override byte[] GetDocumentText() {
            return documentReader_.GetDocumentTextData();
        }

        public override ParsedSection LoadSection(IRTextSection section) {
            lock (this) {
                Trace.TraceInformation(
                    $"Section loader {ObjectTracker.Track(this)}: ({section.Number}) {section.Name}");

                if (cacheEnabled_ && sectionCache_.TryGetValue(section, out var result)) {
                    Trace.TraceInformation($"Section loader {ObjectTracker.Track(this)}: found in cache");
                    return result;
                }

                InitializeParser();
                string text = GetSectionText(section);
                var function = sectionParser_.ParseSection(section, text);
                result = new ParsedSection(section, text, function);

                if (cacheEnabled_ && function != null) {
                    sectionCache_.Add(section, result);
                }

                if (errorHandler_.HadParsingErrors) {
                    result.ParsingErrors = errorHandler_.ParsingErrors;
                }

                FreeParser();
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
        private Dictionary<IRTextSection, CompressedString> sectionTextMap_;
        private IRTextSummary summary_;
        private IRTextSection lastSection_;
        private string lastSectionText_;

        public DebugSectionLoader() {
            Initialize(false);
            sectionTextMap_ = new Dictionary<IRTextSection, CompressedString>();
        }

        public override IRTextSummary LoadDocument(ProgressInfoHandler progressHandler) {
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
            //? TODO: Compress on another thread
            Trace.TraceInformation($"Adding section {section.Name}, length {text.Length}");
            sectionTextMap_[section] = new CompressedString(text);
            lastSection_ = section;
            lastSectionText_ = text;
        }

        public override ParsedSection LoadSection(IRTextSection section) {
            lock (this) {
                Trace.TraceInformation(
                    $"Debug section loader {ObjectTracker.Track(this)}: ({section.Number}) {section.Name}");

                if (cacheEnabled_ && sectionCache_.TryGetValue(section, out var result)) {
                    Trace.TraceInformation(
                        $"Debug section loader {ObjectTracker.Track(this)}: found in cache");

                    return result;
                }

                InitializeParser();
                string text = GetSectionText(section);
                var function = sectionParser_.ParseSection(section, text);
                result = new ParsedSection(section, text, function);

                if (cacheEnabled_ && function != null) {
                    sectionCache_.Add(section, result);
                }

                if (errorHandler_.HadParsingErrors) {
                    result.ParsingErrors = errorHandler_.ParsingErrors;
                }

                FreeParser();
                return result;
            }
        }

        public override string GetSectionText(IRTextSection section) {
            if(section == lastSection_) {
                return lastSectionText_;
            }

            return sectionTextMap_[section].ToString();
        }

        public override string LoadSectionPassOutput(IRPassOutput output) {
            return "";
        }
    }
}
