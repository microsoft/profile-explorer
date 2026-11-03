// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProfileExplorer.CoreTests;

/// <summary>
/// Compiles and runs native C++ tests for the managed profiler named-pipe request framing
/// (<c>NamedPipeClient.h</c>) and CLR code-map FunctionID/ReJITID resolution
/// (<c>FunctionCodeRequest.h</c>) across both x64 and x86 pointer widths.
/// </summary>
[TestClass]
public class ManagedProfilerPipeValidationTests {
  private string directory_;

  [TestInitialize]
  public void Initialize() {
    directory_ = Path.Combine(Path.GetTempPath(), $"ProfileExplorer-pipe-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory_);
  }

  [TestCleanup]
  public void Cleanup() {
    if (Directory.Exists(directory_)) {
      Directory.Delete(directory_, recursive: true);
    }
  }

  [DataTestMethod]
  [DataRow("vcvars64.bat")]
  [DataRow("vcvars32.bat")]
  public async Task ResolveFunctionCodeRequest_AndReadMessage_RejectInvalidFramesAndIdentities(string vcvarsBatchFile) {
    string vsInstallPath = await FindVisualStudioInstallationPath();
    if (string.IsNullOrEmpty(vsInstallPath)) {
      Assert.Inconclusive("Visual Studio C++ build tools are not installed on this machine.");
      return;
    }

    string vcvarsPath = Path.Combine(vsInstallPath, "VC", "Auxiliary", "Build", vcvarsBatchFile);
    if (!File.Exists(vcvarsPath)) {
      Assert.Inconclusive($"Visual C++ environment script not found: {vcvarsPath}");
      return;
    }

    string srcRoot = FindSrcRootDirectory();
    string managedProfilerDir = Path.Combine(srcRoot, "ManagedProfiler");
    string coreClrIncDir = Path.Combine(managedProfilerDir, "external", "coreclr", "inc");
    string coreClrPalIncDir = Path.Combine(managedProfilerDir, "external", "coreclr", "pal", "prebuilt", "inc");
    string cppSourcePath = Path.Combine(srcRoot, "ProfileExplorerCoreTests", "TestData",
                                        "ManagedProfiler", "PipeValidationTests.cpp");
    Assert.IsTrue(File.Exists(cppSourcePath), $"Native test source not found: {cppSourcePath}");

    string objPath = Path.Combine(directory_, "tests.obj");
    string exePath = Path.Combine(directory_, "tests.exe");
    string command =
      $"call \"{vcvarsPath}\" >nul && " +
      $"cl /nologo /O2 /DNDEBUG /DUNICODE /D_UNICODE /DNOMINMAX /EHsc /MT /std:c++20 /permissive- " +
      $"/I\"{managedProfilerDir}\" /I\"{coreClrIncDir}\" /I\"{coreClrPalIncDir}\" " +
      $"\"{cppSourcePath}\" \"/Fo{objPath}\" \"/Fe{exePath}\" && " +
      $"\"{exePath}\"";

    var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
      Arguments = $"/d /s /c \"{command}\"",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var (exitCode, stdout, stderr) = await RunProcess(psi, TimeSpan.FromSeconds(30));
    Assert.AreEqual(0, exitCode,
                    $"Native pipe validation test failed ({vcvarsBatchFile}):\n{stdout}\n{stderr}");
    StringAssert.Contains(stdout, "Native pipe framing and function identity checks passed.");
  }

  [TestMethod]
  public async Task RunProcess_BothOutputStreamsExceedPipeCapacity_Completes() {
    var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
      Arguments = "/d /c \"for /L %i in (1,1,8192) do @(echo stdout-marker& echo stderr-marker 1>&2)\"",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    var (exitCode, stdout, stderr) = await RunProcess(psi, TimeSpan.FromSeconds(10));
    Assert.AreEqual(0, exitCode);
    Assert.AreEqual(8192, stdout.Split("stdout-marker", StringSplitOptions.None).Length - 1);
    Assert.AreEqual(8192, stderr.Split("stderr-marker", StringSplitOptions.None).Length - 1);
    Assert.IsTrue(stdout.Length > 64 * 1024 && stderr.Length > 64 * 1024);
  }

  [TestMethod]
  public async Task RunProcess_ChildDoesNotExit_FailsWithinTimeout() {
    var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,
                                                "WindowsPowerShell", "v1.0", "powershell.exe")) {
      Arguments = "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    var stopwatch = Stopwatch.StartNew();
    var exception = await Assert.ThrowsExceptionAsync<AssertFailedException>(
      () => RunProcess(psi, TimeSpan.FromSeconds(1)).WaitAsync(TimeSpan.FromSeconds(10)));
    StringAssert.Contains(exception.Message, "timed out");
    Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
  }

  private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcess(
    ProcessStartInfo startInfo, TimeSpan timeout) {
    using var process = Process.Start(startInfo);
    Assert.IsNotNull(process, $"Failed to start {startInfo.FileName}.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    try {
      await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(timeout);
    }
    catch (TimeoutException) {
      if (!process.HasExited) {
        process.Kill(entireProcessTree: true);
      }
      await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
      Assert.Fail($"Process timed out after {timeout}: {startInfo.FileName}");
    }
    return (process.ExitCode, await stdout, await stderr);
  }

  private static async Task<string> FindVisualStudioInstallationPath() {
    string vswherePath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
      "Microsoft Visual Studio", "Installer", "vswhere.exe");
    if (!File.Exists(vswherePath)) {
      return null;
    }

    var psi = new ProcessStartInfo(vswherePath) {
      Arguments = "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var (exitCode, stdout, stderr) = await RunProcess(psi, TimeSpan.FromSeconds(10));
    Assert.AreEqual(0, exitCode, $"Visual Studio discovery failed: {stderr}");
    string output = stdout.Trim();

    if (string.IsNullOrEmpty(output)) {
      return null;
    }

    return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
  }

  private static string FindSrcRootDirectory() {
    string currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    while (currentDir != null) {
      if (File.Exists(Path.Combine(currentDir, "ManagedProfiler", "FunctionCodeRequest.h"))) {
        return currentDir;
      }
      currentDir = Directory.GetParent(currentDir)?.FullName;
    }

    throw new DirectoryNotFoundException("Could not locate src directory containing ManagedProfiler.");
  }
}
