// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ProfileExplorer.Core.Profile.Spgo;

public static class SptDumpTool {
  public static string Find(string? configuredPath, CancellationToken token = default) {
    if (!string.IsNullOrWhiteSpace(configuredPath)) {
      if (!File.Exists(configuredPath)) {
        throw new FileNotFoundException("The configured SPTDump was not found. Choose another tool or use Auto-detect.",
                                        configuredPath);
      }
      return Path.GetFullPath(configuredPath);
    }

    foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
      token.ThrowIfCancellationRequested();
      string path = directory.Trim().Trim('"');
      if (Path.IsPathFullyQualified(path) && File.Exists(Path.Combine(path, "sptdump.exe"))) {
        return Path.Combine(path, "sptdump.exe");
      }
    }

    string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                   @"Microsoft Visual Studio\Installer\vswhere.exe");
    if (File.Exists(vswhere)) {
      string? msvcPath = Utilities.Utils.DetectMSVCPath();
      token.ThrowIfCancellationRequested();
      if (msvcPath != null && File.Exists(Path.Combine(msvcPath, "sptdump.exe"))) {
        return Path.Combine(msvcPath, "sptdump.exe");
      }
    }

    foreach (string programFiles in new[] {
      Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
      Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
    }.Distinct(StringComparer.OrdinalIgnoreCase)) {
      string root = Path.Combine(programFiles, "Microsoft Visual Studio");
      foreach (string year in Directories(root)) {
        foreach (string edition in Directories(year)) {
          string tools = Path.Combine(edition, @"VC\Tools\MSVC");
          var versions = Directories(tools)
            .OrderByDescending(path => Version.TryParse(Path.GetFileName(path), out var version) ? version : new Version());
          foreach (string version in versions) {
            token.ThrowIfCancellationRequested();
            string[] hosts = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
              ? new[] {"Hostarm64", "Hostx64", "Hostx86"}
              : new[] {"Hostx64", "Hostx86"};
            foreach (string host in hosts) {
              foreach (string target in new[] {"x64", "arm64", "x86"}) {
                string path = Path.Combine(version, "bin", host, target, "sptdump.exe");
                if (File.Exists(path)) {
                  return path;
                }
              }
            }
          }
        }
      }
    }
    throw new FileNotFoundException("SPTDump was not found on PATH or in Visual Studio/MSVC installations. " +
                                    "Use Change to select SPTDump.exe from an installed MSVC toolset.");
  }

  private static string[] Directories(string path) {
    try {
      return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      Trace.TraceWarning($"Cannot search for SPTDump in {path}: {ex.Message}");
      return Array.Empty<string>();
    }
  }

  public static async Task<SptProfile> ReadAsync(string toolPath, string profilePath, CancellationToken token) {
    var startInfo = new ProcessStartInfo(toolPath) {
      WorkingDirectory = Path.GetDirectoryName(toolPath),
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true
    };
    startInfo.ArgumentList.Add("/all");
    startInfo.ArgumentList.Add(profilePath);
    using var process = new Process {StartInfo = startInfo};
    token.ThrowIfCancellationRequested();
    if (!process.Start()) {
      throw new IOException("SPTDump could not be started.");
    }
    var errorTask = ReadErrorsAsync(process.StandardError);
    try {
      var profile = await SptDumpParser.ParseAsync(process.StandardOutput, token).ConfigureAwait(false);
      await process.WaitForExitAsync(token).ConfigureAwait(false);
      string errors = await errorTask.ConfigureAwait(false);
      if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(errors)) {
        throw new IOException($"SPTDump exited with code {process.ExitCode}. {errors}".Trim());
      }
      return profile;
    }
    catch (InvalidDataException ex) {
      Stop(process);
      await process.WaitForExitAsync().ConfigureAwait(false);
      string errors = await errorTask.ConfigureAwait(false);
      throw new InvalidDataException($"{ex.Message}\nSPTDump exit code: {process.ExitCode}. {errors}".Trim(), ex);
    }
    finally {
      Stop(process);
      await process.WaitForExitAsync().ConfigureAwait(false);
      string errors = await errorTask.ConfigureAwait(false);
      if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(errors)) {
        Trace.TraceError($"SPTDump exit code {process.ExitCode}: {errors}");
      }
    }
  }

  private static void Stop(Process process) {
    try {
      if (!process.HasExited) {
        process.Kill(entireProcessTree: true);
      }
    }
    catch (InvalidOperationException) when (process.HasExited) {
      // The process exited between the check and Kill.
    }
  }

  private static async Task<string> ReadErrorsAsync(StreamReader reader) {
    var text = new StringBuilder();
    var buffer = new char[4096];
    bool truncated = false;
    int read;
    while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0) {
      int retained = Math.Min(read, 65536 - text.Length);
      text.Append(buffer, 0, retained);
      truncated |= retained < read;
    }
    if (truncated) {
      text.AppendLine("\n[Further diagnostic output omitted]");
    }
    return text.ToString();
  }
}
