// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace IRExplorerCore {
    public class SectionReaderProgressInfo {
        public SectionReaderProgressInfo(long bytesProcessed, long totalBytes) {
            BytesProcessed = bytesProcessed;
            TotalBytes = totalBytes;
        }

        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
    }

    public delegate void ProgressInfoHandler(IRSectionReader reader, SectionReaderProgressInfo info);

    public interface IRSectionReader {
        IRTextSummary GenerateSummary(ProgressInfoHandler progressHandler);
        string GetSectionText(IRTextSection section);
        List<string> GetSectionTextLines(IRTextSection section);
        string GetPassOutputText(IRPassOutput output);
        List<string> GetPassOutputTextLines(IRPassOutput output);
        string GetRawSectionText(IRTextSection section);
        string GetRawPassOutputText(IRPassOutput output);
        public byte[] GetDocumentTextData();
        void Dispose();
    }
}
