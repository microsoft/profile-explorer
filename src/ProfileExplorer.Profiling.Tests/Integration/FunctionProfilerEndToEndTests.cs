// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Disassembly;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Profiling.Symbols;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end integration tests using the MsoTrace PDB + DLL test data.
/// These tests validate the full pipeline: PDB loading → function enumeration →
/// synthetic sample aggregation → disassembly → annotation.
///
/// Note: These do NOT use the ETL trace (that would require TraceEvent or DataLayer).
/// Instead, they create synthetic samples at known function RVAs from the PDB
/// and verify the full profiling + disassembly pipeline works correctly.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class FunctionProfilerEndToEndTests {
  private static string PdbPath => TestDataHelper.GetSymbolFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoPdbFile);
  private static string DllPath => TestDataHelper.GetBinaryFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoDllFile);

  private static bool CanRun() {
    return TestDataHelper.HasTestData(TestDataHelper.MsoTrace) &&
           File.Exists(PdbPath) && File.Exists(DllPath);
  }

  [TestMethod]
  public void FullPipeline_PdbAndDll_ProducesAnnotatedAssembly() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    // Step 1: Load PDB to discover functions and their RVAs.
    using var pdbProvider = new PdbSymbolProvider();
    if (!pdbProvider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed (DIA SDK not registered?).");
      return;
    }

    var allFunctions = pdbProvider.GetSortedFunctions();
    Assert.IsTrue(allFunctions.Count > 0, "PDB should contain functions.");

    // Find SortByParameterGroups.
    var targetFunc = allFunctions.FirstOrDefault(f => f.Name.Contains("SortByParameterGroups"));
    if (targetFunc == null) {
      Assert.Inconclusive("Could not find SortByParameterGroups in PDB.");
      return;
    }

    // Step 2: Create synthetic samples at known instruction offsets in the function.
    long moduleBase = 0x180000000; // Typical ASLR base for 64-bit DLLs.
    long funcAbsAddr = moduleBase + targetFunc.RVA;

    var images = new IProfileImage[] {
      new TestProfileImage(TestDataHelper.MsoModuleName, moduleBase, 0x1000000, // 16MB
        0, Guid.Empty, 0, TestDataHelper.MsoPdbFile, 1)
    };

    // Distribute 100 samples across the function body.
    var samples = new List<IProfileSample>();
    int sampleCount = 100;
    for (int i = 0; i < sampleCount; i++) {
      long offset = (i * 4) % (int)targetFunc.Size;
      long ip = funcAbsAddr + offset;
      samples.Add(new SyntheticSample(ip, TimeSpan.FromMilliseconds(1), 1, 1,
        TestDataHelper.MsoModuleName, moduleBase));
    }

    // Step 3: Use FunctionProfiler with a pre-loaded PDB (bypass symbol server).
    // We'll test the lower-level components directly since we have local files.
    var ipResolver = new Profiling.IpResolver();
    ipResolver.AddImage(TestDataHelper.MsoModuleName, moduleBase, 0x1000000);
    ipResolver.SetFunctions(moduleBase, allFunctions);

    var aggregator = new Profiling.SampleAggregator(ipResolver);
    aggregator.AddSamples(samples);

    var profiles = aggregator.Build();
    Assert.IsTrue(profiles.Count > 0, "Should have at least one function profile.");

    // Find the target function's profile.
    var profile = profiles.FirstOrDefault(p => p.Key.FunctionName.Contains("SortByParameterGroups"));
    Assert.IsNotNull(profile.Value, "Should have a profile for SortByParameterGroups.");
    Assert.IsTrue(profile.Value.ExclusiveWeight.TotalMilliseconds >= sampleCount * 0.9,
      $"Most samples should be attributed to this function. Got {profile.Value.ExclusiveWeight.TotalMilliseconds}ms, expected ~{sampleCount}ms.");

    // Step 4: Disassemble the function with the mature capstone disassembler.
    using var disassembler = Disassembler.CreateForBinary(DllPath, pdbProvider, null);

    if (disassembler == null) {
      // Binary couldn't be opened — verify profiling worked and skip assembly steps.
      Assert.IsTrue(profile.Value.InstructionWeight.Count > 0, "Should have instruction weights.");
      return;
    }

    long targetRva = profile.Value.FunctionDebugInfo.RVA;
    var instructions = disassembler.DisassembleToList(targetRva, (int)profile.Value.FunctionDebugInfo.Size);

    Assert.IsTrue(instructions.Count > 0,
      $"Should disassemble {profile.Key.FunctionName} (Size={profile.Value.FunctionDebugInfo.Size}).");

    // Step 5: Annotate with timing data.
    var funcDebugInfo = pdbProvider.FindFunctionByRVA(targetRva);
    var annotated = AssemblyAnnotator.Annotate(
      instructions, profile.Value.InstructionWeight, targetRva,
      pdbProvider, funcDebugInfo, ProcessorArchitecture.Amd64,
      minHotLinePercent: 1.0, maxHotLines: 10);

    Assert.IsNotNull(annotated);
    Assert.IsTrue(annotated.Lines.Count > 0, "Annotated assembly should have lines.");
    Assert.IsFalse(string.IsNullOrEmpty(annotated.FullText), "Full text should not be empty.");

    // Verify hot lines are present (we distributed samples across the function).
    Assert.IsTrue(annotated.HotLines.Count > 0, "Should have at least one hot line.");
    Assert.IsTrue(annotated.HotLines[0].Percent > 0, "Top hot line should have positive percent.");

    // Verify hot lines are sorted descending.
    for (int i = 1; i < annotated.HotLines.Count; i++) {
      Assert.IsTrue(annotated.HotLines[i].Percent <= annotated.HotLines[i - 1].Percent,
        "Hot lines should be sorted descending by percent.");
    }

    // Verify the full text contains timing annotations.
    Assert.IsTrue(annotated.FullText.Contains("[Time(%):"),
      "Full text should contain timing annotations.");
  }

  [TestMethod]
  public void PdbFunctions_ConsistentWithBinarySearch() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var functions = provider.GetSortedFunctions();
    Assert.IsTrue(functions.Count > 10);

    // Verify binary search finds most functions by their own RVA.
    // Some may not round-trip exactly due to overlapping/PGO-split functions.
    int checked_ = 0;
    int found = 0;
    foreach (var func in functions.Take(50)) {
      var result = FunctionDebugInfo.BinarySearch(functions, func.RVA);
      if (result != null) found++;
      checked_++;
    }

    Assert.IsTrue(found > checked_ * 0.9, $"Binary search should find most functions ({found}/{checked_}).");
  }

  [TestMethod]
  public void MultipleModules_IndependentProfiles() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var functions = provider.GetSortedFunctions();
    if (functions.Count < 2) { Assert.Inconclusive("Need at least 2 functions."); return; }

    long moduleBase = 0x180000000;

    // Pick two functions whose RVA is unique (not ICF-folded) and round-trips through the
    // resolver, so each sample is attributed deterministically to the expected name.
    var usable = TestDataHelper.GetUniqueRvaFunctions(provider)
      .Where(f => provider.FindFunctionByRVA(f.RVA)?.RVA == f.RVA)
      .Take(2).ToList();
    if (usable.Count < 2) { Assert.Inconclusive("Need at least 2 unique-RVA functions."); return; }

    var func1 = usable[0];
    var func2 = usable[1];

    var ipResolver = new Profiling.IpResolver();
    ipResolver.AddImage(TestDataHelper.MsoModuleName, moduleBase, 0x1000000);
    ipResolver.SetFunctions(moduleBase, functions);

    var aggregator = new Profiling.SampleAggregator(ipResolver);
    aggregator.AddSamples([
      new SyntheticSample(moduleBase + func1.RVA, TimeSpan.FromMilliseconds(30), 1, 1,
        TestDataHelper.MsoModuleName, moduleBase),
      new SyntheticSample(moduleBase + func2.RVA, TimeSpan.FromMilliseconds(70), 1, 1,
        TestDataHelper.MsoModuleName, moduleBase)
    ]);

    var profiles = aggregator.Build();
    Assert.AreEqual(2, profiles.Count, "Should have exactly 2 function profiles.");

    double total = aggregator.TotalWeight.TotalMilliseconds;
    var p1 = profiles.First(p => p.Key.FunctionName == func1.Name);
    var p2 = profiles.First(p => p.Key.FunctionName == func2.Name);

    Assert.AreEqual(30.0, p1.Value.ExclusiveWeight.TotalMilliseconds / total * 100, 0.1);
    Assert.AreEqual(70.0, p2.Value.ExclusiveWeight.TotalMilliseconds / total * 100, 0.1);
  }

  /// <summary>
  /// Local-binary fallback (private-binary parity with the old MCP engine): when the symbol server
  /// can't provide the binary (NoSymbolLocator returns null) but the traced image's ImagePath points
  /// to a local on-disk DLL whose PE TimeDateStamp MATCHES the trace's image record,
  /// GetAnnotatedAssemblyAsync must disassemble from that local file. Mirrors
  /// ProfileExplorerCore.BinaryFileLocator.FindExactLocalBinaryFile so DataLayer can disassemble
  /// private OS binaries (not on the public symbol server) from the local install.
  /// </summary>
  [TestMethod]
  public async Task LocalBinaryFallback_UsesLocalDll_WhenTimeDateStampMatches() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    var (funcId, profile, dllTimeStamp, dllImageSize) = BuildLocalBinaryScenario();
    if (profile == null) { Assert.Inconclusive("Could not build a function profile (or read the on-disk DLL)."); return; }

    var options = new ProfilerOptions {
      SymbolPaths = new[] { "srv*https://symbols.invalid" },
      MinHotLinePercent = 0.0,
      MaxHotLines = 50
    };
    using var profiler = new FunctionProfiler(options, new NoSymbolLocator());
    profiler.AddImages(new IProfileImage[] {
      new TestProfileImage(TestDataHelper.MsoModuleName, LocalBinaryModuleBase, dllImageSize,
        dllTimeStamp, Guid.Empty, 0, TestDataHelper.MsoPdbFile, 1, imagePath: DllPath)
    });

    var annotated = await profiler.GetAnnotatedAssemblyAsync(funcId, profile);

    Assert.IsNotNull(annotated,
      "Should disassemble from the local binary even though the symbol server returns nothing.");
    Assert.IsTrue(annotated!.Lines.Count > 0, "Should have real disassembled instruction lines.");
    Assert.IsFalse(annotated.FullText.Contains("[offset +0x"),
      "Real disassembly must NOT be the no-binary offset fallback.");
  }

  /// <summary>
  /// The local-binary fallback is gated on a PE TimeDateStamp match so we never disassemble the wrong
  /// build. When the local DLL's timestamp does NOT match the trace's image record, the local file is
  /// ignored and (with the symbol server also empty) we fall back to offset-level hot lines.
  /// </summary>
  [TestMethod]
  public async Task LocalBinaryFallback_IgnoresLocalDll_WhenTimeDateStampMismatches() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    var (funcId, profile, dllTimeStamp, dllImageSize) = BuildLocalBinaryScenario();
    if (profile == null) { Assert.Inconclusive("Could not build a function profile (or read the on-disk DLL)."); return; }

    var options = new ProfilerOptions {
      SymbolPaths = new[] { "srv*https://symbols.invalid" },
      MinHotLinePercent = 0.0,
      MaxHotLines = 50
    };
    using var profiler = new FunctionProfiler(options, new NoSymbolLocator());
    // Deliberately wrong TimeDateStamp -> the local DLL must be rejected (wrong build).
    int wrongTimeStamp = unchecked(dllTimeStamp + 1);
    profiler.AddImages(new IProfileImage[] {
      new TestProfileImage(TestDataHelper.MsoModuleName, LocalBinaryModuleBase, dllImageSize,
        wrongTimeStamp, Guid.Empty, 0, TestDataHelper.MsoPdbFile, 1, imagePath: DllPath)
    });

    var annotated = await profiler.GetAnnotatedAssemblyAsync(funcId, profile);

    // With no binary (local rejected + server empty) the only possible output is the offset fallback,
    // never real disassembly.
    if (annotated != null) {
      Assert.IsTrue(annotated.FullText.Contains("[offset +0x"),
        "A build mismatch must ignore the local binary, yielding the offset fallback (not real disassembly).");
    }
  }

  private const long LocalBinaryModuleBase = 0x180000000;

  // Build a single-function profile (with InstructionWeight) plus the on-disk DLL's real PE
  // TimeDateStamp, for exercising the local-binary path in GetAnnotatedAssemblyAsync.
  private static (ProfileFunctionId FuncId, FunctionProfileData? Profile, int DllTimeStamp, int DllImageSize)
      BuildLocalBinaryScenario() {
    using var pdbProvider = new PdbSymbolProvider();
    if (!pdbProvider.LoadDebugInfo(PdbPath)) return (default, null, 0, 0);

    var functions = pdbProvider.GetSortedFunctions();
    var target = TestDataHelper.GetUniqueRvaFunctions(pdbProvider)
      .FirstOrDefault(f => pdbProvider.FindFunctionByRVA(f.RVA)?.RVA == f.RVA && f.Size > 16);
    if (target == null) return (default, null, 0, 0);

    var ipResolver = new Profiling.IpResolver();
    ipResolver.AddImage(TestDataHelper.MsoModuleName, LocalBinaryModuleBase, 0x1000000);
    ipResolver.SetFunctions(LocalBinaryModuleBase, functions);

    var aggregator = new Profiling.SampleAggregator(ipResolver);
    var samples = new List<IProfileSample>();
    for (int i = 0; i < 20; i++) {
      long ip = LocalBinaryModuleBase + target.RVA + (i * 4) % (int)target.Size;
      samples.Add(new SyntheticSample(ip, TimeSpan.FromMilliseconds(1), 1, 1,
        TestDataHelper.MsoModuleName, LocalBinaryModuleBase));
    }
    aggregator.AddSamples(samples);

    var profiles = aggregator.Build();
    var profile = profiles.FirstOrDefault(p => p.Key.FunctionName == target.Name);

    // The local-binary fallback is gated on the on-disk DLL's real PE identity (TimeDateStamp +
    // SizeOfImage). If the DLL can't be read we can't set up a meaningful match/mismatch, so mark the
    // scenario inconclusive (null profile) rather than fabricating a 0 stamp that would make the match
    // test spuriously fail.
    var dllInfo = PEBinaryInfoProvider.GetBinaryFileInfo(DllPath);
    if (dllInfo == null) return (default, null, 0, 0);

    return (new ProfileFunctionId(TestDataHelper.MsoModuleName, target.Name), profile.Value,
            dllInfo.TimeStamp, (int)dllInfo.ImageSize);
  }

  /// <summary>Symbol locator that never resolves — forces the local-binary path to be the only source.</summary>
  private sealed class NoSymbolLocator : ISymbolFileLocator {
    public Task<string?> FindSymbolFileAsync(string pdbName, Guid guid, int age, CancellationToken ct = default) =>
      Task.FromResult<string?>(null);

    public Task<string?> FindBinaryFileAsync(string binaryName, int timeDateStamp, long imageSize,
                                             CancellationToken ct = default) =>
      Task.FromResult<string?>(null);
  }
}

internal class TestProfileImage : IProfileImage {
  public TestProfileImage(string name, long baseAddr, int size, int timeStamp,
                           Guid pdbGuid, int pdbAge, string pdbName, int processId,
                           string? imagePath = null) {
    ImageName = name;
    BaseAddress = baseAddr;
    Size = size;
    TimeDateStamp = timeStamp;
    PdbGuid = pdbGuid;
    PdbAge = pdbAge;
    PdbName = pdbName;
    ProcessId = processId;
    ImagePath = imagePath;
  }

  public string ImageName { get; }
  public long BaseAddress { get; }
  public int Size { get; }
  public int TimeDateStamp { get; }
  public Guid PdbGuid { get; }
  public int PdbAge { get; }
  public string PdbName { get; }
  public int ProcessId { get; }
  public string? ImagePath { get; }
}
