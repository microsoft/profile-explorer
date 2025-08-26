// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ProfileExplorerCore.Binary;
using ProfileExplorerCore.Diff;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.Session;
using ProfileExplorerCore.Settings;
using ProtoBuf;

namespace ProfileExplorerCore.Providers;

public interface ICompilerInfoProvider {
  string CompilerIRName { get; }
  string CompilerDisplayName { get; }
  string DefaultSyntaxHighlightingFile { get; }
  ISession Session { get; }
  ICompilerIRInfo IR { get; }
  INameProvider NameProvider { get; }
  string OpenFileFilter { get; }
  string OpenDebugFileFilter { get; }
  Task ReloadSettings();

  Task<bool> AnalyzeLoadedFunction(FunctionIR function, IRTextSection section,
                                   FunctionDebugInfo funcDebugInfo = null);

  IDiffInputFilter CreateDiffInputFilter();
  IDiffOutputFilter CreateDiffOutputFilter();
  IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile);
  Task<IDebugInfoProvider> GetOrCreateDebugInfoProvider(IRTextFunction function);
  DebugFileSearchResult FindDebugInfoFile(string imagePath, SymbolFileSourceSettings settings = null);
  DebugFileSearchResult FindDebugInfoFile(SymbolFileDescriptor symbolFile, SymbolFileSourceSettings settings = null);
  Task<DebugFileSearchResult> FindDebugInfoFileAsync(string imagePath, SymbolFileSourceSettings settings = null);

  Task<DebugFileSearchResult> FindDebugInfoFileAsync(SymbolFileDescriptor symbolFile,
                                                     SymbolFileSourceSettings settings = null);

  Task<BinaryFileSearchResult> FindBinaryFileAsync(BinaryFileDescriptor binaryFile,
                                                   SymbolFileSourceSettings settings = null);
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