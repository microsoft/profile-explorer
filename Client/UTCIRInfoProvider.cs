// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Client {
    public class UTCIRInfoProvider : ICompilerIRInfoProvider {
        UTCStyleProvider styles_;
        UTCNameProvider names_;
        UTCRemarkProvider remarks_;

        public UTCIRInfoProvider() {
            styles_ = new UTCStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new UTCRemarkProvider();
        }

        public string CompilerIRName => "UTC";
        public INameProvider NameProvider => names_;
        public IStyleProvider StyleProvider => styles_;
        public IRRemarkProvider RemarkProvider => remarks_;

    }
}
