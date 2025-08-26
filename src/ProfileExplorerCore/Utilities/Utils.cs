// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using Microsoft.Win32;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.IR.Tags;
using ProfileExplorerCore.Providers;

namespace ProfileExplorerCore.Utilities;

public static class Utils {
  private static readonly TaskFactory TaskFactoryInstance = new(CancellationToken.None,
                                                                TaskCreationOptions.None,
                                                                TaskContinuationOptions.None,
                                                                TaskScheduler.Default);
  private static readonly string HTML_COPY_HEADER =
    "Version:0.9\r\n" +
    "StartHTML:{0:0000000000}\r\n" +
    "EndHTML:{1:0000000000}\r\n" +
    "StartFragment:{2:0000000000}\r\n" +
    "EndFragment:{3:0000000000}\r\n";
  private static readonly string HTML_COPY_START =
    "<html>\r\n" +
    "<body>\r\n" +
    "<!--StartFragment-->";
  private static readonly string HTML_COPY_END =
    "<!--EndFragment-->\r\n" +
    "</body>\r\n" +
    "</html>";

  public static TResult RunSync<TResult>(Func<Task<TResult>> func) {
    return TaskFactoryInstance.StartNew<Task<TResult>>(func).
      Unwrap<TResult>().GetAwaiter().GetResult();
  }

  public static void RunSync(Func<Task> func) {
    TaskFactoryInstance.StartNew<Task>(func).Unwrap().GetAwaiter().GetResult();
  }

  //? TODO: This should be part of the IR NameProvider
  public static string MakeBlockDescription(BlockIR block) {
    if (block == null) {
      return "";
    }

    if (block.HasLabel && !string.IsNullOrEmpty(block.Label.Name)) {
      return $"B{block.Number} ({block.Label.Name})";
    }

    return $"B{block.Number}";
  }

  //? TODO: This should be part of the IR NameProvider
  public static string MakeElementDescription(IRElement element) {
    switch (element) {
      case BlockIR block:
        return MakeBlockDescription(block);
      case InstructionIR instr: {
        var builder = new StringBuilder();
        bool needsComma = false;

        if (instr.Destinations.Count > 0) {
          foreach (var destOp in instr.Destinations) {
            if (needsComma) {
              builder.Append(", ");
            }
            else {
              needsComma = true;
            }

            builder.Append(MakeElementDescription(destOp));
          }

          builder.Append(" = ");
        }

        builder.Append($"{instr.OpcodeText} ");
        needsComma = false;

        foreach (var sourceOp in instr.Sources) {
          if (needsComma) {
            builder.Append(", ");
          }
          else {
            needsComma = true;
          }

          builder.Append(MakeElementDescription(sourceOp));
        }

        return builder.ToString();
      }
      case OperandIR op: {
        string text = GetSymbolName(op);

        switch (op.Kind) {
          case OperandKind.Address:
          case OperandKind.LabelAddress: {
            text = $"&{text}";
            break;
          }
          case OperandKind.Indirection: {
            text = $"[{MakeElementDescription(op.IndirectionBaseValue)}]";
            break;
          }
          case OperandKind.IntConstant: {
            text = op.IntValue.ToString();
            break;
          }
          case OperandKind.FloatConstant: {
            text = op.FloatValue.ToString();
            break;
          }
        }

        var ssaTag = op.GetTag<ISSAValue>();

        if (ssaTag != null) {
          text += $"<{ssaTag.DefinitionId}>";
        }

        return text;
      }
      default:
        return element.ToString();
    }
  }

  //? TODO: This should be part of the IR NameProvider
  public static string GetSymbolName(OperandIR op) {
    if (op.HasName) {
      return op.NameValue.ToString();
    }

    return "";
  }

  public static void Swap<T>(ref T a, ref T b) {
    var temp = a;
    a = b;
    b = temp;
  }

  public static string GetAutoSaveFilePath() {
    string AUTO_SAVE_TEMP_FILE = "autosave.pex";
    return Path.Combine(Path.GetTempPath(), AUTO_SAVE_TEMP_FILE);
  }

  public static bool TryDeleteFile(string path) {
    if (!File.Exists(path)) {
      return false;
    }

    try {
      File.Delete(path);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to delete {path}: {ex.Message}");
      return false;
    }

    return true;
  }

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

  public static string TryGetFileNameWithoutExtension(string path) {
    try {
      if (string.IsNullOrEmpty(path)) {
        return "";
      }

      return Path.GetFileNameWithoutExtension(path);
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

  public static string CleanupPath(string path) {
    if (string.IsNullOrEmpty(path)) {
      return path;
    }

    path = path.Trim();

    if (path.Length >= 2 &&
        path[0] == '"' &&
        path[^1] == '"') {
      path = path.Substring(1, path.Length - 2);
    }

    return path;
  }

  public static bool OpenExternalFile(string path, bool checkFileExists = true) {
    if (checkFileExists && !File.Exists(path)) {
      return false;
    }

    try {
      var psi = new ProcessStartInfo(path) {
        UseShellExecute = true
      };

      Process.Start(psi);
      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to open file: {path}, exception {ex.Message}");
      return false;
    }
  }

  public static bool OpenURL(string path) {
    return OpenExternalFile(path, false);
  }

  public static bool ExecuteTool(string path, string args, CancelableTask cancelableTask = null,
                                 Dictionary<string, string> envVariables = null) {
    return ExecuteToolWithOutput(path, args, cancelableTask, envVariables) != null;
  }

  public static string ExecuteToolWithOutput(string path, string args, CancelableTask cancelableTask = null,
                                             Dictionary<string, string> envVariables = null) {
    if (!File.Exists(path)) {
      return null;
    }

    Trace.TraceInformation($"Executing tool {path} with args {args}");

    var outputText = new StringBuilder();
    var procInfo = new ProcessStartInfo(path) {
      Arguments = args,
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardError = false,
      RedirectStandardOutput = true
    };

    if (envVariables != null) {
      foreach (var pair in envVariables) {
        procInfo.EnvironmentVariables[pair.Key] = pair.Value;
      }
    }

    try {
      using var process = new Process {StartInfo = procInfo, EnableRaisingEvents = true};

      process.OutputDataReceived += (sender, e) => {
        outputText.AppendLine(e.Data);
      };

      process.Start();
      process.BeginOutputReadLine();

      do {
        process.WaitForExit(100);

        if (cancelableTask != null && cancelableTask.IsCanceled) {
          Trace.TraceWarning($"Task {ObjectTracker.Track(cancelableTask)}: Canceled");
          process.Kill();
          return null;
        }
      } while (!process.HasExited);

      process.CancelOutputRead();

      if (process.ExitCode != 0) {
        Trace.TraceError($"Task {ObjectTracker.Track(cancelableTask)}: Failed with error code: {process.ExitCode}");
        Trace.TraceError($"  Output:\n{outputText}");
        return null;
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Task {ObjectTracker.Track(cancelableTask)}: Failed with exception: {ex.Message}");
      return null;
    }

    return outputText.ToString();
  }

  public static string DetectMSVCPath() {
    // https://devblogs.microsoft.com/cppblog/finding-the-visual-c-compiler-tools-in-visual-studio-2017/
    string vswherePath = @"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe";
    string vswhereArgs =
      @"-latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath";

    vswherePath = Environment.ExpandEnvironmentVariables(vswherePath);

    try {
      string vsPath = ExecuteToolWithOutput(vswherePath, vswhereArgs);

      if (string.IsNullOrEmpty(vsPath)) {
        Trace.TraceError("Failed to run vswhere");
        return null;
      }

      vsPath = vsPath.SplitLinesRemoveEmpty().First();
      string msvcPathInfo = Path.Combine(vsPath, @"VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt");
      string msvcVersion = File.ReadLines(msvcPathInfo).First();
      return Path.Combine(vsPath, @"VC\Tools\MSVC", msvcVersion, @"bin\HostX64\x64");
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to find MSVC path: {ex.Message}");
    }

    return null;
  }

  public static DebugFileSearchResult LocateDebugInfoFile(string imagePath, string extension) {
    try {
      if (!File.Exists(imagePath)) {
        return DebugFileSearchResult.None;
      }

      string path = Path.GetDirectoryName(imagePath);
      string imageName = Path.GetFileNameWithoutExtension(imagePath);
      string pdbPath = Path.Combine(path, imageName) + extension;

      if (File.Exists(pdbPath)) {
        return DebugFileSearchResult.Success(pdbPath);
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to find debug file for {imagePath}: {ex.Message}");
    }

    return DebugFileSearchResult.None;
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

  public static void OpenExplorerAtFile(string path) {
    if (!File.Exists(path) && !Directory.Exists(path)) {
      return;
    }

    try {
      Process.Start("explorer.exe", "/select, " + path);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to start explorer.exe for {path}: {ex.Message}");
    }
  }
 

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static double SnapToPixels(double value) {
    return Math.Round(value);
  }

  private static bool IsOptionsUpdateEvent(string sourceName) {
    return sourceName != "Button" &&
           sourceName != "ToggleButton" &&
           sourceName != "TextBox" &&
           sourceName != "TextBoxView" &&
           sourceName != "TextBlock";
  }

  public static string ConvertHtmlToClipboardFormat(string html) {
    var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    byte[] data = Array.Empty<byte>();
    byte[] header = encoding.GetBytes(string.Format(HTML_COPY_HEADER, 0, 1, 2, 3));
    data = data.Concat(header).ToArray();

    int startHtml = data.Length;
    data = data.Concat(encoding.GetBytes(HTML_COPY_START)).ToArray();

    int startFragment = data.Length;
    data = data.Concat(encoding.GetBytes(html)).ToArray();
    int endFragment = data.Length;
    data = data.Concat(encoding.GetBytes(HTML_COPY_END)).ToArray();

    int endHtml = data.Length;
    byte[] newHeader = encoding.GetBytes(
      string.Format(HTML_COPY_HEADER, startHtml, endHtml, startFragment, endFragment));
    Array.Copy(newHeader, data, startHtml);
    return encoding.GetString(data);
  }

  public static string RemovePathQuotes(string text) {
    if (string.IsNullOrEmpty(text)) {
      return text;
    }

    return text.Trim('\"');
  }

  public struct KeyCharInfo {
    public bool IsLetter;
    public char Letter;
    public bool IsShift;
    public bool IsControl;
    public bool IsAlt;
  }
}