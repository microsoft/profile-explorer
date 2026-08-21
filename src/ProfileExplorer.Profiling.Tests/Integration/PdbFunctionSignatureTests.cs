// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Symbols;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="PdbSymbolProvider.TryGetFunctionSignature"/> against a small,
/// purpose-built native DLL (<c>testlib.dll</c>/<c>testlib.pdb</c>) compiled with a full private
/// PDB via the local MSVC toolchain (<c>cl /LD /Zi /Od</c>). Unlike the MSO trace fixture used
/// elsewhere in this suite -- which turned out, via direct verification during this feature's
/// development, to have zero SymTagFunction records (a public-symbols-only PDB, common for shipped
/// binaries) -- this fixture has known, exactly verifiable signatures, so these tests assert
/// precise expected values rather than only structural invariants.
/// <para>
/// Fixture source:
/// <code>
/// extern "C" __declspec(dllexport) int __cdecl AddIntegers(int a, int b);
/// extern "C" __declspec(dllexport) double __stdcall ComputeRatio(double numerator, double denominator, int scale);
/// extern "C" __declspec(dllexport) void __cdecl CopyBuffer(char* destination, const char* source, unsigned int length);
/// </code>
/// </para>
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class PdbFunctionSignatureTests {
  private const string TestCaseName = "SignatureTestLib";
  private static string DllPath => TestDataHelper.GetBinaryFilePath(TestCaseName, "testlib.dll");
  private static string PdbPath => TestDataHelper.GetSymbolFilePath(TestCaseName, "testlib.pdb");

  private static bool CanRun() => File.Exists(DllPath) && File.Exists(PdbPath);

  private static FunctionDebugInfo? FindByName(PdbSymbolProvider provider, string name) {
    return provider.GetSortedFunctions().FirstOrDefault(f => f.Name == name);
  }

  [TestMethod]
  public void TryGetFunctionSignature_CdeclIntFunction_MatchesExactKnownSignature() {
    if (!CanRun()) { Assert.Inconclusive("testlib fixture not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed (DIA SDK?).");
      return;
    }

    var func = FindByName(provider, "AddIntegers");
    Assert.IsNotNull(func, "AddIntegers should be found in the fixture PDB.");

    var signature = provider.TryGetFunctionSignature(func!);
    Assert.IsNotNull(signature, "AddIntegers has a full private PDB record and must resolve.");

    Console.WriteLine($"{signature!.CallingConvention} {signature.ReturnTypeName} AddIntegers(" +
                      string.Join(", ", signature.Parameters.Select(p => $"{p.TypeName} {p.Name}")) + ")");

    Assert.AreEqual("int", signature.ReturnTypeName);
    Assert.AreEqual("__cdecl", signature.CallingConvention);
    Assert.AreEqual(2, signature.Parameters.Count);
    Assert.AreEqual("int", signature.Parameters[0].TypeName);
    Assert.AreEqual("a", signature.Parameters[0].Name);
    Assert.AreEqual("int", signature.Parameters[1].TypeName);
    Assert.AreEqual("b", signature.Parameters[1].Name);
  }

  [TestMethod]
  public void TryGetFunctionSignature_StdcallMixedTypeFunction_MatchesExactKnownSignature() {
    if (!CanRun()) { Assert.Inconclusive("testlib fixture not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var func = FindByName(provider, "ComputeRatio");
    Assert.IsNotNull(func, "ComputeRatio should be found in the fixture PDB.");

    var signature = provider.TryGetFunctionSignature(func!);
    Assert.IsNotNull(signature);

    Console.WriteLine($"{signature!.CallingConvention} {signature.ReturnTypeName} ComputeRatio(" +
                      string.Join(", ", signature.Parameters.Select(p => $"{p.TypeName} {p.Name}")) + ")");

    // x64 has a single unified calling convention -- __stdcall/__fastcall are no-ops the compiler
    // silently accepts and ignores on x64, so DIA correctly reports __cdecl here despite the
    // source declaring __stdcall. See TryGetFunctionSignature_X86_DistinctCallingConventions
    // below for a build where the keyword actually takes effect and is distinguishable.
    Assert.AreEqual("double", signature.ReturnTypeName);
    Assert.AreEqual("__cdecl", signature.CallingConvention); // __stdcall collapses to __cdecl on x64.
    Assert.AreEqual(3, signature.Parameters.Count);
    Assert.AreEqual("double", signature.Parameters[0].TypeName);
    Assert.AreEqual("numerator", signature.Parameters[0].Name);
    Assert.AreEqual("double", signature.Parameters[1].TypeName);
    Assert.AreEqual("denominator", signature.Parameters[1].Name);
    Assert.AreEqual("int", signature.Parameters[2].TypeName);
    Assert.AreEqual("scale", signature.Parameters[2].Name);
  }

  [TestMethod]
  public void TryGetFunctionSignature_VoidReturnWithPointerParameters_MatchesExactKnownSignature() {
    if (!CanRun()) { Assert.Inconclusive("testlib fixture not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var func = FindByName(provider, "CopyBuffer");
    Assert.IsNotNull(func, "CopyBuffer should be found in the fixture PDB.");

    var signature = provider.TryGetFunctionSignature(func!);
    Assert.IsNotNull(signature);

    Console.WriteLine($"{signature!.CallingConvention} {signature.ReturnTypeName} CopyBuffer(" +
                      string.Join(", ", signature.Parameters.Select(p => $"{p.TypeName} {p.Name}")) + ")");

    Assert.AreEqual("void", signature.ReturnTypeName);
    Assert.AreEqual("__cdecl", signature.CallingConvention);
    Assert.AreEqual(3, signature.Parameters.Count);
    Assert.AreEqual("char*", signature.Parameters[0].TypeName);
    Assert.AreEqual("destination", signature.Parameters[0].Name);
    Assert.AreEqual("char*", signature.Parameters[1].TypeName); // const-qualifier not tracked by this renderer.
    Assert.AreEqual("source", signature.Parameters[1].Name);
    Assert.AreEqual("unsigned int", signature.Parameters[2].TypeName);
    Assert.AreEqual("length", signature.Parameters[2].Name);
  }

  /// <summary>
  /// ARM64 build of the same fixture source -- the primary target architecture alongside x64 for
  /// modern Windows. Validates that PDB signature extraction (return type, parameters, calling
  /// convention rendering) works correctly for ARM64 binaries, not just x86/x64.
  /// </summary>
  [TestMethod]
  public void TryGetFunctionSignature_Arm64Build_MatchesExactKnownSignatures() {
    string dllPath = TestDataHelper.GetBinaryFilePath(TestCaseName, "testlibarm64.dll");
    string pdbPath = TestDataHelper.GetSymbolFilePath(TestCaseName, "testlibarm64.pdb");

    if (!File.Exists(dllPath) || !File.Exists(pdbPath)) {
      Assert.Inconclusive("ARM64 testlib fixture not available.");
      return;
    }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(pdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var addFunc = provider.GetSortedFunctions().FirstOrDefault(f => f.Name.Contains("AddIntegers"));
    Assert.IsNotNull(addFunc, "AddIntegers should be found in the ARM64 fixture PDB.");

    var addSig = provider.TryGetFunctionSignature(addFunc!);
    Assert.IsNotNull(addSig);
    Console.WriteLine($"{addSig!.CallingConvention} {addSig.ReturnTypeName} AddIntegers(" +
                      string.Join(", ", addSig.Parameters.Select(p => $"{p.TypeName} {p.Name}")) + ")");

    Assert.AreEqual("int", addSig.ReturnTypeName);
    Assert.AreEqual(2, addSig.Parameters.Count);
    Assert.AreEqual("int", addSig.Parameters[0].TypeName);
    Assert.AreEqual("a", addSig.Parameters[0].Name);
    Assert.AreEqual("int", addSig.Parameters[1].TypeName);
    Assert.AreEqual("b", addSig.Parameters[1].Name);
    // Calling convention naming isn't asserted to an exact MSVC keyword here: ARM64 (like x64) has
    // a single unified AAPCS64-derived ABI, and RenderCallingConvention's known-keyword table is
    // sourced from x86/x64 CV_call_e values -- an ARM64-specific code may legitimately render as an
    // explicit "<callingconvention:N>" placeholder rather than "__cdecl". Either is honest; a blank
    // or null value would not be.
    Assert.IsFalse(string.IsNullOrWhiteSpace(addSig.CallingConvention));

    var copyFunc = provider.GetSortedFunctions().FirstOrDefault(f => f.Name.Contains("CopyBuffer"));
    Assert.IsNotNull(copyFunc, "CopyBuffer should be found in the ARM64 fixture PDB.");

    var copySig = provider.TryGetFunctionSignature(copyFunc!);
    Assert.IsNotNull(copySig);
    Console.WriteLine($"{copySig!.CallingConvention} {copySig.ReturnTypeName} CopyBuffer(" +
                      string.Join(", ", copySig.Parameters.Select(p => $"{p.TypeName} {p.Name}")) + ")");

    Assert.AreEqual("void", copySig.ReturnTypeName);
    Assert.AreEqual(3, copySig.Parameters.Count);
    Assert.AreEqual("char*", copySig.Parameters[0].TypeName);
    Assert.AreEqual("unsigned int", copySig.Parameters[2].TypeName);
  }

  [TestMethod]
  public void TryGetFunctionSignature_UnknownRva_ReturnsNull() {
    if (!CanRun()) { Assert.Inconclusive("testlib fixture not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var signature = provider.TryGetFunctionSignature(FunctionDebugInfo.Unknown);
    Assert.IsNull(signature);
  }

  [TestMethod]
  public void TryGetFunctionSignature_NoPdbLoaded_ReturnsNull() {
    using var provider = new PdbSymbolProvider();
    var signature = provider.TryGetFunctionSignature(new FunctionDebugInfo("test", 0x1000, 16));
    Assert.IsNull(signature);
  }

  /// <summary>
  /// x86 build of the same fixture source: unlike x64 (which has a single unified calling
  /// convention and silently ignores __stdcall/__fastcall), x86 Windows genuinely distinguishes
  /// __cdecl and __stdcall at the ABI level (caller-cleans-stack vs. callee-cleans-stack), so this
  /// is what actually exercises RenderCallingConvention's non-__cdecl mapping. x86 remains a
  /// relevant target despite Windows 11 being 64-bit-only, since WOW64 still runs 32-bit code.
  /// </summary>
  [TestMethod]
  public void TryGetFunctionSignature_X86Build_DistinctCallingConventionsAreReported() {
    string dllPath = TestDataHelper.GetBinaryFilePath(TestCaseName, "testlib32.dll");
    string pdbPath = TestDataHelper.GetSymbolFilePath(TestCaseName, "testlib32.pdb");

    if (!File.Exists(dllPath) || !File.Exists(pdbPath)) {
      Assert.Inconclusive("x86 testlib fixture not available.");
      return;
    }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(pdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var cdeclFunc = FindByName(provider, "_AddIntegers");
    var stdcallFunc = FindByName(provider, "_ComputeRatio@20"); // x86 __stdcall decoration: @<arg-bytes>.

    // Decorated-name lookup is brittle across compiler versions; fall back to a name-contains scan.
    cdeclFunc ??= provider.GetSortedFunctions().FirstOrDefault(f => f.Name.Contains("AddIntegers"));
    stdcallFunc ??= provider.GetSortedFunctions().FirstOrDefault(f => f.Name.Contains("ComputeRatio"));

    Assert.IsNotNull(cdeclFunc, "AddIntegers should be found in the x86 fixture PDB.");
    Assert.IsNotNull(stdcallFunc, "ComputeRatio should be found in the x86 fixture PDB.");

    var cdeclSig = provider.TryGetFunctionSignature(cdeclFunc!);
    var stdcallSig = provider.TryGetFunctionSignature(stdcallFunc!);

    Console.WriteLine($"{cdeclFunc!.Name} -> {cdeclSig?.CallingConvention}");
    Console.WriteLine($"{stdcallFunc!.Name} -> {stdcallSig?.CallingConvention}");

    Assert.IsNotNull(cdeclSig);
    Assert.IsNotNull(stdcallSig);
    Assert.AreEqual("__cdecl", cdeclSig!.CallingConvention);
    Assert.AreEqual("__stdcall", stdcallSig!.CallingConvention);
    Assert.AreNotEqual(cdeclSig.CallingConvention, stdcallSig.CallingConvention,
      "x86 must distinguish __cdecl from __stdcall (unlike x64, where both collapse to the same ABI).");
  }

  /// <summary>
  /// Documents (rather than asserts a bug in) a real, verified fixture characteristic: the MSO
  /// trace PDB used by <see cref="PdbSymbolProviderTests"/> elsewhere in this suite has zero
  /// SymTagFunction records (confirmed via direct DIA enumeration during this feature's
  /// development) -- a public-symbols-only PDB, common for shipped binaries. TryGetFunctionSignature
  /// correctly returns null for every function in that fixture as a result; this is not testable
  /// against real expected values there, which is exactly why the testlib fixture above exists.
  /// </summary>
  [TestMethod]
  public void TryGetFunctionSignature_PublicSymbolOnlyPdb_ReturnsNullRatherThanFabricating() {
    string msoPdbPath = TestDataHelper.GetSymbolFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoPdbFile);

    if (!TestDataHelper.HasTestData(TestDataHelper.MsoTrace) || !File.Exists(msoPdbPath)) {
      Assert.Inconclusive("MSO trace fixture not available.");
      return;
    }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(msoPdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var functions = provider.GetSortedFunctions().Where(f => f.Size > 0).Take(50).ToList();
    Assert.IsTrue(functions.Count > 0);

    foreach (var func in functions) {
      var signature = provider.TryGetFunctionSignature(func);
      Assert.IsNull(signature, $"{func.Name}: expected null (no SymTagFunction record in this public-only PDB), " +
                               "not a fabricated signature.");
    }
  }
}
