// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore.IR;

namespace IRExplorerCore {
    public sealed class DocumentSectionLoader : IRTextSectionLoader {
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
            documentReader_ = irInfo.CreateSectionReader(textData);
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
                return null;
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
            var result = TryGetCachedParsedSection(section);

            if (result != null) {
                return result;
            }

            var text = GetSectionTextSpan(section);
            var (sectionParser, errorHandler) = InitializeParser();
            FunctionIR function = null;

            if (sectionParser != null) {
                function = sectionParser.ParseSection(section, text);
            }

            if (function == null) {
                // Parsing failed, create a dummy function to avoid issues in the UI.
                function = new FunctionIR();
            }

            result = new ParsedIRTextSection(section, text, function);
            CacheParsedSection(section, function, result);

            if (errorHandler.HadParsingErrors) {
                result.ParsingErrors = errorHandler.ParsingErrors;
            }

            return result;
        }

        public override string GetSectionText(IRTextSection section) {
            return section.ModuleOutput != null ?
                documentReader_.GetPassOutputText(section.ModuleOutput) :
                documentReader_.GetSectionText(section);
        }

        public override ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section) {
            return section.ModuleOutput != null ?
                documentReader_.GetPassOutputTextSpan(section.ModuleOutput) :
                documentReader_.GetSectionTextSpan(section);
        }

        public override string GetSectionOutputText(IRPassOutput output) {
            if (output == null) {
                // With some documents there is no before/after text.
                return string.Empty;
            }

            return documentReader_.GetPassOutputText(output);
        }

        public override ReadOnlyMemory<char> GetSectionPassOutputTextSpan(IRPassOutput output) {
            if (output == null) {
                // With some documents there is no before/after text.
                return ReadOnlyMemory<char>.Empty;
            }

            return documentReader_.GetPassOutputTextSpan(output);
        }

        public override List<string> GetSectionPassOutputTextLines(IRPassOutput output) {
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

        public override ReadOnlyMemory<char> GetRawSectionTextSpan(IRTextSection section) {
            return documentReader_.GetRawSectionTextSpan(section);
        }

        public override ReadOnlyMemory<char> GetRawSectionPassOutputSpan(IRPassOutput output) {
            return documentReader_.GetRawPassOutputTextSpan(output);
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