// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Concurrent;
using IRExplorerCore;

namespace IRExplorerUI.Compilers.ASM;

public sealed class ASMNameProvider : INameProvider {
  private static ConcurrentDictionary<string, string> demangledNameMap_;
  private static ConcurrentDictionary<string, string> functionNameMap_;

  static ASMNameProvider() {
    demangledNameMap_ = new ConcurrentDictionary<string, string>();
    functionNameMap_ = new ConcurrentDictionary<string, string>();
  }

  public bool IsDemanglingSupported => true;
  public bool IsDemanglingEnabled => IsDemanglingSupported && App.Settings.SectionSettings.ShowDemangledNames;
  public FunctionNameDemanglingOptions GlobalDemanglingOptions => App.Settings.SectionSettings.DemanglingOptions;

  public string GetSectionName(IRTextSection section, bool includeNumber) {
    string sectionName = section.Name;

    if (string.IsNullOrEmpty(sectionName)) {
      sectionName = section.ParentFunction.Name;
    }

    if (!string.IsNullOrEmpty(sectionName)) {
      sectionName = FormatFunctionName(sectionName);
      sectionName = sectionName.Length <= 50 ? sectionName : $"{sectionName.Substring(0, 20)}...";
    }

    if (includeNumber) {
      return $"({section.Number}) {sectionName}";
    }

    return sectionName;
  }

  public string GetFunctionName(IRTextFunction function) {
    return function.Name;
  }

  public string DemangleFunctionName(string name, FunctionNameDemanglingOptions options) {
    if (!demangledNameMap_.TryGetValue(name, out string demangledName)) {
      demangledName = PDBDebugInfoProvider.DemangleFunctionName(name, options);
      demangledNameMap_.TryAdd(name, demangledName);
    }

    return demangledName;
  }

  public string DemangleFunctionName(IRTextFunction function, FunctionNameDemanglingOptions options) {
    return DemangleFunctionName(function.Name, options);
  }

  public string FormatFunctionName(string name) {
    if (!IsDemanglingEnabled) {
      return name;
    }

    if (!functionNameMap_.TryGetValue(name, out string demangledName)) {
      demangledName = PDBDebugInfoProvider.DemangleFunctionName(name, FunctionNameDemanglingOptions.OnlyName);
      functionNameMap_.TryAdd(name, demangledName);
    }

    return demangledName;
  }

  public string FormatFunctionName(IRTextFunction function) {
    return FormatFunctionName(function.Name);
  }
}
