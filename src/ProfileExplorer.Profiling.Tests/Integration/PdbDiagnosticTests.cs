using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public class PdbDiagnosticTests
{
    [TestMethod]
    public void DiagnoseRealWindowsPdb()
    {
        string? pdbPath = Environment.GetEnvironmentVariable("PE_TEST_PDB_PATH");
        if (string.IsNullOrEmpty(pdbPath) || !File.Exists(pdbPath))
        {
            Assert.Inconclusive("Set PE_TEST_PDB_PATH to a valid PDB path to run this test.");
            return;
        }

        // Set msdia path relative to repo layout: src/external/msdia140.dll
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string msdiaPath = Path.Combine(repoRoot, "src", "external", "msdia140.dll");
        if (File.Exists(msdiaPath))
            PdbSymbolProvider.MsDiaPath = msdiaPath;

        using var provider = new PdbSymbolProvider();
        Console.WriteLine($"DIA error before: {PdbSymbolProvider.DiaRegistrationError}");

        bool loaded = provider.LoadDebugInfo(pdbPath);
        Console.WriteLine($"Loaded: {loaded}");
        Console.WriteLine($"DIA error after: {PdbSymbolProvider.DiaRegistrationError}");

        Assert.IsTrue(loaded, $"PDB load failed: {PdbSymbolProvider.DiaRegistrationError}");

        var funcs = provider.GetSortedFunctions();
        Console.WriteLine($"Total functions: {funcs.Count}");
        Assert.IsTrue(funcs.Count > 0, "No functions enumerated");

        // Show first 5
        foreach (var f in funcs.Take(5))
            Console.WriteLine($"  [{f.RVA:X8}] {f.Name} (size={f.Size})");

        // Search for MeasureCore
        var matches = funcs.Where(f => f.Name.Contains("MeasureCore", StringComparison.OrdinalIgnoreCase)).ToList();
        Console.WriteLine($"MeasureCore matches: {matches.Count}");
        foreach (var m in matches.Take(5))
            Console.WriteLine($"  [{m.RVA:X8}] {m.Name} (size={m.Size})");

        Assert.IsTrue(matches.Count > 0, "MeasureCore not found in PDB functions");
    }
}
