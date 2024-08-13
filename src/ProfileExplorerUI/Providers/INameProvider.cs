// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using ProfileExplorer.Core;

namespace ProfileExplorer.UI;

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