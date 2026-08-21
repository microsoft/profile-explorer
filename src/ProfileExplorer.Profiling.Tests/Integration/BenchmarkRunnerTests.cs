// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Symbols;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="BenchmarkRunner"/> -- the reverse-engineering enhancement
/// plan's benchmark *harness infrastructure*, stratified by <see cref="SymbolAvailabilityTier"/>.
/// Uses three real, already-available fixtures to exercise all three tiers:
/// <list type="bullet">
/// <item><see cref="SymbolAvailabilityTier.FullPdb"/>: testlib.dll/testlib.pdb (self-authored,
/// Stage 5 fixture) -- a full private PDB with real signatures.</item>
/// <item><see cref="SymbolAvailabilityTier.PublicSymbolsOnlyPdb"/>: the MSO trace fixture, which
/// direct DIA enumeration (during Stage 5's development) proved has zero SymTagFunction records.</item>
/// <item><see cref="SymbolAvailabilityTier.NoPdb"/>: the same testlib.dll, analyzed with no PDB at
/// all and a pre-known RVA -- the real-world scenario this plan is built around, where FUN AI
/// already has the address from an ETW stack/allocation site.</item>
/// </list>
/// This intentionally measures static-evidence *coverage*, not AI pseudocode accuracy -- see
/// <see cref="BenchmarkReport"/>'s remarks for why that scoring step needs an LLM + human rubric
/// this test suite cannot fabricate.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class BenchmarkRunnerTests {
  [ClassInitialize]
  public static void ClassInit(TestContext _) {
    string assemblyDir = Path.GetDirectoryName(typeof(BenchmarkRunnerTests).Assembly.Location)!;
    var dir = new DirectoryInfo(assemblyDir);

    while (dir != null) {
      foreach (string candidateDir in new[] { dir.FullName, Path.Combine(dir.FullName, "src") }) {
        string candidate = Path.Combine(candidateDir, "external", "msdia140.dll");

        if (File.Exists(candidate)) {
          PdbSymbolProvider.MsDiaPath = candidate;
          return;
        }
      }

      dir = dir.Parent;
    }
  }

  private static ISymbolDebugInfo? LoadPdb(string pdbPath) {
    var provider = new PdbSymbolProvider();
    return provider.LoadDebugInfo(pdbPath) ? provider : null;
  }

  [TestMethod]
  public void Run_ThreeSymbolAvailabilityTiers_ProducesExpectedCoverageStratification() {
    string testlibDll = TestDataHelper.GetBinaryFilePath("SignatureTestLib", "testlib.dll");
    string testlibPdb = TestDataHelper.GetSymbolFilePath("SignatureTestLib", "testlib.pdb");

    if (!File.Exists(testlibDll) || !File.Exists(testlibPdb)) {
      Assert.Inconclusive("testlib fixture not available.");
      return;
    }

    // Discover CopyBuffer's RVA once (out-of-band, matching how a real caller would already know
    // an address from ETW) so the NoPdb case doesn't need any name resolution at all. CopyBuffer
    // (not AddIntegers) is used here because dumpbin /unwindinfo confirms it has a real .pdata
    // entry: AddIntegers/ComputeRatio are trivial leaf functions (no stack frame, no calls) that
    // the x64 ABI does not require unwind info for at all -- a genuine, previously-undiscovered
    // coverage gap this benchmark surfaced (see plan.md), not a bug in this test.
    long copyBufferRva;
    using (var provider = new PdbSymbolProvider()) {
      if (!provider.LoadDebugInfo(testlibPdb)) { Assert.Inconclusive("PDB load failed (DIA SDK?)."); return; }
      var func = provider.GetSortedFunctions().FirstOrDefault(f => f.Name.Contains("CopyBuffer"));
      if (func == null) { Assert.Inconclusive("CopyBuffer not found."); return; }
      copyBufferRva = func.RVA;
    }

    var cases = new List<BenchmarkCase> {
      new("FullPdb-AddIntegers", testlibDll, testlibPdb, "AddIntegers", SymbolAvailabilityTier.FullPdb),
      new("NoPdb-CopyBuffer", testlibDll, null, "CopyBuffer", SymbolAvailabilityTier.NoPdb, copyBufferRva)
    };

    string msoPdb = TestDataHelper.GetSymbolFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoPdbFile);
    string msoDll = TestDataHelper.GetBinaryFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoDllFile);
    bool msoAvailable = TestDataHelper.HasTestData(TestDataHelper.MsoTrace) && File.Exists(msoPdb) && File.Exists(msoDll);

    if (msoAvailable) {
      // Short name, not the fully-qualified TestDataHelper.MsoTopFunction: PdbSymbolProviderTests'
      // own FindFunctionByName_ReturnsKnownFunction test already discovered the PDB doesn't match
      // the fully-qualified name reliably, and falls back to a "Contains" search on the short name.
      cases.Add(new BenchmarkCase("PublicSymbolsOnly-TopFunction", msoDll, msoPdb,
        "SortByParameterGroups", SymbolAvailabilityTier.PublicSymbolsOnlyPdb));
    }

    var report = BenchmarkRunner.Run(cases, LoadPdb);

    foreach (var result in report.CaseResults) {
      Console.WriteLine($"{result.CaseName} [{result.ExpectedTier}]: Success={result.Success} " +
                        $"HasSignature={result.HasSignature} Bounds={result.BoundaryProvenance} " +
                        $"Instructions={result.InstructionCount} Blocks={result.BlockCount} " +
                        $"Resolved={result.ResolvedCallOrReferenceCount} Failure={result.FailureReason}");
    }

    foreach (var summary in report.TierSummaries) {
      Console.WriteLine($"Tier {summary.Tier}: {summary.SucceededCount}/{summary.CaseCount} succeeded " +
                        $"({summary.SuccessRate:P0}), {summary.HasSignatureCount} with signature " +
                        $"({summary.SignatureAvailabilityRate:P0})");
    }

    var fullPdbResult = report.CaseResults.Single(r => r.CaseName == "FullPdb-AddIntegers");
    Assert.IsTrue(fullPdbResult.Success, fullPdbResult.FailureReason);
    Assert.IsTrue(fullPdbResult.HasSignature, "A full private PDB must yield a resolved signature.");
    Assert.AreEqual(FunctionBoundaryProvenance.PdbSymbols, fullPdbResult.BoundaryProvenance);
    Assert.IsNotNull(fullPdbResult.AssemblyOnlyText);
    Assert.IsNotNull(fullPdbResult.EnhancedPromptText);

    var noPdbResult = report.CaseResults.Single(r => r.CaseName == "NoPdb-CopyBuffer");
    Assert.IsTrue(noPdbResult.Success, noPdbResult.FailureReason);
    Assert.IsFalse(noPdbResult.HasSignature, "With no PDB, no signature should be fabricated.");
    Assert.AreEqual(FunctionBoundaryProvenance.ExceptionDirectory, noPdbResult.BoundaryProvenance,
      "With no PDB, bounds must come from the .pdata fallback, not PDB symbols.");

    if (msoAvailable) {
      var publicOnlyResult = report.CaseResults.SingleOrDefault(r => r.CaseName == "PublicSymbolsOnly-TopFunction");

      if (publicOnlyResult is { Success: true }) {
        Assert.IsFalse(publicOnlyResult.HasSignature,
          "The MSO fixture PDB has zero SymTagFunction records (confirmed during Stage 5 development) " +
          "-- no signature should ever be fabricated for it.");
      }
    }

    // Coverage-rate stratification: FullPdb and NoPdb tiers must be reported separately, not
    // averaged into one misleading number -- exactly the survivorship-bias risk the plan flags.
    var fullPdbTier = report.TierSummaries.Single(s => s.Tier == SymbolAvailabilityTier.FullPdb);
    var noPdbTier = report.TierSummaries.Single(s => s.Tier == SymbolAvailabilityTier.NoPdb);
    Assert.AreEqual(1.0, fullPdbTier.SignatureAvailabilityRate);
    Assert.AreEqual(0.0, noPdbTier.SignatureAvailabilityRate);
  }

  [TestMethod]
  public void Run_UnresolvableFunctionName_ReportsExplicitFailureNotException() {
    string testlibDll = TestDataHelper.GetBinaryFilePath("SignatureTestLib", "testlib.dll");
    string testlibPdb = TestDataHelper.GetSymbolFilePath("SignatureTestLib", "testlib.pdb");

    if (!File.Exists(testlibDll) || !File.Exists(testlibPdb)) {
      Assert.Inconclusive("testlib fixture not available.");
      return;
    }

    var cases = new List<BenchmarkCase> {
      new("Unresolvable", testlibDll, testlibPdb, "ThisFunctionDoesNotExist", SymbolAvailabilityTier.FullPdb)
    };

    var report = BenchmarkRunner.Run(cases, LoadPdb);

    Assert.AreEqual(1, report.CaseResults.Count);
    Assert.IsFalse(report.CaseResults[0].Success);
    Assert.IsNotNull(report.CaseResults[0].FailureReason);
  }
}
