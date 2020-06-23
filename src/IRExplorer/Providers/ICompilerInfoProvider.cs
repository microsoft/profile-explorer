// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CoreLib;

namespace Client {
    public interface ICompilerInfoProvider {
        string CompilerIRName { get; }

        ICompilerIRInfo IR { get; }
        INameProvider NameProvider { get; }
        ISectionStyleProvider StyleProvider { get; }
        IRRemarkProvider RemarkProvider { get; }
    }
}
