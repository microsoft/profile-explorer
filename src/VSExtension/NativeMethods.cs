// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Runtime.InteropServices;
using System.Text;

namespace ProfileExplorerExtension;

static class NativeMethods {
  [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
  private static extern bool PathFindOnPath([In][Out] StringBuilder pszFile,
                                            [In] string[] ppszOtherDirs);

  private const int MAX_PATH = 260;

  public static string GetFullPathFromWindows(string exeName) {
    var sb = new StringBuilder(exeName, MAX_PATH);
    return PathFindOnPath(sb, null) ? sb.ToString() : null;
  }
}