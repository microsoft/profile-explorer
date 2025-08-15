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
  private static readonly TaskFactory TaskFactoryInstance = new(CancellationToken.None,
                                                                TaskCreationOptions.None,
                                                                TaskContinuationOptions.None,
                                                                TaskScheduler.Default);

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

  public static string TryGetDirectoryName(string path) {
    try {
      if (string.IsNullOrEmpty(path)) {
        return "";
      }

      if (!Directory.Exists(path)) {
        path = Path.GetDirectoryName(path);
      }

      // Remove \ at the end.
      if (path.EndsWith(Path.DirectorySeparatorChar) ||
          path.EndsWith(Path.AltDirectorySeparatorChar)) {
        path = path.Substring(0, path.Length - 1);
      }

      return path;
    }
    catch (Exception ex) {
      return "";
    }
  }

  public static bool IsBinaryFile(string filePath) {
    return FileHasExtension(filePath, ".exe") ||
           FileHasExtension(filePath, ".dll") ||
           FileHasExtension(filePath, ".sys");
  }

  public static bool IsExecutableFile(string filePath) {
    return FileHasExtension(filePath, ".exe");
  }

  public static bool FileHasExtension(string filePath, string extension) {
    if (string.IsNullOrEmpty(filePath)) {
      return false;
    }

    try {
      extension = extension.ToLowerInvariant();

      if (!extension.StartsWith(".")) {
        extension = $".{extension}";
      }

      return Path.GetExtension(filePath).ToLowerInvariant() == extension;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed FileHasExtension for {filePath}: {ex}");
      return false;
    }
  }

  public static string GetFileExtension(string filePath) {
    if (string.IsNullOrEmpty(filePath)) {
      return "";
    }

    try {
      return Path.GetExtension(filePath);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed GetFileExtension for {filePath}: {ex}");
      return "";
    }
  }

  public static long ComputeDirectorySize(string path, bool recursive = false) {
    try {
      if (!Directory.Exists(path)) {
        return 0;
      }

      long total = 0;

      foreach (string file in Directory.EnumerateFileSystemEntries(path)) {
        if (!File.GetAttributes(file).HasFlag(FileAttributes.Directory)) {
          total += new FileInfo(file).Length;
        }
        else if (recursive) {
          total += ComputeDirectorySize(file, true);
        }
      }

      return total;
    }
    catch {
      return 0;
    }
  }

  public static TResult RunSync<TResult>(Func<Task<TResult>> func) {
    return TaskFactoryInstance.StartNew<Task<TResult>>(func).
      Unwrap<TResult>().GetAwaiter().GetResult();
  }

  public static void RunSync(Func<Task> func) {
    TaskFactoryInstance.StartNew<Task>(func).Unwrap().GetAwaiter().GetResult();
  }
}