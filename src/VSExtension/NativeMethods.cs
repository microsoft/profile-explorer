﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Runtime.InteropServices;
using System.Text;

namespace ProfileExplorerExtension;

static class NativeMethods {
  private const int MAX_PATH = 260;

  [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
  private static extern bool PathFindOnPath([In][Out] StringBuilder pszFile,
                                            [In] string[] ppszOtherDirs);

  public static string GetFullPathFromWindows(string exeName) {
    var sb = new StringBuilder(exeName, MAX_PATH);
    return PathFindOnPath(sb, null) ? sb.ToString() : null;
  }
}