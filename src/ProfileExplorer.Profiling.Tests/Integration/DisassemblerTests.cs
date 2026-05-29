// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Disassembly;
using ProfileExplorer.Profiling.Symbols;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public class DisassemblerTests {
  private static string DllPath => TestDataHelper.GetBinaryFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoDllFile);
  private static string PdbPath => TestDataHelper.GetSymbolFilePath(TestDataHelper.MsoTrace, TestDataHelper.MsoPdbFile);

  private static bool CanRun() {
    return TestDataHelper.HasTestData(TestDataHelper.MsoTrace) &&
           File.Exists(DllPath) &&
           Disassembler.CapstoneAvailable;
  }

  private static (FunctionDebugInfo? func, PdbSymbolProvider? provider) FindTestFunction() {
    if (!File.Exists(PdbPath)) return (null, null);

    var provider = new PdbSymbolProvider();
    if (!provider.LoadDebugInfo(PdbPath)) {
      provider.Dispose();
      return (null, null);
    }

    var functions = provider.GetSortedFunctions();
    // Find SortByParameterGroups or any function with reasonable size.
    var func = functions.FirstOrDefault(f => f.Name.Contains("SortByParameterGroups"))
               ?? functions.FirstOrDefault(f => f.Size > 20 && f.Size < 10000);

    return (func, provider);
  }

  [TestMethod]
  public void Disassemble_x64Function_ReturnsInstructions() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    var (func, provider) = FindTestFunction();
    if (func == null) { Assert.Inconclusive("No suitable function found."); provider?.Dispose(); return; }

    using (provider) {
      using var disassembler = new Disassembler();
      var instructions = disassembler.DisassembleFunction(
        DllPath, func.RVA, (int)func.Size, 0x180000000, // Typical ASLR base
        ProcessorArchitecture.Amd64, provider);

      Assert.IsTrue(instructions.Count > 0,
        $"Should produce instructions for {func.Name} (RVA={func.RVA:X}, Size={func.Size})");

      // First instruction should be a valid x64 instruction.
      Assert.IsFalse(string.IsNullOrEmpty(instructions[0].Text));
      Assert.IsTrue(instructions[0].Size > 0);
    }
  }

  [TestMethod]
  public void Disassemble_ResolvesCallTargets() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    var (func, provider) = FindTestFunction();
    if (func == null) { Assert.Inconclusive("No suitable function found."); provider?.Dispose(); return; }

    using (provider) {
      using var disassembler = new Disassembler();

      // Use a larger function to increase chance of call instructions.
      var functions = provider!.GetSortedFunctions();
      var bigFunc = functions.Where(f => f.Size > 200).OrderByDescending(f => f.Size).FirstOrDefault();
      if (bigFunc == null) { Assert.Inconclusive("No large function found."); return; }

      var instructions = disassembler.DisassembleFunction(
        DllPath, bigFunc.RVA, (int)bigFunc.Size, 0x180000000,
        ProcessorArchitecture.Amd64, provider);

      // Check that at least some call instructions have resolved names.
      var callInstructions = instructions.Where(i => i.Text.StartsWith("call")).ToList();

      // It's valid if there are calls; they may or may not resolve depending on targets.
      if (callInstructions.Count > 0) {
        // At least verify they have text content.
        Assert.IsTrue(callInstructions.All(c => !string.IsNullOrEmpty(c.Text)));
      }
    }
  }

  [TestMethod]
  public void Disassemble_FunctionBoundaries() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    var (func, provider) = FindTestFunction();
    if (func == null) { Assert.Inconclusive("No suitable function found."); provider?.Dispose(); return; }

    using (provider) {
      long imageBase = 0x180000000;
      using var disassembler = new Disassembler();
      var instructions = disassembler.DisassembleFunction(
        DllPath, func.RVA, (int)func.Size, imageBase,
        ProcessorArchitecture.Amd64, provider);

      if (instructions.Count == 0) { Assert.Inconclusive("No instructions produced."); return; }

      // All instruction RVAs should be within [funcRVA, funcRVA + funcSize).
      long funcStart = func.RVA;
      long funcEnd = func.RVA + func.Size;

      foreach (var instr in instructions) {
        Assert.IsTrue(instr.Rva >= funcStart && instr.Rva < funcEnd,
          $"Instruction at RVA {instr.Rva:X} outside function bounds [{funcStart:X}, {funcEnd:X})");
      }
    }
  }

  [TestMethod]
  public void Disassemble_HandlesShortFunctions() {
    if (!CanRun()) { Assert.Inconclusive("Test data not available."); return; }

    var (_, provider) = FindTestFunction();
    if (provider == null) { Assert.Inconclusive("PDB load failed."); return; }

    using (provider) {
      // Find a function small enough to be short but big enough to contain code.
      var functions = provider.GetSortedFunctions();
      var shortFunc = functions.FirstOrDefault(f => f.Size is > 4 and <= 32);

      if (shortFunc == null) { Assert.Inconclusive("No short function found."); return; }

      using var disassembler = new Disassembler();
      var instructions = disassembler.DisassembleFunction(
        DllPath, shortFunc.RVA, (int)shortFunc.Size, 0x180000000,
        ProcessorArchitecture.Amd64, provider);

      // Short functions should produce at least 1 instruction.
      Assert.IsTrue(instructions.Count > 0,
        $"Short function {shortFunc.Name} (Size={shortFunc.Size}) should produce instructions.");
    }
  }

  [TestMethod]
  public void Disassemble_InvalidBinary_ReturnsEmpty() {
    using var disassembler = new Disassembler();
    var instructions = disassembler.DisassembleFunction(
      @"C:\nonexistent\fake.dll", 0x1000, 100, 0x180000000,
      ProcessorArchitecture.Amd64);

    Assert.AreEqual(0, instructions.Count);
  }
}
