// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Diff;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProtoBuf;

namespace ProfileExplorer.Core.Providers;

public interface ICompilerInfoProvider {
  string CompilerIRName { get; }
  string CompilerDisplayName { get; }
  string DefaultSyntaxHighlightingFile { get; }
  ICompilerIRInfo IR { get; }
  INameProvider NameProvider { get; }
  IBinaryFileFinder BinaryFileFinder { get; }
  IDebugFileFinder DebugFileFinder { get; }
  IDebugInfoProviderFactory DebugInfoProviderFactory { get; }
  string OpenFileFilter { get; }
  string OpenDebugFileFilter { get; }
  Task ReloadSettings();

  Task<bool> AnalyzeLoadedFunction(FunctionIR function, IRTextSection section,
                                   ILoadedDocument loadedDoc, FunctionDebugInfo funcDebugInfo = null);

  IDiffInputFilter CreateDiffInputFilter();
  IDiffOutputFilter CreateDiffOutputFilter();
}

[ProtoContract]
public class BinaryFileSearchResult {
  public static BinaryFileSearchResult None = new();
  [ProtoMember(1)]
  public bool Found { get; set; }
  [ProtoMember(2)]
  public BinaryFileDescriptor BinaryFile { get; set; }
  [ProtoMember(3)]
  public string FilePath { get; set; }
  [ProtoMember(4)]
  public string Details { get; set; }

  public static BinaryFileSearchResult Success(BinaryFileDescriptor file, string filePath, string details = null) {
    return new BinaryFileSearchResult {Found = true, BinaryFile = file, FilePath = filePath, Details = details};
  }

  public static BinaryFileSearchResult Success(string filePath) {
    if (File.Exists(filePath)) {
      var info = PEBinaryInfoProvider.GetBinaryFileInfo(filePath);
      return new BinaryFileSearchResult {Found = true, BinaryFile = info, FilePath = filePath};
    }

    return new BinaryFileSearchResult {Found = false, BinaryFile = null, FilePath = filePath};
  }

  public static BinaryFileSearchResult Failure(BinaryFileDescriptor file, string details) {
    return new BinaryFileSearchResult {BinaryFile = file, Details = details};
  }

  public override string ToString() {
    return FilePath;
  }
}

[ProtoContract]
public class DebugFileSearchResult {
  public static DebugFileSearchResult None = new();
  [ProtoMember(1)]
  public bool Found { get; set; }
  [ProtoMember(2)]
  public SymbolFileDescriptor SymbolFile { get; set; }
  [ProtoMember(3)]
  public string FilePath { get; set; }
  [ProtoMember(4)]
  public string Details { get; set; }

  public static DebugFileSearchResult Success(SymbolFileDescriptor symbolFile, string filePath, string details = null) {
    return new DebugFileSearchResult {Found = true, SymbolFile = symbolFile, FilePath = filePath, Details = details};
  }

  public static DebugFileSearchResult Success(string filePath) {
    return Success(new SymbolFileDescriptor(Path.GetFileName(filePath)), filePath);
  }

  public static DebugFileSearchResult Failure(SymbolFileDescriptor symbolFile, string details) {
    return new DebugFileSearchResult {SymbolFile = symbolFile, Details = details};
  }

  public override string ToString() {
    return FilePath;
  }
}