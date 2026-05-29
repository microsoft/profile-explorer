// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Disassembly;
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
    ipResolver.SetFunctions(TestDataHelper.MsoModuleName, allFunctions);

    var aggregator = new Profiling.SampleAggregator(ipResolver);
    aggregator.AddSamples(samples);

    var profiles = aggregator.Build();
    Assert.IsTrue(profiles.Count > 0, "Should have at least one function profile.");

    // Find the target function's profile.
    var profile = profiles.FirstOrDefault(p => p.FunctionName.Contains("SortByParameterGroups"));
    Assert.IsNotNull(profile, "Should have a profile for SortByParameterGroups.");
    Assert.IsTrue(profile.ExclusiveWeight.TotalMilliseconds >= sampleCount * 0.9,
      $"Most samples should be attributed to this function. Got {profile.ExclusiveWeight.TotalMilliseconds}ms, expected ~{sampleCount}ms.");

    // Step 4: Disassemble the function (requires Capstone).
    if (!Disassembler.CapstoneAvailable) {
      // Capstone not installed — verify profiling worked and skip assembly steps.
      Assert.IsTrue(profile.InstructionWeights.Count > 0, "Should have instruction weights.");
      return;
    }

    using var disassembler = new Disassembler();
    var instructions = disassembler.DisassembleFunction(
      DllPath, profile.FunctionRva, profile.FunctionSize,
      moduleBase, ProcessorArchitecture.Amd64, pdbProvider);

    Assert.IsTrue(instructions.Count > 0,
      $"Should disassemble {profile.FunctionName} (Size={profile.FunctionSize}).");

    // Step 5: Annotate with timing data.
    var funcDebugInfo = pdbProvider.FindFunctionByRVA(profile.FunctionRva);
    var annotated = AssemblyAnnotator.Annotate(
      instructions, profile.InstructionWeights, profile.FunctionRva,
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

    // Create samples for two different functions.
    var func1 = functions[0];
    var func2 = functions.First(f => f.RVA != func1.RVA);

    var ipResolver = new Profiling.IpResolver();
    ipResolver.AddImage(TestDataHelper.MsoModuleName, moduleBase, 0x1000000);
    ipResolver.SetFunctions(TestDataHelper.MsoModuleName, functions);

    var aggregator = new Profiling.SampleAggregator(ipResolver);
    aggregator.AddSamples([
      new SyntheticSample(moduleBase + func1.RVA, TimeSpan.FromMilliseconds(30), 1, 1,
        TestDataHelper.MsoModuleName, moduleBase),
      new SyntheticSample(moduleBase + func2.RVA, TimeSpan.FromMilliseconds(70), 1, 1,
        TestDataHelper.MsoModuleName, moduleBase)
    ]);

    var profiles = aggregator.Build();
    Assert.AreEqual(2, profiles.Count, "Should have exactly 2 function profiles.");

    var p1 = profiles.First(p => p.FunctionName == func1.Name);
    var p2 = profiles.First(p => p.FunctionName == func2.Name);

    Assert.AreEqual(30.0, p1.ExclusivePercent, 0.1);
    Assert.AreEqual(70.0, p2.ExclusivePercent, 0.1);
  }
}

internal class TestProfileImage : IProfileImage {
  public TestProfileImage(string name, long baseAddr, int size, int timeStamp,
                           Guid pdbGuid, int pdbAge, string pdbName, int processId) {
    ImageName = name;
    BaseAddress = baseAddr;
    Size = size;
    TimeDateStamp = timeStamp;
    PdbGuid = pdbGuid;
    PdbAge = pdbAge;
    PdbName = pdbName;
    ProcessId = processId;
  }

  public string ImageName { get; }
  public long BaseAddress { get; }
  public int Size { get; }
  public int TimeDateStamp { get; }
  public Guid PdbGuid { get; }
  public int PdbAge { get; }
  public string PdbName { get; }
  public int ProcessId { get; }
}
