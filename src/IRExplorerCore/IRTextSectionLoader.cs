// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Threading;
using CSharpTest.Net.Collections;
using IRExplorerCore.IR;

namespace IRExplorerCore;

public class ParsedIRTextSection {
  public ParsedIRTextSection(IRTextSection section, ReadOnlyMemory<char> text, FunctionIR function) {
    Section = section;
    Text = text;
    Function = function;
  }

  public IRTextSection Section { get; set; }
  public ReadOnlyMemory<char> Text { get; set; }
  public FunctionIR Function { get; set; }
  public List<IRParsingError> ParsingErrors { get; set; }
  public bool IsCached { get; set; }

  public bool HadParsingErrors => ParsingErrors != null && ParsingErrors.Count > 0;

  public override string ToString() {
    return Section.ToString();
  }
}

public abstract class IRTextSectionLoader : IDisposable {
  private const int CACHE_LIMIT = 32;

  protected LurchTable<IRTextSection, ParsedIRTextSection> sectionCache_;
  protected ICompilerIRInfo irInfo_;
  protected bool cacheEnabled_;
  protected object lockObject_;
  protected long sectionPreprocessingCompleted_;

  public event EventHandler<bool> SectionPreprocessingCompleted;

  public bool SectionSignaturesComputed => Interlocked.Read(ref sectionPreprocessingCompleted_) != 0;

  public abstract IRTextSummary LoadDocument(ProgressInfoHandler progressHandler);
  public abstract string GetDocumentOutputText();
  public abstract byte[] GetDocumentTextBytes();
  public abstract ParsedIRTextSection LoadSection(IRTextSection section);
  public abstract string GetSectionText(IRTextSection section);
  public abstract ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section);
  public abstract string GetSectionOutputText(IRPassOutput output);
  public abstract ReadOnlyMemory<char> GetSectionPassOutputTextSpan(IRPassOutput output);
  public abstract List<string> GetSectionPassOutputTextLines(IRPassOutput output);
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

  protected void NotifySectionPreprocessingCompleted(bool canceled) {
    Interlocked.Exchange(ref sectionPreprocessingCompleted_, 1);
    SectionPreprocessingCompleted?.Invoke(this, canceled);
  }

  protected void Initialize(ICompilerIRInfo irInfo, bool cacheEnabled) {
    irInfo_ = irInfo;
    cacheEnabled_ = cacheEnabled;
    lockObject_ = new object();
    sectionPreprocessingCompleted_ = 0;
    sectionCache_ = new LurchTable<IRTextSection, ParsedIRTextSection>(LurchTableOrder.Insertion, CACHE_LIMIT);
  }

  protected (IRSectionParser, IRParsingErrorHandler) InitializeParser(long functionSize = 0) {
    var errorHandler = irInfo_.CreateParsingErrorHandler();
    return (irInfo_.CreateSectionParser(errorHandler, functionSize), errorHandler);
  }

  protected void CacheParsedSection(IRTextSection section, FunctionIR function, ParsedIRTextSection result) {
    lock (lockObject_) {
      if (cacheEnabled_ && function != null) {
        sectionCache_[section] = result;
      }
    }
  }

  protected ParsedIRTextSection TryGetCachedParsedSection(IRTextSection section) {
    lock (lockObject_) {
      if (cacheEnabled_ && sectionCache_.TryGetValue(section, out var cachedResult)) {
        //Trace.TraceInformation($"Section loader {ObjectTracker.Track(this)}: found in cache");
        cachedResult.IsCached = true;
        return cachedResult;
      }
    }

    return null;
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
