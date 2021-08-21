// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using IRExplorerCore.IR;

namespace IRExplorerCore {
    public class ParsedIRTextSection {
        public IRTextSection Section { get; set; }
        public ReadOnlyMemory<char> Text { get; set; }
        public FunctionIR Function { get; set; }
        public List<IRParsingError> ParsingErrors { get; set; }
        public bool IsCached { get; set; }

        public bool HadParsingErrors => ParsingErrors != null && ParsingErrors.Count > 0;

        public ParsedIRTextSection(IRTextSection section, ReadOnlyMemory<char> text, FunctionIR function) {
            Section = section;
            Text = text;
            Function = function;
        }

        public override string ToString() {
            return Section.ToString();
        }
    }

    public abstract class IRTextSectionLoader : IDisposable {
        protected Dictionary<IRTextSection, ParsedIRTextSection> sectionCache_;
        protected ICompilerIRInfo irInfo_;
        protected bool cacheEnabled_;
        protected object lockObject_;
        protected long sectionPreprocessingCompleted_;

        public event EventHandler<bool> SectionPreprocessingCompleted;

        protected void NotifySectionPreprocessingCompleted(bool canceled) {
            Interlocked.Exchange(ref sectionPreprocessingCompleted_, 1);
            SectionPreprocessingCompleted?.Invoke(this, canceled);
        }

        protected void Initialize(ICompilerIRInfo irInfo, bool cacheEnabled) {
            irInfo_ = irInfo;
            cacheEnabled_ = cacheEnabled;
            lockObject_ = new object();
            sectionPreprocessingCompleted_ = 0;
            sectionCache_ = new Dictionary<IRTextSection, ParsedIRTextSection>();
        }

        protected (IRSectionParser, IRParsingErrorHandler) InitializeParser() {
            var errorHandler = irInfo_.CreateParsingErrorHandler();
            return (irInfo_.CreateSectionParser(errorHandler), errorHandler);
        }

        public bool SectionSignaturesComputed => Interlocked.Read(ref sectionPreprocessingCompleted_) != 0;

        public abstract IRTextSummary LoadDocument(ProgressInfoHandler progressHandler);
        public abstract string GetDocumentOutputText();
        public abstract byte[] GetDocumentTextBytes();
        public abstract ParsedIRTextSection LoadSection(IRTextSection section);
        public abstract string GetSectionText(IRTextSection section);
        public abstract ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section);
        public abstract string GetSectionOutputText(IRPassOutput output);
        public abstract ReadOnlyMemory<char> GetSectionOutputTextSpan(IRPassOutput output);
        public abstract List<string> GetSectionOutputTextLines(IRPassOutput output);
        public abstract string GetRawSectionText(IRTextSection section);
        public abstract string GetRawSectionPassOutput(IRPassOutput output);
        public abstract ReadOnlyMemory<char> GetRawSectionTextSpan(IRTextSection section);
        public abstract ReadOnlyMemory<char> GetRawSectionPassOutputSpan(IRPassOutput output);

        public ParsedIRTextSection TryGetLoadedSection(IRTextSection section) {
            if (!cacheEnabled_) {
                return null;
            }

            lock (lockObject_) {
                return sectionCache_.TryGetValue(section, out var result) ? result : null;
            }
        }

        public string GetSectionText(IRTextSection section, bool useCache = true) {
            var result = GetSectionTextSpan(section, useCache);
            return result.ToString();
        }

        public ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section, bool useCache = true) {
            if (useCache && cacheEnabled_) {
                lock (lockObject_) {
                    if (sectionCache_.TryGetValue(section, out var result)) {
                        return result.Text;
                    }
                }
            }

            return GetSectionTextSpan(section);
        }

        public string GetRawSectionText(IRTextSection section, bool useCache = true) {
            return GetRawSectionText(section);
        }

        public void SuspendCaching() {
            cacheEnabled_ = false;
        }

        public void ResumeCaching() {
            cacheEnabled_ = true;
        }

        public void ResetCache() {
            if (!cacheEnabled_) {
                return;
            }

            lock (lockObject_) {
                sectionCache_.Clear();
            }
        }

        #region IDisposable Support

        protected bool disposed_;
        protected abstract void Dispose(bool disposing);

        ~IRTextSectionLoader() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
