// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Client {
    public interface ICompilerIRInfoProvider {
        string CompilerIRName { get; }
        INameProvider NameProvider { get; }
        IStyleProvider StyleProvider { get; }
        IRRemarkProvider RemarkProvider { get; }
    }
}
