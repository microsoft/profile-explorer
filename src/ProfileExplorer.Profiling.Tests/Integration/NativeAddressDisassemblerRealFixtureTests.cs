// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Symbols;
using ProfileExplorer.Profiling.Tests.Helpers;
using System.Reflection.PortableExecutable;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="NativeAddressDisassembler"/> against the real compiled x64
/// fixture (Mso20win32client.dll/.pdb) shared with <see cref="DisassemblerTests"/>. Complements
/// <see cref="NativeAddressDisassemblerSyntheticTests"/> by exercising the API's two function-bounds
/// sources (PDB/DIA symbols vs. the PE exception directory) against the *same* real address, and
/// return-address normalization against a genuine compiler-emitted call site rather than hand-encoded
/// bytes. Skips (via <see cref="Assert.Inconclusive"/>) when the shared fixture isn't present, matching
/// the existing convention in <see cref="DisassemblerTests"/>.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class NativeAddressDisassemblerRealFixtureTests {
  private static string DllPath => TestDataHelper.GetBinaryFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoDllFile);
  private static string PdbPath => TestDataHelper.GetSymbolFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoPdbFile);

  private static bool CanRun() {
    return TestDataHelper.HasTestData(TestDataHelper.MsoTrace) &&
           File.Exists(DllPath) &&
           File.Exists(PdbPath);
  }

  private static PdbSymbolProvider? LoadProvider() {
    var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      provider.Dispose();
      return null;
    }

    return provider;
  }

  [TestMethod]
  public void RealBinary_PdbBounds_ResolvesExactFunctionRangeAndName() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = LoadProvider();
    if (provider == null) { Assert.Inconclusive("PDB load failed."); return; }

    var func = TestDataHelper.GetUniqueRvaFunctions(provider).FirstOrDefault(f => f.Size is > 20 and < 5000);
    if (func == null) { Assert.Inconclusive("No suitable function found."); return; }

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = DllPath,
      Address = func.RVA,
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.InstructionPointer,
      SymbolDebugInfo = provider
    });

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.AreEqual(Machine.Amd64, result.Architecture);
    Assert.IsNotNull(result.Range);
    Assert.AreEqual(FunctionBoundaryProvenance.PdbSymbols, result.Range.Value.Provenance);
    Assert.AreEqual(func.RVA, result.Range.Value.StartRva);
    Assert.AreEqual(func.RVA + func.Size, result.Range.Value.EndRva);
    Assert.AreEqual(func.Name, result.Range.Value.FunctionName);
    Assert.AreEqual($"{TestDataHelper.MsoModuleName}!{func.Name}", result.QualifiedName);
    Assert.IsTrue(result.Instructions.Count > 0);
  }

  [TestMethod]
  public void RealBinary_PdataFallback_ResolvesBoundsWithoutPdb() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = LoadProvider();
    if (provider == null) { Assert.Inconclusive("PDB load failed."); return; }

    var func = TestDataHelper.GetUniqueRvaFunctions(provider).FirstOrDefault(f => f.Size is > 20 and < 5000);
    if (func == null) { Assert.Inconclusive("No suitable function found."); return; }

    // Same address as the PDB-bounds test, but with no symbol provider -- must fall back to the
    // PE exception directory (.pdata) rather than fail, since real x64 code always has .pdata.
    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = DllPath,
      Address = func.RVA,
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.InstructionPointer,
      SymbolDebugInfo = null
    });

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.IsNotNull(result.Range);
    Assert.AreEqual(FunctionBoundaryProvenance.ExceptionDirectory, result.Range.Value.Provenance);
    Assert.AreEqual(RuntimeFunctionKind.Amd64, result.Range.Value.RuntimeFunctionKind);
    Assert.IsNull(result.Range.Value.FunctionName); // No PDB -> no name, only bounds.
    StringAssert.Contains(result.QualifiedName, $"<unknown+0x{result.Range.Value.StartRva:X}>");

    // The .pdata-derived range must still contain the requested RVA (may legitimately differ from
    // the PDB's exact bounds e.g. due to padding, but must be a superset containing the address).
    Assert.IsTrue(func.RVA >= result.Range.Value.StartRva && func.RVA < result.Range.Value.EndRva);
    Assert.IsTrue(result.Instructions.Count > 0);
  }

  [TestMethod]
  public void RealBinary_ReturnAddressNormalization_OnRealCompilerEmittedCallSite() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = LoadProvider();
    if (provider == null) { Assert.Inconclusive("PDB load failed."); return; }

    var functions = provider.GetSortedFunctions();
    var bigFuncs = functions.Where(f => f.Size is > 200 and < 20000).OrderByDescending(f => f.Size).Take(50).ToList();
    if (bigFuncs.Count == 0) { Assert.Inconclusive("No suitable large function found."); return; }

    using var disassembler = Disassembler.CreateForBinary(DllPath, provider, null);
    Assert.IsNotNull(disassembler);

    // Find a direct call whose own instruction lies strictly inside its function (not the very
    // last instruction), so the "return address" (call.Rva + call.Size) is unambiguously still
    // within the same function's bounds.
    foreach (var func in bigFuncs) {
      var instructions = disassembler!.DisassembleToStructuredList(func.RVA, func.Size);

      foreach (var instr in instructions) {
        if (instr.Target is {IsCall: true} && instr.Rva + instr.Size < func.RVA + func.Size) {
          long returnAddressRva = instr.Rva + instr.Size;

          var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
            BinaryPath = DllPath,
            Address = returnAddressRva,
            AddressForm = NativeAddressForm.ModuleRva,
            FrameKind = FrameAddressKind.ReturnAddress,
            SymbolDebugInfo = provider
          });

          Assert.IsTrue(result.Success, result.ErrorMessage);
          Assert.IsTrue(result.WasNormalized,
            $"Expected normalization for return address 0x{returnAddressRva:X} following call at 0x{instr.Rva:X}.");
          Assert.AreEqual(instr.Rva, result.NormalizedRva);
          Assert.AreEqual(returnAddressRva, result.RequestedRva);
          return; // Found and verified one real call site -- sufficient for this determinism check.
        }
      }
    }

    Assert.Inconclusive("No non-terminal direct call instruction found among sampled functions.");
  }

  [TestMethod]
  public void RealBinary_ImageIdentityMismatch_FailsExplicitly() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    var wrongIdentity = new BinaryFileDescriptor {
      ImageName = TestDataHelper.MsoModuleName,
      TimeStamp = 1, // Deliberately wrong -- real timestamp is never 1.
      ImageSize = 1
    };

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = DllPath,
      ExpectedImageIdentity = wrongIdentity,
      Address = 0x1000,
      AddressForm = NativeAddressForm.ModuleRva
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.ImageIdentityMismatch, result.FailureReason);
  }
}
