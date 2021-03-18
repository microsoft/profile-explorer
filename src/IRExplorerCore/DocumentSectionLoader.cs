// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore.IR;

namespace IRExplorerCore {
    public class DocumentSectionLoader : IRTextSectionLoader {
        private IRSectionReader documentReader_;
        private ConcurrentExclusiveSchedulerPair taskScheduler_;
        private TaskFactory taskFactory_;
        private CancelableTask preprocessTask_;

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
            var tasks = new List<Task>();

            var result = documentReader_.GenerateSummary(progressHandler, (reader, sectionInfo) => {
                //? TODO: Extract to be reusable?
                if (taskScheduler_ == null) {
                    taskScheduler_ = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 2);
                    taskFactory_ = new TaskFactory(taskScheduler_.ConcurrentScheduler);
                    preprocessTask_ = new CancelableTask();
                }

                tasks.Add(taskFactory_.StartNew(() => {
                    ComputeSectionSignature(sectionInfo);
                }, preprocessTask_.Token));
            });

            if (result == null) {
                preprocessTask_.Cancel();
            }
            else if(tasks.Count > 0) {
                Task.Run(() => {
                    Task.WaitAll(tasks.ToArray());
                    NotifySectionPreprocessingCompleted(preprocessTask_.IsCanceled);
                });
            }

            return result;
        }

        private void ComputeSectionSignature(SectionReaderText sectionInfo) {
            var sha = SHA256.Create();
            var lines = sectionInfo.TextLines;

            for (int i = 0; i < lines.Count - 1; i++) {
                var data = Encoding.ASCII.GetBytes(lines[i]);
                sha.TransformBlock(data, 0, data.Length, null, 0);
            }

            if (lines.Count > 0) {
                var data = Encoding.ASCII.GetBytes(lines[^1]);
                sha.TransformFinalBlock(data, 0, data.Length);
            }

            sectionInfo.Output.Signature = sha.Hash;
        }
        

        public override string GetDocumentOutputText() {
            var data = documentReader_.GetDocumentTextData();
            return Encoding.UTF8.GetString(data);
        }

        public override byte[] GetDocumentTextBytes() {
            return documentReader_.GetDocumentTextData();
        }

        public override ParsedIRTextSection LoadSection(IRTextSection section) {
            //Trace.TraceInformation(
            //    $"Section loader {ObjectTracker.Track(this)}: ({section.Number}) {section.Name}");

            lock (lockObject_) {
                if (cacheEnabled_ && sectionCache_.TryGetValue(section, out var cachedResult)) {
                    //Trace.TraceInformation($"Section loader {ObjectTracker.Track(this)}: found in cache");
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

        public override string GetSectionOutputText(IRPassOutput output) {
            if (output == null) {
                // With some documents there is no before/after text.
                return string.Empty;
            }

            return documentReader_.GetPassOutputText(output);
        }

        public override List<string> GetSectionOutputTextLines(IRPassOutput output) {
            if (output == null) {
                // With some documents there is no before/after text.
                return new List<string>();
            }

            return documentReader_.GetPassOutputTextLines(output);
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
                preprocessTask_?.Cancel();
                documentReader_ = null;
                disposed_ = true;
            }
        }
    }
}
