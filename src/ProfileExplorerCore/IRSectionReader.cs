// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace ProfileExplorerCore;

public delegate void ProgressInfoHandler(IRSectionReader reader, SectionReaderProgressInfo info);
public delegate void SectionTextHandler(IRSectionReader reader, SectionReaderText info);

public interface IRSectionReader {
  IRTextSummary GenerateSummary(ProgressInfoHandler progressHandler,
                                SectionTextHandler sectionTextHandler = null);

  string GetSectionText(IRTextSection section);
  ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section);
  List<string> GetSectionTextLines(IRTextSection section);
  string GetPassOutputText(IRPassOutput output);
  ReadOnlyMemory<char> GetPassOutputTextSpan(IRPassOutput output);
  List<string> GetPassOutputTextLines(IRPassOutput output);
  string GetRawSectionText(IRTextSection section);
  ReadOnlyMemory<char> GetRawSectionTextSpan(IRTextSection section);
  string GetRawPassOutputText(IRPassOutput output);
  ReadOnlyMemory<char> GetRawPassOutputTextSpan(IRPassOutput output);
  public byte[] GetDocumentTextData();
  void Dispose();
}

public class SectionReaderProgressInfo {
  public SectionReaderProgressInfo(long bytesProcessed, long totalBytes) {
    BytesProcessed = bytesProcessed;
    TotalBytes = totalBytes;
  }

  public SectionReaderProgressInfo(bool working) {
    IsIndeterminate = working;
  }

  public long BytesProcessed { get; set; }
  public long TotalBytes { get; set; }
  public bool IsIndeterminate { get; set; }
}

public class SectionReaderText {
  public SectionReaderText(IRPassOutput output, List<string> textLines) {
    Output = output;
    TextLines = textLines;
  }

  public IRPassOutput Output { get; set; }
  public List<string> TextLines { get; set; }
}