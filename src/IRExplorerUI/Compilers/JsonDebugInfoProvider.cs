// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;

namespace IRExplorerUI.Compilers;

public class JsonDebugInfoProvider : IDebugInfoProvider {
  private Dictionary<string, FunctionDebugInfo> functionMap_;
  private List<FunctionDebugInfo> functions_;
  public Machine? Architecture => null;
  public SymbolFileSourceOptions SymbolOptions { get; set; }

  private static SourceFileDebugInfo GetSourceFileInfo(FunctionDebugInfo info) {
    return new SourceFileDebugInfo(info.StartSourceLine.FilePath,
                                   info.StartSourceLine.FilePath,
                                   info.StartSourceLine.Line);
  }

  public bool AnnotateSourceLocations(FunctionIR function, IRTextFunction textFunc) {
    return AnnotateSourceLocations(function, textFunc.Name);
  }

  public bool AnnotateSourceLocations(FunctionIR function, string functionName) {
    var metadataTag = function.GetTag<AssemblyMetadataTag>();

    if (metadataTag == null) {
      return false;
    }

    var funcInfo = FindFunction(functionName);

    if (funcInfo == null || !funcInfo.HasSourceLines) {
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

  public SourceLineDebugInfo FindSourceLineByRVA(long rva) {
    var funcInfo = FindFunctionByRVA(rva);

    if (funcInfo != null && funcInfo.HasSourceLines) {
      long offset = rva - funcInfo.StartRVA;
      return funcInfo.FindNearestLine(offset);
    }

    return SourceLineDebugInfo.Unknown;
  }

  public bool LoadDebugInfo(string debugFilePath) {
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

  public bool LoadDebugInfo(DebugFileSearchResult debugFile) {
    if (!debugFile.Found) {
      return false;
    }

    return LoadDebugInfo(debugFile.FilePath);
  }

  public bool PopulateSourceLines(FunctionDebugInfo funcInfo) {
    return true;
  }

  public void Unload() {
  }

  public void Dispose() {
  }
}
