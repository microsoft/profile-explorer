// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IRExplorerUI {
    internal static class NativeMethods {
        private const int MAX_PATH = 260;

        public static string GetFullPathFromWindows(string exeName) {
            var sb = new StringBuilder(exeName, MAX_PATH);
            return PathFindOnPath(sb, null) ? sb.ToString() : null;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern bool PathFindOnPath([In][Out] StringBuilder pszFile,
                                                  [In] string[] ppszOtherDirs);
    }
}
