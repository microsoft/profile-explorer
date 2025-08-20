// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using ProfileExplorerCore2.IR;
using ProfileExplorerCore2.IR.Tags;
using ProfileExplorerCore2.Providers;
using ProfileExplorerCore2.Settings;
using ProfileExplorerCore2.Utilities;

namespace ProfileExplorerCore2.Binary;

public class JsonDebugInfoProvider : IDebugInfoProvider {
  private Dictionary<string, FunctionDebugInfo> functionMap_;
  private List<FunctionDebugInfo> functions_;
  public Machine? Architecture => null;
  public SymbolFileSourceSettings SymbolSettings { get; set; }

  public bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc) {
    return AnnotateSourceLocations(function, textFunc.Name);
  }

  public bool AnnotateSourceLocations(FunctionIR function,
                                      FunctionDebugInfo funcInfo) {
    var metadataTag = function.GetTag<AssemblyMetadataTag>();

    if (metadataTag == null) {
      return false;
    }

    if (!funcInfo.HasSourceLines) {
      return false;
    }

    foreach (var pair in metadataTag.OffsetToElementMap) {
      var lineInfo = funcInfo.FindNearestLine(pair.Key);

      if (!lineInfo.IsUnknown) {
        var locationTag = pair.Value.GetOrAddTag<SourceLocationTag>();
        locationTag.Reset(); // Tag may be already populated.
        locationTag.Line = lineInfo.Line;
        locationTag.Column = lineInfo.Column;
      }
    }

    return true;
  }

  public FunctionDebugInfo FindFunction(string functionName) {
    return functionMap_.GetValueOr(functionName, FunctionDebugInfo.Unknown);
  }

  public IEnumerable<FunctionDebugInfo> EnumerateFunctions() {
    return functions_;
  }

  public List<FunctionDebugInfo> GetSortedFunctions() {
    return functions_;
  }

  public FunctionDebugInfo FindFunctionByRVA(long rva) {
    return FunctionDebugInfo.BinarySearch(functions_, rva);
  }

  public SourceFileDebugInfo FindFunctionSourceFilePath(IRTextFunction textFunc) {
    return FindFunctionSourceFilePath(textFunc.Name);
  }

  public SourceFileDebugInfo FindFunctionSourceFilePath(string functionName) {
    if (functionMap_.TryGetValue(functionName, out var funcInfo)) {
      return GetSourceFileInfo(funcInfo);
    }

    return SourceFileDebugInfo.Unknown;
  }

  public SourceFileDebugInfo FindSourceFilePathByRVA(long rva) {
    var funcInfo = FindFunctionByRVA(rva);

    if (funcInfo != null && funcInfo.HasSourceLines) {
      return GetSourceFileInfo(funcInfo);
    }

    return SourceFileDebugInfo.Unknown;
  }

  public SourceLineDebugInfo FindSourceLineByRVA(long rva, bool includeInlinees) {
    var funcInfo = FindFunctionByRVA(rva);

    if (funcInfo != null && funcInfo.HasSourceLines) {
      long offset = rva - funcInfo.StartRVA;
      return funcInfo.FindNearestLine(offset);
    }

    return SourceLineDebugInfo.Unknown;
  }

  public bool LoadDebugInfo(DebugFileSearchResult debugFile, IDebugInfoProvider other = null) {
    if (!debugFile.Found) {
      return false;
    }

    return LoadDebugInfo(debugFile);
  }

  public bool PopulateSourceLines(FunctionDebugInfo funcInfo) {
    return true;
  }

  public void Unload() {
  }

  public void Dispose() {
  }

  private static SourceFileDebugInfo GetSourceFileInfo(FunctionDebugInfo info) {
    return new SourceFileDebugInfo(info.FirstSourceLine.FilePath,
                                   info.FirstSourceLine.FilePath,
                                   info.FirstSourceLine.Line);
  }

  public bool AnnotateSourceLocations(FunctionIR function, string functionName) {
    var funcInfo = FindFunction(functionName);

    if (funcInfo == null) {
      return false;
    }

    return AnnotateSourceLocations(function, funcInfo);
  }

  public bool LoadDebugInfo(string debugFilePath, IDebugInfoProvider other = null) {
    if (!JsonUtils.DeserializeFromFile(debugFilePath, out functions_)) {
      return false;
    }

    functions_.Sort();
    functionMap_ = new Dictionary<string, FunctionDebugInfo>(functions_.Count);

    foreach (var func in functions_) {
      functionMap_[func.Name] = func;
    }

    return true;
  }
}