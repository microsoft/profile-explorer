// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore.IR;

namespace IRExplorerCore;

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

    if (tasks.Count > 0) {
      Task.Run(() => {
        Task.WaitAll(tasks.ToArray());
        NotifySectionPreprocessingCompleted(preprocessTask_.IsCanceled);
      });
    }

    return result;
  }

  public override string GetDocumentOutputText() {
    byte[] data = documentReader_.GetDocumentTextData();
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
    FunctionIR function;

    if (sectionParser == null) {
      function = new FunctionIR(); //? TODO: Workaround for not having an LLVM parser
    }
    else {
      function = sectionParser.ParseSection(section, text);
    }

    result = new ParsedIRTextSection(section, text, function);
    CacheParsedSection(section, function, result);

    if (errorHandler.HadParsingErrors) {
      result.ParsingErrors = errorHandler.ParsingErrors;
    }

    return result;
  }

  public override string GetSectionText(IRTextSection section) {
    return documentReader_.GetSectionText(section);
  }

  public override ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section) {
    return documentReader_.GetSectionTextSpan(section);
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

  private void ComputeSectionSignature(SectionReaderText sectionInfo) {
    var sha = SHA256.Create();
    var lines = sectionInfo.TextLines;

    for (int i = 0; i < lines.Count - 1; i++) {
      byte[] data = Encoding.ASCII.GetBytes(lines[i]);
      sha.TransformBlock(data, 0, data.Length, null, 0);
    }

    if (lines.Count > 0) {
      byte[] data = Encoding.ASCII.GetBytes(lines[^1]);
      sha.TransformFinalBlock(data, 0, data.Length);
    }

    sectionInfo.Output.Signature = sha.Hash;
  }
}
