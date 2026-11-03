// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
  public void ResolveFunctionCodeRequest_AndReadMessage_RejectInvalidFramesAndIdentities(string vcvarsBatchFile) {
    string vsInstallPath = FindVisualStudioInstallationPath();
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
    string cppSourcePath = Path.Combine(srcRoot, "ProfileExplorerCoreTests", "TestData",
                                        "ManagedProfiler", "PipeValidationTests.cpp");
    Assert.IsTrue(File.Exists(cppSourcePath), $"Native test source not found: {cppSourcePath}");

    string objPath = Path.Combine(directory_, "tests.obj");
    string exePath = Path.Combine(directory_, "tests.exe");
    string command =
      $"call \"{vcvarsPath}\" >nul && " +
      $"cl /nologo /O2 /DNDEBUG /DUNICODE /D_UNICODE /DNOMINMAX /EHsc /std:c++17 " +
      $"/I\"{managedProfilerDir}\" \"{cppSourcePath}\" \"/Fo{objPath}\" \"/Fe{exePath}\" && " +
      $"\"{exePath}\"";

    var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
      Arguments = $"/d /s /c \"{command}\"",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = Process.Start(psi);
    Assert.IsNotNull(process, "Failed to start native test compiler/runner process.");

    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    if (!process.WaitForExit(30000)) {
      process.Kill(entireProcessTree: true);
      Assert.Fail($"Native pipe validation test timed out ({vcvarsBatchFile}).");
    }

    Assert.AreEqual(0, process.ExitCode,
                    $"Native pipe validation test failed ({vcvarsBatchFile}):\n{stdout}\n{stderr}");
    StringAssert.Contains(stdout, "Native pipe framing and function identity checks passed.");
  }

  private static string FindVisualStudioInstallationPath() {
    string vswherePath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
      "Microsoft Visual Studio", "Installer", "vswhere.exe");
    if (!File.Exists(vswherePath)) {
      return null;
    }

    var psi = new ProcessStartInfo(vswherePath) {
      Arguments = "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath",
      RedirectStandardOutput = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = Process.Start(psi);
    if (process == null) {
      return null;
    }

    string output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit(10000);

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
