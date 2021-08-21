﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using IRExplorerCore;

namespace IRExplorerUI {
    public class DebugSectionLoader : IRTextSectionLoader {
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

        public override string GetDocumentOutputText() {
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
            return Encoding.UTF8.GetBytes(GetDocumentOutputText());
        }

        public void AddSection(IRTextSection section, string text) {
            //? TODO: Compress on another thread
            Trace.TraceInformation($"Adding section {section.Name}, length {text.Length}");
            sectionTextMap_[section] = new CompressedString(text);
            lastSection_ = section;
            lastSectionText_ = text;
        }

        public override ParsedIRTextSection LoadSection(IRTextSection section) {
            lock (lockObject_) {
                Trace.TraceInformation(
                    $"Debug section loader {ObjectTracker.Track(this)}: ({section.Number}) {section.Name}");

                if (cacheEnabled_ && sectionCache_.TryGetValue(section, out var result)) {
                    Trace.TraceInformation(
                        $"Debug section loader {ObjectTracker.Track(this)}: found in cache");

                    return result;
                }

                var (sectionParser, errorHandler) = InitializeParser();
                var text = GetSectionTextSpan(section);
                var function = sectionParser.ParseSection(section, text);
                result = new ParsedIRTextSection(section, text, function);

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

        public override ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section) {
            if (section == lastSection_) {
                return lastSectionText_.AsMemory();
            }

            return sectionTextMap_[section].ToString().AsMemory();
        }

        public override string GetSectionOutputText(IRPassOutput output) {
            return "";
        }

        public override ReadOnlyMemory<char> GetSectionOutputTextSpan(IRPassOutput output) {
        return ReadOnlyMemory<char>.Empty;
        }

        public override List<string> GetSectionOutputTextLines(IRPassOutput output) {
            return new List<string>();
        }

        public override string GetRawSectionText(IRTextSection section) {
            return GetSectionText(section);
        }

        public override string GetRawSectionPassOutput(IRPassOutput output) {
            return "";
        }

        public override ReadOnlyMemory<char> GetRawSectionTextSpan(IRTextSection section) {
            return GetSectionTextSpan(section);
        }

        public override ReadOnlyMemory<char> GetRawSectionPassOutputSpan(IRPassOutput output) {
            return ReadOnlyMemory<char>.Empty;
        }

        protected override void Dispose(bool disposing) {
            if (!disposed_) {
                disposed_ = true;
            }
        }
    }
}
