using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;

namespace IRExplorerUI.Document;

public class SourceFileFinder {
  private SourceFileMapper sourceFileMapper_;
  private HashSet<string> disabledSourceMappings_;
  private ISession session_;

  public SourceFileFinder(ISession session) {
    session_ = session;
    LoadSettings();
  }

  private void LoadSettings() {
    var settings = App.Settings.SourceFileSettings.FinderSettings;
    disabledSourceMappings_ = settings.DisabledSourceMappings.CloneHashSet();
    sourceFileMapper_ = new SourceFileMapper(settings.SourceMappings.CloneDictionary());
  }

  public void SaveSettings() {
    var settings = App.Settings.SourceFileSettings.FinderSettings;
    settings.SourceMappings = sourceFileMapper_.SourceMap.CloneDictionary();
    settings.DisabledSourceMappings = disabledSourceMappings_.CloneHashSet();
  }

  public async Task<(SourceFileDebugInfo, IDebugInfoProvider)>
    FindLocalSourceFile(IRTextFunction function, FrameworkElement owner = null) {
    var debugInfo = await session_.GetDebugInfoProvider(function);

    if (debugInfo == null) {
      return (SourceFileDebugInfo.Unknown, null);
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
      // Check if the file can be found. If it's from another machine,
      // a mapping is done after the user is asked to pick the new location of the file.
      if (File.Exists(sourceInfo.FilePath)) {
        return (sourceInfo, debugInfo);
      }
      else if (!disabledSourceMappings_.Contains(sourceInfo.FilePath)) {
        var filePath = sourceFileMapper_.Map(sourceInfo.FilePath, () =>
                                               Utils.ShowOpenFileDialog(
                                                 $"Source File|{Utils.TryGetFileName(sourceInfo.OriginalFilePath)}",
                                                 null, $"Open {sourceInfo.OriginalFilePath}"));
        if (!string.IsNullOrEmpty(filePath)) {
          sourceInfo.FilePath = filePath;
        }
        else if (Utils.ShowYesNoMessageBox("Continue asking for the location of this source file?", owner) ==
                 MessageBoxResult.No) {
          disabledSourceMappings_.Add(sourceInfo.FilePath);
        }

        SaveSettings();
        return (sourceInfo, debugInfo);
      }
    }

    return (SourceFileDebugInfo.Unknown, null);
  }

  public void Reset() {
    //? TODO: Should clear only the current file
    disabledSourceMappings_.Clear();
    sourceFileMapper_.Reset();
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