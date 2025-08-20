// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace ProfileExplorerCore2.Providers;

public delegate string FunctionNameFormatter(string name);

[Flags]
public enum FunctionNameDemanglingOptions {
  Default = 0,
  OnlyName = 1 << 0,
  NoReturnType = 1 << 1,
  NoSpecialKeywords = 1 << 2
}

public interface INameProvider {
  bool IsDemanglingSupported { get; }
  bool IsDemanglingEnabled { get; }
  FunctionNameDemanglingOptions GlobalDemanglingOptions { get; }
  string GetSectionName(IRTextSection section, bool includeNumber = true);
  string GetFunctionName(IRTextFunction function);

  string DemangleFunctionName(IRTextFunction function, FunctionNameDemanglingOptions options =
                                FunctionNameDemanglingOptions.Default);

  string DemangleFunctionName(string name, FunctionNameDemanglingOptions options);
  string FormatFunctionName(IRTextFunction function);
  string FormatFunctionName(string name);

  void SettingsChanged();
  //? TODO: GetBlockName
}