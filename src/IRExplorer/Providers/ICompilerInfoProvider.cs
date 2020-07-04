// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;

namespace IRExplorer {
    public interface ICompilerInfoProvider {
        string CompilerIRName { get; }

        ICompilerIRInfo IR { get; }
        INameProvider NameProvider { get; }
        ISectionStyleProvider SectionStyleProvider { get; }
        IRRemarkProvider RemarkProvider { get; }
    }
}
