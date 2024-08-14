// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProfileExplorer.Core;

namespace ProfileExplorer.UI;

public class SectionSearchResult {
  public SectionSearchResult(IRTextSection section) {
    Section = section;
  }

  public bool HasResults => Results != null && Results.Count > 0;
  public IRTextSection Section { get; set; }
  public ReadOnlyMemory<char> SectionText { get; set; }
  public List<TextSearchResult> Results { get; set; }
  public List<TextSearchResult> BeforeOutputResults { get; set; }
  public List<TextSearchResult> AfterOutputResults { get; set; }
}

public class SectionTextSearcherOptions {
  public SectionTextSearcherOptions() {
    SearchBeforeOutput = false;
    SearchAfterOutput = false;
    KeepSectionText = true;
    UseRawSectionText = false;
    MaxThreadCount = App.Settings.GeneralSettings.CurrentCpuCoreLimit;
  }

  public bool SearchBeforeOutput { get; set; }
  public bool SearchAfterOutput { get; set; }
  public bool KeepSectionText { get; set; }
  public bool UseRawSectionText { get; set; }
  public int MaxThreadCount { get; set; }
}

public class SectionTextSearcher {
  private IRTextSectionLoader sectionLoader_;
  private SectionTextSearcherOptions options_;

  public SectionTextSearcher(IRTextSectionLoader sectionLoader, SectionTextSearcherOptions options = null) {
    sectionLoader_ = sectionLoader;

    if (options != null) {
      options_ = options;
    }
    else {
      // Use default options if not set.
      options_ = new SectionTextSearcherOptions();
    }
  }

  public async Task<List<SectionSearchResult>> SearchAsync(string searchedText,
                                                           TextSearchKind searchKind,
                                                           List<IRTextSection> sections,
                                                           CancelableTask cancelableTask = null) {
    var resultList = new List<SectionSearchResult>(sections.Count);

    if (string.IsNullOrEmpty(searchedText)) {
      return resultList;
    }

    using var concurrencySemaphore = new SemaphoreSlim(options_.MaxThreadCount);
    const int BatchSize = 1024;
    int batches = sections.Count / BatchSize;
    int index = 0;

    while (index < sections.Count) {
      if (cancelableTask != null && cancelableTask.IsCanceled) {
        break;
      }

      int batchStart = index;
      int batchSize = Math.Min(BatchSize, sections.Count - batchStart);
      var tasks = new Task<SectionSearchResult>[batchSize];

      for (int i = 0; i < batchSize; i++) {
        await concurrencySemaphore.WaitAsync().ConfigureAwait(false);

        var section = sections[batchStart + i];
        tasks[i] = Task.Run(() => {
          try {
            return SearchSection(searchedText, searchKind, section, cancelableTask);
          }
          finally {
            concurrencySemaphore.Release();
          }
        });
      }

      await Task.WhenAll(tasks).ConfigureAwait(false);

      foreach (var task in tasks) {
        resultList.Add(await task);
      }

      index += batchSize;
    }

    return resultList;
  }

  public async Task<SectionSearchResult> SearchSectionAsync(string searchedText,
                                                            TextSearchKind searchKind,
                                                            IRTextSection section) {
    var input = new List<IRTextSection> {section};
    var resultList = await SearchAsync(searchedText, searchKind, input);

    if (resultList.Count > 0) {
      return resultList[0];
    }

    return new SectionSearchResult(section);
  }

  public SectionSearchResult SearchSection(string searchedText, TextSearchKind searchKind,
                                           IRTextSection section, CancelableTask cancelableTask) {
    var text = options_.UseRawSectionText ?
      sectionLoader_.GetRawSectionTextSpan(section) :
      sectionLoader_.GetSectionTextSpan(section);
    return SearchSection(text, searchedText, searchKind, section, cancelableTask);
  }

  public SectionSearchResult SearchSection(ReadOnlyMemory<char> text, string searchedText, TextSearchKind searchKind,
                                           IRTextSection section, CancelableTask cancelableTask = null) {
    var result = new SectionSearchResult(section) {
      SectionText = options_.KeepSectionText ? text : ReadOnlyMemory<char>.Empty,
      Results = TextSearcher.AllIndexesOf(text, searchedText, 0, searchKind, cancelableTask)
    };

    if (cancelableTask != null && cancelableTask.IsCanceled) {
      return result;
    }

    if (options_.SearchBeforeOutput) {
      var beforeText = options_.UseRawSectionText ?
        sectionLoader_.GetRawSectionPassOutputSpan(section.OutputBefore) :
        sectionLoader_.GetSectionPassOutputTextSpan(section.OutputBefore);

      if (beforeText.Length > 0) {
        result.BeforeOutputResults = TextSearcher.AllIndexesOf(beforeText, searchedText, 0, searchKind, cancelableTask);
      }
    }

    if (options_.SearchAfterOutput) {
      var afterText = options_.UseRawSectionText ?
        sectionLoader_.GetRawSectionPassOutputSpan(section.OutputAfter) :
        sectionLoader_.GetSectionPassOutputTextSpan(section.OutputAfter);

      if (afterText.Length > 0) {
        result.BeforeOutputResults = TextSearcher.AllIndexesOf(afterText, searchedText, 0, searchKind, cancelableTask);
      }
    }

    return result;
  }

  public async Task<SectionSearchResult> SearchSectionWithTextAsync(string text, string searchedText,
                                                                    TextSearchKind searchKind,
                                                                    IRTextSection section,
                                                                    CancelableTask cancelableTask = null) {
    var result = await Task.Run(() => TextSearcher.AllIndexesOf(text, searchedText, 0, searchKind, cancelableTask));

    return new SectionSearchResult(section) {
      SectionText = options_.KeepSectionText ? text.AsMemory() : ReadOnlyMemory<char>.Empty,
      Results = result
    };
  }
}