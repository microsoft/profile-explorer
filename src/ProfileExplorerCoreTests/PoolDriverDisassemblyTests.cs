// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorerCoreTests;

/// <summary>
/// End-to-end proof for <see cref="NativeAddressDisassemblyService.ResolveByIdentity"/> — the full
/// locate-then-disassemble path FUN AI's pool pipeline calls. Uses a real first-party OS binary that
/// is present locally (ntdll.dll), so <see cref="BinaryFileLocator"/> resolves it by identity without
/// any network download, and the disassembler decodes a real function via the PE exception directory
/// (.pdata). This validates the whole API surface end-to-end on known code.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class PoolDriverDisassemblyTests {
  [TestMethod]
  public void ResolveByIdentity_LocalFirstPartyBinary_DisassemblesEndToEnd() {
    var path = Path.Combine(Environment.SystemDirectory, "ntdll.dll");
    if (!File.Exists(path)) {
      Assert.Inconclusive($"{path} not found.");
      return;
    }

    var info = PEBinaryInfoProvider.GetBinaryFileInfo(path);

    // AddressOfEntryPoint is 0x0 for ntdll.dll (no traditional entry point -- TLS-only load), so it
    // is not a valid disassembly target. Instead, derive a guaranteed-real code RVA the same way
    // NativeAddressDisassembler's own .pdata fallback does: parse the PE exception directory
    // (IMAGE_DIRECTORY_ENTRY_EXCEPTION) for a real RUNTIME_FUNCTION entry and disassemble its start.
    long entryRva;
    using (var peInfo = new PEBinaryInfoProvider(path)) {
      Assert.IsTrue(peInfo.Initialize(), $"Failed to read PE headers for {path}.");
      var exceptionData = peInfo.GetExceptionDirectoryData();
      Assert.IsFalse(exceptionData.IsEmpty, "Expected a non-empty .pdata exception directory for x64 ntdll.dll.");

      var ranges = RuntimeFunctionTable.ParseAmd64(exceptionData.Span);
      var range = ranges.FirstOrDefault(r => r.EndRva - r.StartRva is > 20 and < 5000);
      Assert.IsTrue(range.EndRva > range.StartRva, "Expected at least one usable RUNTIME_FUNCTION entry.");
      entryRva = range.StartRva;
    }

    var settings = new SymbolFileSourceSettings();
    settings.ManagedIdentityEnabled = false;
    PDBDebugInfoProvider.ReinitializeCredentials(settings);

    var identity = new BinaryFileDescriptor {
      ImageName = "ntdll.dll",
      ImagePath = path, // local → BinaryFileLocator resolves without a network download
      TimeStamp = info.TimeStamp,
      ImageSize = info.ImageSize,
      Architecture = info.Architecture,
      FileKind = BinaryFileKind.Native
    };

    var result = NativeAddressDisassemblyService.ResolveByIdentity(
      identity,
      address: entryRva,
      NativeAddressForm.ModuleRva,
      FrameAddressKind.InstructionPointer,
      settings);

    Console.WriteLine($"Binary={path} EntryRva=0x{entryRva:X} TimeStamp=0x{info.TimeStamp:X} Size={info.ImageSize}");
    Console.WriteLine($"Success={result.Success} Failure={result.FailureReason} Error={result.ErrorMessage}");
    Console.WriteLine($"Module={result.ModuleName} Arch={result.Architecture} Qualified={result.QualifiedName}");
    if (result.Range is { } r) {
      Console.WriteLine($"Range=[0x{r.StartRva:X}..0x{r.EndRva:X}] Provenance={r.Provenance} Name={r.FunctionName}");
    }

    Console.WriteLine($"Instructions={result.Instructions.Count}");
    foreach (var ins in result.Instructions.Take(40)) {
      Console.WriteLine($"  +0x{ins.Rva:X6}  {ins.Text}");
    }

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.IsTrue(result.Instructions.Count > 0, "Expected disassembled instructions.");
  }

  /// <summary>
  /// End-to-end proof of the real target scenario: a third-party driver with NO local copy on
  /// this machine and NO PDB on symweb, so <see cref="BinaryFileLocator"/> must download the raw
  /// binary from the internal symbol server by identity alone. This is the case the
  /// <c>BinaryFileLocator</c> size-probing fallback exists for: the ETW/DataLayer-reported
  /// ImageSize (mapped-view size) doesn't match the real PE SizeOfImage symweb indexes by.
  /// Identity captured from a real FunGates trace blame-walk (Cont -&gt; e1r63x64.sys, an Intel
  /// NIC driver). Requires network + internal auth (symweb) -- Inconclusive, not Fail, when
  /// unavailable (matches convention for network-dependent Integration tests in this project).
  /// </summary>
  [TestMethod]
  public void ResolveByIdentity_RemoteThirdPartyDriverNoLocalCopy_DownloadsAndDisassembles() {
    const string imageName = "e1r63x64.sys";
    const int timeStamp = unchecked((int)0x50A5733A);
    const long etwReportedImageSize = 466944; // 0x72000 -- mapped-view size from the trace/DataLayer.
    const long targetRva = 0x28897; // The blamed frame's return address, per the FunGates blame walk.

    var settings = new SymbolFileSourceSettings();
    settings.ManagedIdentityEnabled = false;
    // Internal symbol resolution only -- never the public msdl server (privacy constraint).
    settings.SymbolPaths.Clear();
    settings.SymbolPaths.Add($@"srv*{SymbolFileSourceSettings.DefaultCacheDirectoryPath}*https://symweb.azurefd.net");
    PDBDebugInfoProvider.ReinitializeCredentials(settings);

    var identity = new BinaryFileDescriptor {
      ImageName = imageName,
      ImagePath = imageName, // No local path -- forces BinaryFileLocator's remote/download path.
      TimeStamp = timeStamp,
      ImageSize = etwReportedImageSize,
      Architecture = System.Reflection.PortableExecutable.Machine.Amd64,
      FileKind = BinaryFileKind.Native
    };

    var located = BinaryFileLocator.LocateBinaryFile(identity, settings);
    Console.WriteLine($"Found={located.Found} Path={located.FilePath}");
    if (!located.Found) {
      Console.WriteLine($"Details:\n{located.Details}");
      Assert.Inconclusive($"Could not download {imageName} (network/auth unavailable, or genuinely not on symweb): {located.Details}");
      return;
    }

    // Corrected ImageSize should be smaller than the ETW-reported size (kernel mapped-view slack).
    var downloadedInfo = PEBinaryInfoProvider.GetBinaryFileInfo(located.FilePath);
    Console.WriteLine($"Corrected ImageSize={downloadedInfo.ImageSize} (ETW-reported was {etwReportedImageSize})");

    var result = NativeAddressDisassemblyService.ResolveByIdentity(
      identity,
      address: targetRva,
      NativeAddressForm.ModuleRva,
      FrameAddressKind.InstructionPointer,
      settings);

    Console.WriteLine($"Success={result.Success} Failure={result.FailureReason} Error={result.ErrorMessage}");
    Console.WriteLine($"Module={result.ModuleName} Arch={result.Architecture} Qualified={result.QualifiedName}");
    if (result.Range is { } r) {
      Console.WriteLine($"Range=[0x{r.StartRva:X}..0x{r.EndRva:X}] Provenance={r.Provenance} Name={r.FunctionName}");
    }

    Console.WriteLine($"Instructions={result.Instructions.Count}");
    foreach (var ins in result.Instructions.Take(40)) {
      Console.WriteLine($"  +0x{ins.Rva:X6}  {ins.Text}");
    }

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.IsTrue(result.Instructions.Count > 0, "Expected disassembled instructions.");
  }

  /// <summary>
  /// Proves the <see cref="BinaryFileLocator"/> size-probing fallback itself, with a real symweb
  /// network round-trip: takes local <c>ntdll.dll</c> (known present on symweb, confirmed via a
  /// manual token probe during the pool-disassembly prototype work) but deliberately reports a
  /// slightly inflated ImageSize (simulating the ETW/DataLayer kernel mapped-view slack) and hides
  /// the local path so <see cref="BinaryFileLocator"/> must go through the real download path
  /// rather than the local-file shortcut. If the probing fallback works, the exact-size lookup
  /// 404s once, then a nearby smaller candidate succeeds. Inconclusive (not Fail) when network/auth
  /// is unavailable, matching this project's convention for network-dependent Integration tests.
  /// </summary>
  [TestMethod]
  public void LocateBinaryFile_InflatedImageSize_SucceedsViaSizeProbingFallback() {
    var path = Path.Combine(Environment.SystemDirectory, "ntdll.dll");
    if (!File.Exists(path)) {
      Assert.Inconclusive($"{path} not found.");
      return;
    }

    var info = PEBinaryInfoProvider.GetBinaryFileInfo(path);

    var settings = new SymbolFileSourceSettings();
    settings.ManagedIdentityEnabled = false;
    // Internal symbol resolution only -- never the public msdl server (privacy constraint).
    settings.SymbolPaths.Clear();
    settings.SymbolPaths.Add($@"srv*{SymbolFileSourceSettings.DefaultCacheDirectoryPath}*https://symweb.azurefd.net");
    PDBDebugInfoProvider.ReinitializeCredentials(settings);

    var identity = new BinaryFileDescriptor {
      ImageName = "ntdll.dll",
      ImagePath = "ntdll.dll", // No real path -- forces the remote path, not the local shortcut.
      TimeStamp = info.TimeStamp,
      ImageSize = info.ImageSize + 3 * 0x1000, // Deliberately inflated by 3 pages of "slack".
      Architecture = info.Architecture,
      FileKind = BinaryFileKind.Native
    };

    var located = BinaryFileLocator.LocateBinaryFile(identity, settings);
    Console.WriteLine($"RealImageSize={info.ImageSize} RequestedImageSize={identity.ImageSize}");
    Console.WriteLine($"Found={located.Found} Path={located.FilePath}");

    if (!located.Found) {
      Console.WriteLine($"Details:\n{located.Details}");
      Assert.Inconclusive($"Could not download ntdll.dll (network/auth unavailable): {located.Details}");
      return;
    }

    var downloadedInfo = PEBinaryInfoProvider.GetBinaryFileInfo(located.FilePath);
    Assert.AreEqual(info.ImageSize, downloadedInfo.ImageSize,
      "Expected the probing fallback to land on the real PE SizeOfImage.");
  }

  /// <summary>
  /// First-party ground-truth probe: disassembles a real, named kernel function
  /// (<c>ntoskrnl!CmpAllocate</c>, one of the functions the FunGates blame-walk landed on) using
  /// this machine's own local <c>ntoskrnl.exe</c> build and its matching private PDB downloaded
  /// from symweb. Unlike the pool-trace scenario, this doesn't need to match the FunGates trace's
  /// exact build -- any locally-consistent (binary, PDB) pair is enough to prove the disassembly
  /// is accurate and get real assembly text to feed the AI-pseudocode step and diff against real
  /// source (via Bluebird) afterwards. Inconclusive when the PDB isn't available or the named
  /// function isn't present in this build (private symbol names can move between OS versions).
  /// </summary>
  [TestMethod]
  public void ResolveByIdentity_FirstPartyNamedKernelFunction_DisassemblesRealCode() {
    var path = Path.Combine(Environment.SystemDirectory, "ntoskrnl.exe");
    if (!File.Exists(path)) {
      Assert.Inconclusive($"{path} not found.");
      return;
    }

    var settings = new SymbolFileSourceSettings();
    settings.ManagedIdentityEnabled = false;
    settings.SymbolPaths.Clear();
    settings.SymbolPaths.Add($@"srv*{SymbolFileSourceSettings.DefaultCacheDirectoryPath}*https://symweb.azurefd.net");
    PDBDebugInfoProvider.ReinitializeCredentials(settings);

    SymbolFileDescriptor symbolFile;
    using (var peInfo = new PEBinaryInfoProvider(path)) {
      Assert.IsTrue(peInfo.Initialize(), $"Failed to read PE headers for {path}.");
      symbolFile = peInfo.SymbolFileInfo;
    }

    if (symbolFile == null) {
      Assert.Inconclusive("No CodeView debug directory entry found in ntoskrnl.exe.");
      return;
    }

    var pdbResult = PDBDebugInfoProvider.LocateDebugInfoFile(symbolFile, settings);
    Console.WriteLine($"PDB Found={pdbResult.Found} Path={pdbResult.FilePath}");
    if (!pdbResult.Found) {
      Console.WriteLine($"Details:\n{pdbResult.Details}");
      Assert.Inconclusive($"Could not download ntoskrnl.pdb (network/auth unavailable): {pdbResult.Details}");
      return;
    }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(pdbResult.FilePath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var func = provider.FindFunction("CmpAllocate");
    if (func == null) {
      Assert.Inconclusive("CmpAllocate not found in this build's ntoskrnl.pdb (private symbol names can change between OS versions).");
      return;
    }

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = path,
      Address = func.RVA,
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.InstructionPointer,
      SymbolDebugInfo = provider
    });

    Console.WriteLine($"CmpAllocate RVA=0x{func.RVA:X} Size={func.Size}");
    Console.WriteLine($"Success={result.Success} Failure={result.FailureReason} Error={result.ErrorMessage}");
    if (result.Range is { } r) {
      Console.WriteLine($"Range=[0x{r.StartRva:X}..0x{r.EndRva:X}] Provenance={r.Provenance} Name={r.FunctionName}");
    }

    Console.WriteLine($"Instructions={result.Instructions.Count}");
    foreach (var ins in result.Instructions) {
      Console.WriteLine($"  +0x{ins.Rva:X6}  {ins.Text}");
    }

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.IsTrue(result.Instructions.Count > 0, "Expected disassembled instructions.");
  }
}

