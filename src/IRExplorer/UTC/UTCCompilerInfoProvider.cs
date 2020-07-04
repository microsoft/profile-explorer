// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;
using IRExplorerCore.UTC;

namespace IRExplorer {
    public class UTCCompilerInfoProvider : ICompilerInfoProvider {
        private UTCCompilerIRInfo ir_;
        private UTCNameProvider names_;
        private UTCRemarkProvider remarks_;
        private UTCSectionStyleProvider styles_;

        public UTCCompilerInfoProvider() {
            ir_ = new UTCCompilerIRInfo();
            styles_ = new UTCSectionStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new UTCRemarkProvider();
        }

        public string CompilerIRName => "UTC";
        public ICompilerIRInfo IR => ir_;
        public INameProvider NameProvider => names_;
        public ISectionStyleProvider SectionStyleProvider => styles_;
        public IRRemarkProvider RemarkProvider => remarks_;
    }
}
