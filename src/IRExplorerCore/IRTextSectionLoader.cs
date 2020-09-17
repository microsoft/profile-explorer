// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore {
    public class ParsedIRTextSection {
        public IRTextSection Section;

        public ParsedIRTextSection(IRTextSection section, string text, FunctionIR function) {
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

    public abstract class IRTextSectionLoader : IDisposable {
        protected Dictionary<IRTextSection, ParsedIRTextSection> sectionCache_;
        protected ICompilerIRInfo irInfo_;
        protected bool cacheEnabled_;
        protected object lockObject_;

        protected void Initialize(ICompilerIRInfo irInfo, bool cacheEnabled) {
            irInfo_ = irInfo;
            cacheEnabled_ = cacheEnabled;
            lockObject_ = new object();

            if (cacheEnabled) {
                sectionCache_ = new Dictionary<IRTextSection, ParsedIRTextSection>();
            }
        }

        protected (IRSectionParser, IRParsingErrorHandler) InitializeParser() {
            var errorHandler = irInfo_.CreateParsingErrorHandler();
            return (irInfo_.CreateSectionParser(errorHandler), errorHandler);
        }

        public abstract IRTextSummary LoadDocument(ProgressInfoHandler progressHandler);
        public abstract string GetDocumentText();
        public abstract byte[] GetDocumentTextBytes();
        public abstract ParsedIRTextSection LoadSection(IRTextSection section);
        public abstract string GetSectionText(IRTextSection section);
        public abstract string GetSectionPassOutput(IRPassOutput output);
        public abstract string GetRawSectionText(IRTextSection section);
        public abstract string GetRawSectionPassOutput(IRPassOutput output);

        public ParsedIRTextSection TryGetLoadedSection(IRTextSection section) {
            if (!cacheEnabled_) {
                return null;
            }

            lock (lockObject_) {
                return sectionCache_.TryGetValue(section, out var result) ? result : null;
            }
        }

        public string GetSectionText(IRTextSection section, bool useCache = true) {
            if (useCache && cacheEnabled_) {
                lock (lockObject_) {
                    if (sectionCache_.TryGetValue(section, out var result)) {
                        return result.Text;
                    }
                }
            }

            return GetSectionText(section);
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
