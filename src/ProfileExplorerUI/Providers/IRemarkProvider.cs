// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Utilities;
using ProfileExplorerUI.Session;

namespace ProfileExplorer.UI;

public interface IRRemarkProvider {
  // TODO: add per-section cache of remarks, don't have to reparse
  // all output when switching sections.
  string SettingsFilePath { get; }
  List<RemarkCategory> RemarkCategories { get; }
  List<RemarkSectionBoundary> RemarkSectionBoundaries { get; }
  List<RemarkTextHighlighting> RemarkTextHighlighting { get; }
  bool SaveSettings();
  bool LoadSettings();
  List<IRTextSection> GetSectionList(IRTextSection currentSection, int maxDepth, bool stopAtSectionBoundaries);

  List<Remark> ExtractAllRemarks(List<IRTextSection> sections, FunctionIR function, ILoadedDocument document,
                                 RemarkProviderOptions options, CancelableTask cancelableTask);

  List<Remark> ExtractRemarks(string text, FunctionIR function,
                              IRTextSection section, RemarkProviderOptions options,
                              CancelableTask cancelableTask);

  List<Remark> ExtractRemarks(List<string> textLines, FunctionIR function,
                              IRTextSection section, RemarkProviderOptions options,
                              CancelableTask cancelableTask);

  OptimizationRemark GetOptimizationRemarkInfo(Remark remark);
}

public class OptimizationRemark {
  public string OptimizationName { get; set; }
  public object Info { get; set; }
}

public class RemarkProviderOptions {
  public RemarkProviderOptions() {
    FindInstructionRemarks = true;
    FindOperandRemarks = true;
    IgnoreOverlappingOperandRemarks = false;
  }

  public bool FindInstructionRemarks { get; set; }
  public bool FindOperandRemarks { get; set; }
  public bool IgnoreOverlappingOperandRemarks { get; set; }

  //? TODO: Options for multi-threading, max cores
}