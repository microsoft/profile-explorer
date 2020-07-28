// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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
        protected Dictionary<IRTextSection, ParsedSection> sectionCache_;
        protected ICompilerIRInfo irInfo_;
        protected bool cacheEnabled_;

        protected void Initialize(ICompilerIRInfo irInfo, bool cacheEnabled) {
            irInfo_ = irInfo;
            cacheEnabled_ = cacheEnabled;

            if (cacheEnabled) {
                sectionCache_ = new Dictionary<IRTextSection, ParsedSection>();
            }
        }

        protected (IRSectionParser, IRParsingErrorHandler) InitializeParser() {
            var errorHandler = irInfo_.CreateParsingErrorHandler();
            return (irInfo_.CreateSectionParser(errorHandler), errorHandler);
        }

        public abstract IRTextSummary LoadDocument(ProgressInfoHandler progressHandler);
        public abstract string GetDocumentText();
        public abstract byte[] GetDocumentTextBytes();
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
        private IRSectionReader documentReader_;

        public DocumentSectionLoader(ICompilerIRInfo irInfo) {
            Initialize(irInfo, true);
        }

        public DocumentSectionLoader(string filePath, ICompilerIRInfo irInfo) {
            Initialize(irInfo, true);
            documentReader_ = irInfo.CreateSectionReader(filePath);
        }

        public DocumentSectionLoader(byte[] textData, ICompilerIRInfo irInfo) {
            Initialize(irInfo, true);
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

        public override ParsedSection LoadSection(IRTextSection section) {
            Trace.TraceInformation(
                $"Section loader {ObjectTracker.Track(this)}: ({section.Number}) {section.Name}");

            lock (this) {
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

            var result = new ParsedSection(section, text, function);

            lock (this) {
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

        public DebugSectionLoader(ICompilerIRInfo irInfo) {
            Initialize(irInfo, cacheEnabled: false);
            sectionTextMap_ = new Dictionary<IRTextSection, CompressedString>();
        }

        public override IRTextSummary LoadDocument(ProgressInfoHandler progressHandler) {
            if (summary_ == null) {
                summary_ = new IRTextSummary();
            }

            return summary_;
        }

        public override string GetDocumentText() {
            var builder = new StringBuilder();
            var list = new List<Tuple<IRTextSection, CompressedString>>();

            foreach (var pair in sectionTextMap_) {
                list.Add(new Tuple<IRTextSection, CompressedString>(pair.Key, pair.Value));
            }

            list.Sort((a, b) => a.Item1.Number.CompareTo(b.Item1.Number));

            foreach (var pair in list) {
                builder.AppendLine(pair.Item1.Name); // Section name.
                builder.AppendLine(pair.Item2.ToString()); // Section text.
                builder.AppendLine();
            }

            return builder.ToString();
        }

        public override byte[] GetDocumentTextBytes() {
            return Encoding.UTF8.GetBytes(GetDocumentText());
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

                var (sectionParser, errorHandler) = InitializeParser();
                string text = GetSectionText(section);
                var function = sectionParser.ParseSection(section, text);
                result = new ParsedSection(section, text, function);

                if (cacheEnabled_ && function != null) {
                    sectionCache_[section] = result;
                }

                if (errorHandler.HadParsingErrors) {
                    result.ParsingErrors = errorHandler.ParsingErrors;
                }

                return result;
            }
        }

        public override string GetSectionText(IRTextSection section) {
            if (section == lastSection_) {
                return lastSectionText_;
            }

            return sectionTextMap_[section].ToString();
        }

        public override string LoadSectionPassOutput(IRPassOutput output) {
            return "";
        }
    }
}
