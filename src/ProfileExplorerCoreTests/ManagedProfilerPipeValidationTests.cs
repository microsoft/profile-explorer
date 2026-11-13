// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProfileExplorer.CoreTests;

internal static class AttachedProfilerTargetProgram {
  private const uint MemCommit = 0x1000;
  private const uint MemReserve = 0x2000;
  private const uint MemRelease = 0x8000;
  private const uint PageExecuteReadWrite = 0x40;

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize,
                                            uint flAllocationType, uint flProtect);

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

  [MethodImpl(MethodImplOptions.NoInlining)]
  public static int BeforeAttachA(int value) => value * 3 + 1;

  [MethodImpl(MethodImplOptions.NoInlining)]
  public static int BeforeAttachB(int value) => value * 5 + 2;

  public static async Task<int> Main(string[] args) {
    if (args.Length == 0 || args[0] != "--profiler-target") {
      return 0;
    }

    if (BeforeAttachA(2) != 7 || BeforeAttachB(2) != 12) {
      return 2;
    }

    IntPtr page = VirtualAlloc(IntPtr.Zero, (UIntPtr)4096,
                               MemCommit | MemReserve, PageExecuteReadWrite);
    long freedAddress = IntPtr.Size == 4
      ? unchecked((uint)page.ToInt32())
      : page.ToInt64();
    if (page == IntPtr.Zero || !VirtualFree(page, UIntPtr.Zero, MemRelease)) {
      return 3;
    }

    Console.WriteLine($"READY:{RuntimeInformation.ProcessArchitecture}:{freedAddress}");

    while (await Console.In.ReadLineAsync() is { } command) {
      if (command == "ping") {
        Console.WriteLine($"ALIVE:{BeforeAttachA(2)}");
      }
      else if (command == "quit") {
        return 0;
      }
    }

    return 0;
  }
}

/// <summary>
/// Compiles and runs native C++ tests for managed-profiler pipe framing, pending-read
/// teardown, code publication, and CLR code-map FunctionID/ReJITID resolution across
/// x64 and x86. A test-only profiler also exercises the shared helpers against a real
/// attached CLR; it does not run the complete production profiler.
/// </summary>
[TestClass]
public class ManagedProfilerPipeValidationTests {
  private const int PipeHeaderSize = 8;
  private const int RequestBodySize = 24;
  private const int FunctionCodeMetadataSize = 28;
  private const int MaxResponseFrameSize = 16 * 1024 * 1024;
  private static readonly Guid ProfilerGuid = new("805A308B-061C-47F3-9B30-F785C3186E81");
  private string directory_;

  private enum PipeMessageKind {
    EndSession = 1,
    FunctionCode = 2,
    RequestFunctionCode = 4
  }

  private sealed record MethodIdentity(long Id, long Address, int Rejit);
  private sealed record NativeTestPaths(string VcvarsPath, string ManagedProfilerDir,
                                        string CoreClrIncDir, string CoreClrPalIncDir,
                                        string CppSourcePath);

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
    var (vcvarsPath, managedProfilerDir, coreClrIncDir, coreClrPalIncDir, cppSourcePath) =
      await FindNativeTestPaths(vcvarsBatchFile);

    string objPath = Path.Combine(directory_, "tests.obj");
    string exePath = Path.Combine(directory_, "tests.exe");
    string command =
      $"call \"{vcvarsPath}\" >nul && " +
      $"cl /nologo /O2 /DNDEBUG /DUNICODE /D_UNICODE /DNOMINMAX /DPEX_TEST_HOOKS /EHsc /MT /std:c++20 /permissive- " +
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

  private static async Task<NativeTestPaths> FindNativeTestPaths(string vcvarsBatchFile) {
    string vsInstallPath = await FindVisualStudioInstallationPath();
    if (string.IsNullOrEmpty(vsInstallPath)) {
      Assert.Inconclusive("Visual Studio C++ build tools are not installed on this machine.");
      return null;
    }

    string vcvarsPath = Path.Combine(vsInstallPath, "VC", "Auxiliary", "Build", vcvarsBatchFile);
    if (!File.Exists(vcvarsPath)) {
      Assert.Inconclusive($"Visual C++ environment script not found: {vcvarsPath}");
      return null;
    }

    string srcRoot = FindSrcRootDirectory();
    string managedProfilerDir = Path.Combine(srcRoot, "ManagedProfiler");
    string coreClrIncDir = Path.Combine(managedProfilerDir, "external", "coreclr", "inc");
    string coreClrPalIncDir = Path.Combine(managedProfilerDir, "external", "coreclr", "pal", "prebuilt", "inc");
    string cppSourcePath = Path.Combine(srcRoot, "ProfileExplorerCoreTests", "TestData",
                                        "ManagedProfiler", "PipeValidationTests.cpp");
    Assert.IsTrue(File.Exists(cppSourcePath), $"Native test source not found: {cppSourcePath}");
    return new NativeTestPaths(vcvarsPath, managedProfilerDir, coreClrIncDir,
                               coreClrPalIncDir, cppSourcePath);
  }

  [DataTestMethod]
  [DataRow("vcvars64.bat")]
  [DataRow("vcvars32.bat")]
  public async Task AttachedProfiler_RejectsInvalidRequestsAndRemainsResponsive(string vcvarsBatchFile) {
    var (vcvarsPath, managedProfilerDir, coreClrIncDir, coreClrPalIncDir, cppSourcePath) =
      await FindNativeTestPaths(vcvarsBatchFile);
    bool is64Bit = vcvarsBatchFile.Equals("vcvars64.bat", StringComparison.OrdinalIgnoreCase);
    string dotnetPath = FindDotNetHostPath(is64Bit);
    if (string.IsNullOrEmpty(dotnetPath)) {
      Assert.Inconclusive($"Matching {(is64Bit ? "x64" : "x86")} .NET host is not installed on this machine.");
      return;
    }

    string objPath = Path.Combine(directory_, "profiler.obj");
    string dllPath = Path.Combine(directory_, "test_profiler.dll");
    string defPath = Path.Combine(managedProfilerDir, "ManagedProfiler.def");
    string buildCommand =
      $"call \"{vcvarsPath}\" >nul && " +
      $"cl /nologo /LD /O2 /DNDEBUG /DUNICODE /D_UNICODE /DNOMINMAX /DPEX_BUILD_TEST_PROFILER_DLL /DPEX_TEST_HOOKS " +
      $"/EHsc /MT /std:c++20 /permissive- " +
      $"/I\"{managedProfilerDir}\" /I\"{coreClrIncDir}\" /I\"{coreClrPalIncDir}\" " +
      $"\"{cppSourcePath}\" \"/Fo{objPath}\" \"/Fe{dllPath}\" " +
      $"/link /DEF:\"{defPath}\"";

    var buildPsi = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
      Arguments = $"/d /s /c \"{buildCommand}\"",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var (buildExit, buildOut, buildErr) = await RunProcess(buildPsi, TimeSpan.FromSeconds(30));
    Assert.AreEqual(0, buildExit,
                    $"Building test profiler DLL failed ({vcvarsPath}):\n{buildOut}\n{buildErr}");

    string testAssemblyPath = Assembly.GetExecutingAssembly().Location;
    string runtimeConfigPath = Path.ChangeExtension(testAssemblyPath, ".runtimeconfig.json");
    var targetPsi = new ProcessStartInfo(dotnetPath) {
      RedirectStandardInput = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    targetPsi.ArgumentList.Add("exec");
    targetPsi.ArgumentList.Add("--runtimeconfig");
    targetPsi.ArgumentList.Add(runtimeConfigPath);
    targetPsi.ArgumentList.Add(testAssemblyPath);
    targetPsi.ArgumentList.Add("--profiler-target");
    targetPsi.Environment["DOTNET_TieredCompilation"] = "0";
    targetPsi.Environment["DOTNET_EnableDiagnostics"] = "1";
    targetPsi.Environment.Remove("CORECLR_ENABLE_PROFILING");
    targetPsi.Environment.Remove("CORECLR_PROFILER");
    targetPsi.Environment.Remove("CORECLR_PROFILER_PATH");

    using var target = Process.Start(targetPsi);
    Assert.IsNotNull(target, "Failed to start attached-profiler target process.");
    Task<string> targetStderr = target.StandardError.ReadToEndAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    try {
      string readyLine = await target.StandardOutput.ReadLineAsync(deadline.Token);
      Assert.IsNotNull(readyLine, "Target exited before reporting readiness.");
      string[] readyParts = readyLine.Split(':');
      Assert.AreEqual(3, readyParts.Length, $"Unexpected target readiness message: {readyLine}");
      Assert.AreEqual("READY", readyParts[0], $"Unexpected target readiness message: {readyLine}");
      Assert.AreEqual(is64Bit ? Architecture.X64.ToString() : Architecture.X86.ToString(),
                      readyParts[1], $"Unexpected target architecture: {readyLine}");
      Assert.IsTrue(long.TryParse(readyParts[2], out long freedAddress),
                    $"Invalid freed address in target readiness message: {readyLine}");

      var diagnostics = new DiagnosticsClient(target.Id);
      var methods = new ConcurrentDictionary<string, MethodIdentity>(StringComparer.Ordinal);
      var providers = new[] {
        new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose,
                              (long)ClrTraceEventParser.Keywords.Jit)
      };

      using (EventPipeSession session = diagnostics.StartEventPipeSession(providers, requestRundown: true)) {
        using var source = new EventPipeEventSource(session.EventStream);
        var rundown = new ClrRundownTraceEventParser(source);
        void Capture(MethodLoadUnloadVerboseTraceData data) {
          if (data.MethodNamespace.EndsWith("AttachedProfilerTargetProgram", StringComparison.Ordinal) &&
              data.MethodName.StartsWith("BeforeAttach", StringComparison.Ordinal)) {
            methods[data.MethodName] = new MethodIdentity(
              data.MethodID, (long)data.MethodStartAddress, (int)data.ReJITID);
          }
        }
        rundown.MethodDCStartVerbose += Capture;
        rundown.MethodDCStopVerbose += Capture;
        Task processing = Task.Run(() => source.Process());
        session.Stop();
        await processing.WaitAsync(deadline.Token);
      }

      MethodIdentity GetRequiredMethod(string name) {
        Assert.IsTrue(methods.TryGetValue(name, out MethodIdentity method),
                      $"CLR rundown did not report {name}. Found: {string.Join(", ", methods.Keys)}");
        return method;
      }

      MethodIdentity methodA = GetRequiredMethod(nameof(AttachedProfilerTargetProgram.BeforeAttachA));
      MethodIdentity methodB = GetRequiredMethod(nameof(AttachedProfilerTargetProgram.BeforeAttachB));

      string pipeName = $"PEXValidation-Attached-{Environment.ProcessId}-{Guid.NewGuid():N}";
      string fullPipePath = $@"\\.\pipe\{pipeName}";
      using var pipe = new NamedPipeServerStream(
        pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096);

      Task connectTask = pipe.WaitForConnectionAsync(deadline.Token);
      byte[] clientData = Encoding.Unicode.GetBytes(fullPipePath + "\0");
      diagnostics.AttachProfiler(TimeSpan.FromSeconds(15), ProfilerGuid, dllPath, clientData);
      await connectTask;

      async Task SendRequest(MethodIdentity method, int? pid = null) {
        byte[] frame = new byte[PipeHeaderSize + RequestBodySize];
        BinaryPrimitives.WriteInt32LittleEndian(frame, (int)PipeMessageKind.RequestFunctionCode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), frame.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8), method.Id);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16), method.Address);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24), method.Rejit);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28), pid ?? target.Id);
        await pipe.WriteAsync(frame, deadline.Token);
      }

      await SendRequest(methodA with { Id = 0xDEADBEEF });
      await SendRequest(methodA with { Rejit = methodA.Rejit + 1 });
      await SendRequest(methodA, target.Id + 1);
      await SendRequest(methodA with { Address = freedAddress });
      await SendRequest(methodB);

      bool receivedExpectedAddress = false;
      async Task<bool> ReadAndCheckResponse() {
        byte[] header = new byte[PipeHeaderSize];
        int bytesRead = await pipe.ReadAsync(header, deadline.Token);
        if (bytesRead == 0) {
          return false;
        }
        await pipe.ReadExactlyAsync(header.AsMemory(bytesRead), deadline.Token);
        int kind = BinaryPrimitives.ReadInt32LittleEndian(header);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        Assert.AreEqual((int)PipeMessageKind.FunctionCode, kind, $"Unexpected response kind: {kind}");
        Assert.IsTrue(size >= PipeHeaderSize + FunctionCodeMetadataSize && size <= MaxResponseFrameSize,
                      $"Invalid FunctionCode frame size: {size}");

        byte[] body = new byte[size - PipeHeaderSize];
        await pipe.ReadExactlyAsync(body, deadline.Token);
        long responseId = BinaryPrimitives.ReadInt64LittleEndian(body);
        long responseAddress = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(8));
        int responseRejit = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(16));
        int responsePid = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(20));
        int codeSize = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(24));

        Assert.AreEqual(target.Id, responsePid, "FunctionCode response has the wrong process ID.");
        Assert.IsTrue(codeSize > 0 && codeSize == body.Length - FunctionCodeMetadataSize,
                      $"Invalid FunctionCode payload size: {codeSize}, body size: {body.Length}");
        Assert.AreEqual(methodB.Id, responseId,
                        $"Unexpected FunctionCode response for 0x{responseId:X}; " +
                        "a rejected request may have been accepted.");
        Assert.AreEqual(methodB.Rejit, responseRejit, "FunctionCode response has the wrong ReJIT ID.");
        Assert.AreNotEqual(freedAddress, responseAddress, "FunctionCode response used the freed address.");
        receivedExpectedAddress |= responseAddress == methodB.Address;
        return true;
      }

      while (!receivedExpectedAddress) {
        Assert.IsTrue(await ReadAndCheckResponse(),
                      $"Pipe closed before code for the valid address 0x{methodB.Address:X} arrived.");
      }

      byte[] endSession = new byte[PipeHeaderSize];
      BinaryPrimitives.WriteInt32LittleEndian(endSession, (int)PipeMessageKind.EndSession);
      BinaryPrimitives.WriteInt32LittleEndian(endSession.AsSpan(4), endSession.Length);
      await pipe.WriteAsync(endSession, deadline.Token);
      while (await ReadAndCheckResponse()) {
      }

      await target.StandardInput.WriteLineAsync("ping");
      Assert.AreEqual("ALIVE:7", await target.StandardOutput.ReadLineAsync(deadline.Token),
                      "Target did not respond after profiler session ended.");

      await target.StandardInput.WriteLineAsync("quit");
      await target.WaitForExitAsync(deadline.Token);
      Assert.AreEqual(0, target.ExitCode, $"Attached target exited with {target.ExitCode}: {await targetStderr}");
    }
    finally {
      if (!target.HasExited) {
        target.Kill(entireProcessTree: true);
        await target.WaitForExitAsync();
      }
    }
  }

  private static string FindDotNetHostPath(bool is64Bit) {
    Architecture osArchitecture = RuntimeInformation.OSArchitecture;
    string programFiles = SelectProgramFilesRoot(
      is64Bit, osArchitecture,
      Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
      Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
      Environment.GetEnvironmentVariable("ProgramW6432"));
    return FindDotNetHostPath(programFiles, is64Bit, RuntimeInformation.OSArchitecture);
  }

  private static string SelectProgramFilesRoot(bool is64Bit, Architecture osArchitecture,
                                               string programFiles, string programFilesX86,
                                               string programFilesNative) {
    if (!is64Bit) {
      return programFilesX86;
    }

    return osArchitecture != Architecture.X86 && !string.IsNullOrEmpty(programFilesNative)
      ? programFilesNative
      : programFiles;
  }

  private static string FindDotNetHostPath(string programFiles, bool is64Bit,
                                           Architecture osArchitecture) {
    if (string.IsNullOrEmpty(programFiles)) {
      return null;
    }

    string dotnetRoot = Path.Combine(programFiles, "dotnet");
    if (is64Bit && osArchitecture == Architecture.Arm64) {
      dotnetRoot = Path.Combine(dotnetRoot, "x64");
    }

    string dotnetExe = Path.Combine(dotnetRoot, "dotnet.exe");
    string net8SharedDir = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
    if (!File.Exists(dotnetExe) || !Directory.Exists(net8SharedDir) ||
        !Directory.EnumerateDirectories(net8SharedDir, "8.0.*").Any()) {
      return null;
    }

    return dotnetExe;
  }

  [TestMethod]
  public void FindDotNetHostPath_SelectsMatchingInstallRootAndArchitecture() {
    string processProgramFiles = Path.Combine(directory_, "ProgramFilesX86");
    string nativeProgramFiles = Path.Combine(directory_, "ProgramFilesNative");
    string x86Host = Path.Combine(processProgramFiles, "dotnet", "dotnet.exe");
    string x86Runtime = Path.Combine(processProgramFiles, "dotnet", "shared",
                                     "Microsoft.NETCore.App", "8.0.1");
    string nativeX64Host = Path.Combine(nativeProgramFiles, "dotnet", "dotnet.exe");
    string nativeX64Runtime = Path.Combine(nativeProgramFiles, "dotnet", "shared",
                                           "Microsoft.NETCore.App", "8.0.2");
    string arm64X64Host = Path.Combine(nativeProgramFiles, "dotnet", "x64", "dotnet.exe");
    string arm64X64Runtime = Path.Combine(nativeProgramFiles, "dotnet", "x64", "shared",
                                          "Microsoft.NETCore.App", "8.0.3");
    Directory.CreateDirectory(x86Runtime);
    Directory.CreateDirectory(nativeX64Runtime);
    Directory.CreateDirectory(arm64X64Runtime);
    File.WriteAllBytes(x86Host, Array.Empty<byte>());
    File.WriteAllBytes(nativeX64Host, Array.Empty<byte>());
    File.WriteAllBytes(arm64X64Host, Array.Empty<byte>());

    Assert.AreEqual(x86Host, FindDotNetHostPath(
      SelectProgramFilesRoot(false, Architecture.X64, processProgramFiles, processProgramFiles,
                             nativeProgramFiles),
      false, Architecture.X64));
    Assert.AreEqual(nativeX64Host, FindDotNetHostPath(
      SelectProgramFilesRoot(true, Architecture.X64, processProgramFiles, processProgramFiles,
                             nativeProgramFiles),
      true, Architecture.X64));
    Assert.AreEqual(arm64X64Host, FindDotNetHostPath(
      SelectProgramFilesRoot(true, Architecture.Arm64, processProgramFiles, processProgramFiles,
                             nativeProgramFiles),
      true, Architecture.Arm64));

    Assert.IsNull(FindDotNetHostPath(processProgramFiles, true, Architecture.Arm64),
                  "An x86 install root must not substitute for the native x64 installation.");
    File.Delete(arm64X64Host);
    Assert.IsNull(FindDotNetHostPath(nativeProgramFiles, true, Architecture.Arm64),
                  "Missing x64 host should make the attached test inconclusive.");
    File.WriteAllBytes(arm64X64Host, Array.Empty<byte>());
    Directory.Delete(arm64X64Runtime, recursive: true);
    Assert.IsNull(FindDotNetHostPath(nativeProgramFiles, true, Architecture.Arm64),
                  "Missing matching runtime should make the attached test inconclusive.");
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
