using System.Collections.Generic;
using System.IO;
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
    sourceFileMapper_ = new SourceFileMapper();
    disabledSourceMappings_ = new HashSet<string>();
    //? TODO: Load sessings
  }

  public (SourceFileDebugInfo, IDebugInfoProvider) FindLocalSourceFile(IRTextFunction function, FrameworkElement owner = null) {
    var loadedDoc = session_.SessionState.FindLoadedDocument(function);
    var debugInfo = GetDebugInfo(loadedDoc);

    if (debugInfo != null) {
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

          return (sourceInfo, debugInfo);
        }
      }
    }

    return (SourceFileDebugInfo.Unknown, null);
  }

  public void Reset() {
    //? TODO: Should clear only the current file
    disabledSourceMappings_.Clear();
    sourceFileMapper_.Reset();
  }
  
  private IDebugInfoProvider GetDebugInfo(LoadedDocument loadedDoc) {
    if (loadedDoc.DebugInfo != null) {
      // Used for managed binaries, where the debug info is constructed during profiling.
      return loadedDoc.DebugInfo;
    }

    if (loadedDoc.DebugInfoFileExists) {
      var debugInfo = session_.CompilerInfo.CreateDebugInfoProvider(loadedDoc.BinaryFile.FilePath);

      if (debugInfo.LoadDebugInfo(loadedDoc.DebugInfoFile)) {
        return debugInfo;
      }
    }

    return null;
  }
  
  private SourceFileDebugInfo LocateSourceFile(FunctionProfileData funcProfile,
                                               IDebugInfoProvider debugInfo) {
    // Lookup function by RVA, more precise.
    if (funcProfile.FunctionDebugInfo != null) {
      return debugInfo.FindSourceFilePathByRVA(funcProfile.FunctionDebugInfo.RVA);
    }
    
    return SourceFileDebugInfo.Unknown;
  }

  private string BrowseSourceFile() {
    return Utils.ShowOpenFileDialog(
      "C/C++ source files|*.c;*.cpp;*.cc;*.cxx;*.h;*.hpp;*.hxx;*.hh|.NET source files|*.cs;*.vb|All Files|*.*");
  }
}
