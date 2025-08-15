// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
namespace ProfileExplorer.Core.Utilities;

public static class Utils {
  public static string TryGetFileName(string path) {
    try {
      if (string.IsNullOrEmpty(path)) {
        return "";
      }

      return Path.GetFileName(path);
    }
    catch (Exception ex) {
      return "";
    }
  }
}