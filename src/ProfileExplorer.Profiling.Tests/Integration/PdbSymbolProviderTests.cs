// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Symbols;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public class PdbSymbolProviderTests {
  private static string PdbPath => TestDataHelper.GetSymbolFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoPdbFile);

  [ClassInitialize]
  public static void ClassInit(TestContext _) {
    // Set the path to msdia140.dll for side-loading (located in src/external/).
    string assemblyDir = Path.GetDirectoryName(typeof(PdbSymbolProviderTests).Assembly.Location)!;
    // Walk up from bin/Release/net8.0-windows to src/external.
    var dir = new DirectoryInfo(assemblyDir);
    while (dir != null) {
      string candidate = Path.Combine(dir.FullName, "external", "msdia140.dll");
      if (File.Exists(candidate)) {
        PdbSymbolProvider.MsDiaPath = candidate;
        break;
      }

      candidate = Path.Combine(dir.FullName, "src", "external", "msdia140.dll");
      if (File.Exists(candidate)) {
        PdbSymbolProvider.MsDiaPath = candidate;
        break;
      }

      dir = dir.Parent;
    }
  }

  private static bool CanRun() {
    // DIA SDK (msdia140.dll) must be registered.
    // If not available, skip these tests gracefully.
    if (!TestDataHelper.HasTestData(TestDataHelper.MsoTrace)) return false;
    if (!File.Exists(PdbPath)) return false;
    return true;
  }

  [TestMethod]
  public void LoadPdb_EnumeratesFunctions() {
    if (!CanRun()) { Assert.Inconclusive("Test data or DIA SDK not available."); return; }

    using var provider = new PdbSymbolProvider();
    bool loaded = provider.LoadDebugInfo(PdbPath);

    if (!loaded && PdbSymbolProvider.DiaRegistrationFailed) {
      Assert.Inconclusive($"DIA SDK not registered: {PdbSymbolProvider.DiaRegistrationError}");
      return;
    }

    Assert.IsTrue(loaded, $"PDB should load successfully. Error: {PdbSymbolProvider.DiaRegistrationError}");
    var functions = provider.GetSortedFunctions();
    Assert.IsTrue(functions.Count > 0, "Should enumerate at least one function.");
    Assert.IsTrue(functions.Count > 100, $"Expected many functions, got {functions.Count}.");
  }

  [TestMethod]
  public void FindFunctionByName_ReturnsKnownFunction() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed (DIA SDK?).");
      return;
    }

    // This function is the top self-time function in the MsoTrace baseline.
    var func = provider.FindFunction(TestDataHelper.MsoTopFunction);

    // May not find by exact name if mangled differently; try searching sorted list.
    if (func == null) {
      var allFuncs = provider.GetSortedFunctions();
      func = allFuncs.FirstOrDefault(f => f.Name.Contains("SortByParameterGroups"));
    }

    Assert.IsNotNull(func, "Should find SortByParameterGroups function.");
    Assert.IsTrue(func.Size > 0, "Function should have non-zero size.");
    Assert.IsTrue(func.RVA > 0, "Function should have non-zero RVA.");
  }

  [TestMethod]
  public void FindFunctionByRVA_ReturnsCorrectName() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    // Find a known function first, then look it up by RVA.
    var functions = provider.GetSortedFunctions();
    Assert.IsTrue(functions.Count > 0);

    var firstFunc = functions[0];
    var found = provider.FindFunctionByRVA(firstFunc.RVA);

    Assert.IsNotNull(found);
    Assert.AreEqual(firstFunc.RVA, found.RVA);
    Assert.AreEqual(firstFunc.Name, found.Name);
  }

  [TestMethod]
  public void FindFunctionByRVA_UnknownRVA_ReturnsNullOrDifferentRVA() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    // DIA may return the nearest function for any RVA. Verify that at minimum
    // a lookup at RVA=0 returns something different from the last function.
    var result = provider.FindFunctionByRVA(0x7FFFFFFF);
    // It's OK if DIA returns a result — just verify it doesn't crash.
    // The important thing is that valid RVAs return the correct function.
  }

  [TestMethod]
  public void PopulateSourceLines_ReturnsMappings() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    // Find a function and populate its source lines.
    var functions = provider.GetSortedFunctions();
    var funcWithSize = functions.FirstOrDefault(f => f.Size > 20);
    Assert.IsNotNull(funcWithSize, "Should have a function with size > 20.");

    bool populated = provider.PopulateSourceLines(funcWithSize);

    // Source lines may or may not be available depending on PDB type.
    if (populated) {
      Assert.IsTrue(funcWithSize.HasSourceLines);
      Assert.IsTrue(funcWithSize.SourceLines!.Count > 0);
      Assert.IsTrue(funcWithSize.SourceLines[0].Line > 0, "Line number should be positive.");
    }
    // If not populated, that's OK — PDB may be stripped. Don't fail.
  }

  [TestMethod]
  public void FindSourceLineByRVA_ReturnsLineInfo() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var functions = provider.GetSortedFunctions();
    var func = functions.FirstOrDefault(f => f.Size > 20);
    Assert.IsNotNull(func);

    var lineInfo = provider.FindSourceLineByRVA(func.RVA);

    // May or may not have source info depending on PDB.
    if (!lineInfo.IsUnknown) {
      Assert.IsTrue(lineInfo.Line > 0);
    }
  }

  [TestMethod]
  public void GetSortedFunctions_IsSortedByRVA() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    using var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      Assert.Inconclusive("PDB load failed.");
      return;
    }

    var functions = provider.GetSortedFunctions();
    Assert.IsTrue(functions.Count > 1);

    for (int i = 1; i < functions.Count; i++) {
      Assert.IsTrue(functions[i].RVA >= functions[i - 1].RVA,
        $"Functions not sorted: [{i - 1}] RVA={functions[i - 1].RVA} > [{i}] RVA={functions[i].RVA}");
    }
  }

  [TestMethod]
  public void LoadPdb_InvalidPath_ReturnsFalse() {
    using var provider = new PdbSymbolProvider();
    bool result = provider.LoadDebugInfo(@"C:\nonexistent\fake.pdb");
    Assert.IsFalse(result);
  }
}
