// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using IRExplorerCore;
using IRExplorerUI.Compilers;

namespace IRExplorerUI.Compilers.ASM; 

public sealed class ASMNameProvider : INameProvider {
    private static ConcurrentDictionary<string, string> demangledNameMap_;

    static ASMNameProvider() {
        demangledNameMap_ = new ConcurrentDictionary<string, string>();
    }

    public bool IsDemanglingSupported => true;
    public bool IsDemanglingEnabled => IsDemanglingSupported && App.Settings.SectionSettings.ShowDemangledNames;
    public FunctionNameDemanglingOptions GlobalDemanglingOptions => App.Settings.SectionSettings.DemanglingOptions;

    public string GetSectionName(IRTextSection section, bool includeNumber) {
        string sectionName = section.Name;

        if (string.IsNullOrEmpty(sectionName)) {
            var funcName = section.ParentFunction.Name;
            if (!string.IsNullOrEmpty(funcName)) {
                return funcName.Length <= 24 ? funcName : $"{funcName.Substring(0, 24)}...";
            }

            return "<UNTITLED>";
        }
            
        if(includeNumber) {
            return $"({section.Number}) {sectionName}";
        }

        return sectionName;
    }

    public string GetFunctionName(IRTextFunction function) {
        return function.Name;
    }

    public string DemangleFunctionName(string name, FunctionNameDemanglingOptions options) {
        if (!demangledNameMap_.TryGetValue(name, out var demangledName)) {
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

        return DemangleFunctionName(name, GlobalDemanglingOptions);
    }

    public string FormatFunctionName(IRTextFunction function) {
        return FormatFunctionName(function.Name);
    }
}
