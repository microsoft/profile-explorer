using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Threading.Tasks;
using System.Windows;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;

namespace IRExplorerUI.Document;

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

  public SourceFileFinder(ISession session) {
    session_ = session;
    LoadSettings();
  }

  private void LoadSettings() {
    var settings = App.Settings.SourceFileSettings.FinderSettings;
    disabledSourceMappings_ = settings.DisabledSourceMappings;
    sourceFileMapper_ = new SourceFileMapper(settings.SourceMappings.CloneDictionary());
  }

  public void SaveSettings() {
    var settings = App.Settings.SourceFileSettings.FinderSettings;
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
      return (sourceInfo, FailureReason.None);
    }
    else if (!IsDisabledSourceFilePath(sourceInfo.FilePath)) {
      var filePath = sourceFileMapper_.Map(sourceInfo.FilePath, () =>
                                             Utils.ShowOpenFileDialog(
                                               $"Source File|{Utils.TryGetFileName(sourceInfo.OriginalFilePath)}",
                                               null, $"Open {sourceInfo.OriginalFilePath}"));
      if (!string.IsNullOrEmpty(filePath)) {
        sourceInfo.FilePath = filePath;
      }
      else if (Utils.ShowYesNoMessageBox("Continue asking for the location of this source file?", null) ==
               MessageBoxResult.No) {
        if (!disabledSourceMappings_.Contains(sourceInfo.FilePath)) {
          disabledSourceMappings_.Add(sourceInfo.FilePath);
        }
      }

      SaveSettings();
      
      if (File.Exists(sourceInfo.FilePath)) {
        return (sourceInfo, FailureReason.None);
      }
      else {
        return (sourceInfo, FailureReason.MappingCanceled);
      }
    }
    else {
      return  (sourceInfo, FailureReason.MappingDisabled);
    }
  }

  private bool IsDisabledSourceFilePath(string filePath) {
    foreach (var path in disabledSourceMappings_) {
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
    SaveSettings();
  }
  
  public void ResetDisabledMappings() {
    disabledSourceMappings_.Clear();
    SaveSettings();
  }
  
  public void ResetDisabledMappings(string filePath) {
    disabledSourceMappings_.Remove(filePath);
    SaveSettings();
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