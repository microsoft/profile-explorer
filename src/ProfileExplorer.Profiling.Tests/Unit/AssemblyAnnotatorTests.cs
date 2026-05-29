// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Disassembly;

namespace ProfileExplorer.Profiling.Tests.Unit;

[TestClass]
[TestCategory("Unit")]
public class AssemblyAnnotatorTests {
  private static IReadOnlyList<DisassembledInstruction> CreateInstructions(long baseAddress, long baseRva, int count) {
    var instructions = new List<DisassembledInstruction>();
    for (int i = 0; i < count; i++) {
      instructions.Add(new DisassembledInstruction(
        baseAddress + i * 4,
        baseRva + i * 4,
        i == 0 ? "push  rbp" : i == count - 1 ? "ret" : $"mov  eax, [{i * 4}]",
        4));
    }

    return instructions;
  }

  [TestMethod]
  public void AnnotatesHotInstructions_WithTimePercent() {
    long funcRva = 0x100;
    var instructions = CreateInstructions(0x7FF00100, funcRva, 5);

    // 50ms on first instruction, 50ms on third.
    var weights = new Dictionary<long, TimeSpan> {
      [0] = TimeSpan.FromMilliseconds(50),
      [8] = TimeSpan.FromMilliseconds(50)
    };

    var result = AssemblyAnnotator.Annotate(instructions, weights, funcRva, null, null,
      ProcessorArchitecture.Amd64, 1.0, 10);

    Assert.AreEqual(5, result.Lines.Count);
    Assert.AreEqual(50.0, result.Lines[0].Percent, 0.1);
    Assert.AreEqual(50.0, result.Lines[2].Percent, 0.1);
    Assert.AreEqual(0.0, result.Lines[1].Percent, 0.1);
  }

  [TestMethod]
  public void ColdInstructions_NoAnnotation() {
    long funcRva = 0x100;
    var instructions = CreateInstructions(0x7FF00100, funcRva, 3);

    // No weight on second instruction.
    var weights = new Dictionary<long, TimeSpan> {
      [0] = TimeSpan.FromMilliseconds(10)
    };

    var result = AssemblyAnnotator.Annotate(instructions, weights, funcRva, null, null,
      ProcessorArchitecture.Amd64, 1.0, 10);

    // Full text should NOT have [Time(%)] for the cold instruction.
    var coldLine = result.Lines[1];
    Assert.AreEqual(0.0, coldLine.Percent, 0.01);
    Assert.IsFalse(result.FullText.Contains("mov  eax, [4]") &&
                   result.FullText.Contains("[Time(%):") &&
                   result.FullText.Split("Time(%)").Length > 2);
  }

  [TestMethod]
  public void HotLineExtraction_AboveThreshold() {
    long funcRva = 0x100;
    var instructions = CreateInstructions(0x7FF00100, funcRva, 10);

    var weights = new Dictionary<long, TimeSpan> {
      [0] = TimeSpan.FromMilliseconds(50),   // 50%
      [4] = TimeSpan.FromMilliseconds(30),   // 30%
      [8] = TimeSpan.FromMilliseconds(15),   // 15%
      [12] = TimeSpan.FromMilliseconds(5),   //  5%
    };

    var result = AssemblyAnnotator.Annotate(instructions, weights, funcRva, null, null,
      ProcessorArchitecture.Amd64, 10.0, 10);

    // MinPercent=10 → only 50%, 30%, 15% qualify.
    Assert.AreEqual(3, result.HotLines.Count);
  }

  [TestMethod]
  public void HotLineExtraction_OrderedByPercent() {
    long funcRva = 0x100;
    var instructions = CreateInstructions(0x7FF00100, funcRva, 5);

    var weights = new Dictionary<long, TimeSpan> {
      [0] = TimeSpan.FromMilliseconds(10),
      [4] = TimeSpan.FromMilliseconds(50),
      [8] = TimeSpan.FromMilliseconds(30),
    };

    var result = AssemblyAnnotator.Annotate(instructions, weights, funcRva, null, null,
      ProcessorArchitecture.Amd64, 1.0, 10);

    Assert.IsTrue(result.HotLines[0].Percent >= result.HotLines[1].Percent);
    Assert.IsTrue(result.HotLines[1].Percent >= result.HotLines[2].Percent);
  }

  [TestMethod]
  public void HotLineExtraction_CappedByMaxHotLines() {
    long funcRva = 0x100;
    var instructions = CreateInstructions(0x7FF00100, funcRva, 20);

    var weights = new Dictionary<long, TimeSpan>();
    for (int i = 0; i < 20; i++) {
      weights[i * 4] = TimeSpan.FromMilliseconds(5);
    }

    var result = AssemblyAnnotator.Annotate(instructions, weights, funcRva, null, null,
      ProcessorArchitecture.Amd64, 1.0, 3); // Max 3 hot lines.

    Assert.AreEqual(3, result.HotLines.Count);
  }

  [TestMethod]
  public void FullText_ContainsTimingAnnotation() {
    long funcRva = 0x100;
    var instructions = new List<DisassembledInstruction> {
      new(0x7FF00100, 0x100, "call  CIconCache::GetIcon", 5)
    };

    var weights = new Dictionary<long, TimeSpan> {
      [0] = TimeSpan.FromMilliseconds(58.18)
    };

    var result = AssemblyAnnotator.Annotate(instructions, weights, funcRva, null, null,
      ProcessorArchitecture.Amd64, 1.0, 10);

    Assert.IsTrue(result.FullText.Contains("[Time(%): 100.00%"));
    Assert.IsTrue(result.FullText.Contains("call  CIconCache::GetIcon"));
  }

  [TestMethod]
  public void EmptyWeights_NoHotLines() {
    long funcRva = 0x100;
    var instructions = CreateInstructions(0x7FF00100, funcRva, 5);

    var result = AssemblyAnnotator.Annotate(instructions, new Dictionary<long, TimeSpan>(),
      funcRva, null, null, ProcessorArchitecture.Amd64, 1.0, 10);

    Assert.AreEqual(0, result.HotLines.Count);
    Assert.AreEqual(5, result.Lines.Count);
  }
}
