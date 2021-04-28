// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;
using System;

namespace IRExplorerUI.Compilers {
    public static class IRModeUtilities {
        public static void SetIRModeFromSettings(ICompilerIRInfo ir) {
            // Set the IR parsing mode (target architecture)
            // based on the syntax highlighting file selected.
            var path = App.GetSyntaxHighlightingFilePath();

            if (!string.IsNullOrEmpty(path) &&
                path.Contains("arm64", StringComparison.OrdinalIgnoreCase)) {
                ir.IRMode = IRMode.ARM64;
            }
            else {
                ir.IRMode = IRMode.x86;
            }
        }
    }
}
