// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using IRExplorerCore;

namespace IRExplorerUI {
    [Flags]
    public enum FunctionNameDemanglingOptions {
        Default = 0,
        OnlyName = 1 << 0,
        NoReturnType = 1 << 1,
        NoSpecialKeywords = 1 << 2,
    }

    public interface INameProvider {
        bool IsDemanglingSupported { get; }
        FunctionNameDemanglingOptions GlobalDemanglingOptions { get; }
        string GetSectionName(IRTextSection section, bool includeNumber = true);
        string GetFunctionName(IRTextFunction function);
        string DemangleFunctionName(IRTextFunction function, FunctionNameDemanglingOptions options);
        string DemangleFunctionName(string name, FunctionNameDemanglingOptions options);

        // GetBlockName
    }
}
