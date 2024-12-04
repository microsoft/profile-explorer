// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading.Tasks;
using System.Windows;
using ProfileExplorer.Core;
using ProfileExplorer.UI.Binary;
using ProfileExplorer.UI.Profile;

namespace ProfileExplorer.UI.Document;

public class SourceFileFinder {
  public enum FailureReason {
    None,
    DebugInfoNotFound,
    FileNotFound,
    MappingDisabled,
    MappingCanceled
  }

  private SourceFileMapper sourceFileMapper_;
  private List<string> disabledSourceMappings_;
  private ISession session_;
  private bool disableOpenDialog_;

  public SourceFileFinder(ISession session) {
    session_ = session;
  }

  public void LoadSettings(SourceFileFinderSettings settings) {
    disabledSourceMappings_ = settings.DisabledSourceMappings;
    sourceFileMapper_ = new SourceFileMapper(settings.SourceMappings.CloneDictionary());
  }

  public void SaveSettings(SourceFileFinderSettings settings) {
    settings.SourceMappings = sourceFileMapper_.SourceMap.CloneDictionary();
    settings.DisabledSourceMappings = disabledSourceMappings_;
    App.SaveApplicationSettings();
  }

  public async Task<(SourceFileDebugInfo, FailureReason)>
    FindLocalSourceFile(SourceFileDebugInfo sourceInfo) {
    return FindLocalSourceFile(sourceInfo, null);
  }

  public async Task<(SourceFileDebugInfo, FailureReason)>
    FindLocalSourceFile(IRTextFunction function) {
    return await Task.Run(async () => {
      var debugInfo = await session_.GetDebugInfoProvider(function).ConfigureAwait(false);

      if (debugInfo == null) {
        return (SourceFileDebugInfo.Unknown, FailureReason.DebugInfoNotFound);
      }

      // First get the source file path from the debug file (this may download it
      // from a source file server, if enabled). If the file path is not found
      // on this machine, try to map it to a local path either automatically
      // or by asking the user to manually locate the source file.
      var sourceInfo = SourceFileDebugInfo.Unknown;
      var funcProfile = session_.ProfileData?.GetFunctionProfile(function);

      if (funcProfile != null) {
        // Try precise function mapping from profiling data first.
        sourceInfo = LocateSourceFile(funcProfile, debugInfo);
      }

      if (sourceInfo.IsUnknown) {
        // Try again using the function name.
        sourceInfo = debugInfo.FindFunctionSourceFilePath(function);
      }

      if (sourceInfo.HasFilePath) {
        return FindLocalSourceFile(sourceInfo, debugInfo);
      }

      return (SourceFileDebugInfo.Unknown, FailureReason.FileNotFound);
    });
  }

  private (SourceFileDebugInfo, FailureReason)
    FindLocalSourceFile(SourceFileDebugInfo sourceInfo, IDebugInfoProvider debugInfo) {
    // Check if the file can be found. If it's from another machine,
    // a mapping is done after the user is asked to pick the new location of the file.
    if (File.Exists(sourceInfo.FilePath)) {
      // This assumes that a checksum match was done already
      // and this is the right source file.
      return (sourceInfo, FailureReason.None);
    }
    else if (!IsDisabledSourceFilePath(sourceInfo.FilePath)) {
      string filePath = sourceFileMapper_.Map(sourceInfo.FilePath, () => {
        if (disableOpenDialog_) {
          return null; // Open File dialog disabled for current session.
        }

        return Utils.ShowOpenFileDialog(
          $"Source File|{Utils.TryGetFileName(sourceInfo.OriginalFilePath)}",
          null, $"Open {sourceInfo.OriginalFilePath}");
      });

      if (!string.IsNullOrEmpty(filePath)) {
        sourceInfo.FilePath = filePath;
      }
      else if (!disableOpenDialog_) {
        var result = Utils.ShowYesNoCancelMessageBox("""
                                                     Continue asking for the location of this source file?

                                                     Press Cancel to stop showing the Open File dialog during the current session for all source files that cannot be found.
                                                     """, null);
        if (result == MessageBoxResult.No ||
            result == MessageBoxResult.Cancel) {
          if (!disabledSourceMappings_.Contains(sourceInfo.FilePath)) {
            disabledSourceMappings_.Add(sourceInfo.FilePath);
          }

          if (result == MessageBoxResult.Cancel) {
            disableOpenDialog_ = true; // Stop showing open dialog for current session.
          }
        }
      }

      if (File.Exists(sourceInfo.FilePath)) {
        return (sourceInfo, FailureReason.None);
      }
      else {
        return (sourceInfo, FailureReason.MappingCanceled);
      }
    }
    else {
      return (sourceInfo, FailureReason.MappingDisabled);
    }
  }

  private bool IsDisabledSourceFilePath(string filePath) {
    foreach (string path in disabledSourceMappings_) {
      // Do a case-insensitive wildcard (*) match.
      if (path.Equals(filePath, StringComparison.OrdinalIgnoreCase) ||
          FileSystemName.MatchesSimpleExpression(path, filePath)) {
        return true;
      }
    }

    return false;
  }

  public void Reset() {
    disabledSourceMappings_.Clear();
    sourceFileMapper_.Reset();
    disableOpenDialog_ = false;
  }

  public void ResetDisabledMappings() {
    disabledSourceMappings_.Clear();
    disableOpenDialog_ = false;
  }

  public void ResetDisabledMappings(string filePath) {
    disabledSourceMappings_.Remove(filePath);
    disableOpenDialog_ = false;
  }

  private SourceFileDebugInfo LocateSourceFile(FunctionProfileData funcProfile,
                                               IDebugInfoProvider debugInfo) {
    // Lookup function by RVA, more precise.
    if (funcProfile.FunctionDebugInfo != null) {
      return debugInfo.FindSourceFilePathByRVA(funcProfile.FunctionDebugInfo.RVA);
    }

    return SourceFileDebugInfo.Unknown;
  }
}