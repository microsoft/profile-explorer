// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection.PortableExecutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="FunctionDataFlowAnalysis"/> against hand-encoded x64
/// instruction sequences with known, by-hand-computed data-flow facts: a straight-line register
/// dependency chain (proving reaching definitions, constant detection, and backward slicing all
/// work together) and an if/else diamond with disagreeing branch definitions (proving ambiguous
/// merges are reported honestly rather than guessed).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class FunctionDataFlowAnalysisTests {
  private static SyntheticPeFile BuildFixture(byte[] text, string fileName) {
    var sections = new List<SyntheticPeBuilder.Section> { new(".text", text, IsExecutableCode: true) };
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections);
    return new SyntheticPeFile(bytes, fileName);
  }

  private static int TextRva(byte[] text) => SyntheticPeBuilder.ComputeSectionRvas(1, new[] { text.Length })[0];

  // ---------------------------------------------------------------------------------------------
  // Straight-line dependency chain:
  //   [0x200] B8 10 00 00 00   mov eax, 0x10   (defines eax = constant 0x10)
  //   [0x205] 89 C3            mov ebx, eax    (defines ebx; reads eax)
  //   [0x207] 83 FB 00         cmp ebx, 0x0    (reads ebx)
  //   [0x20A] C3               ret
  // ---------------------------------------------------------------------------------------------
  private static byte[] BuildChainText() {
    return new byte[] {
      0xB8, 0x10, 0x00, 0x00, 0x00, // mov eax, 0x10
      0x89, 0xC3,                   // mov ebx, eax
      0x83, 0xFB, 0x00,             // cmp ebx, 0x0
      0xC3                          // ret
    };
  }

  [TestMethod]
  public void StraightLineChain_ReachingDefinitions_ConstantDetection_AndBackwardSlice() {
    var text = BuildChainText();
    using var fixture = BuildFixture(text, "SyntheticDataFlowChain.dll");
    int rva = TextRva(text);

    using var disasm = Disassembler.CreateForBinary(fixture.Path, null, null, enableSemanticDetail: true);
    Assert.IsNotNull(disasm);
    var instructions = disasm!.DisassembleToSemanticList(rva, text.Length);
    Assert.AreEqual(4, instructions.Count);

    var cfg = FunctionControlFlowGraph.Build(instructions, rva, rva + text.Length);
    Assert.IsNotNull(cfg);
    var dfa = FunctionDataFlowAnalysis.Build(cfg!);

    int movEaxIdx = dfa.FindInstructionIndexByRva(rva + 0x00)!.Value;
    int movEbxIdx = dfa.FindInstructionIndexByRva(rva + 0x05)!.Value;
    int cmpIdx = dfa.FindInstructionIndexByRva(rva + 0x07)!.Value;

    Console.WriteLine($"movEaxIdx={movEaxIdx} movEbxIdx={movEbxIdx} cmpIdx={cmpIdx}");

    // "mov ebx, eax" reads eax -- its reaching definition must be the immediate-load instruction.
    var eaxAtMovEbx = dfa.GetReachingDefinition(movEbxIdx, "eax");
    Assert.AreEqual(ReachingDefinitionState.Unique, eaxAtMovEbx.State);
    Assert.AreEqual(movEaxIdx, eaxAtMovEbx.DefiningInstructionIndex);

    // eax is a direct immediate load -> its value is provably constant 0x10 there.
    Assert.IsTrue(dfa.TryGetConstantValue(movEbxIdx, "eax", out long eaxValue));
    Assert.AreEqual(0x10, eaxValue);

    // "cmp ebx, 0" reads ebx -- its reaching definition must be "mov ebx, eax".
    var ebxAtCmp = dfa.GetReachingDefinition(cmpIdx, "ebx");
    Assert.AreEqual(ReachingDefinitionState.Unique, ebxAtCmp.State);
    Assert.AreEqual(movEbxIdx, ebxAtCmp.DefiningInstructionIndex);

    // ebx is NOT directly constant here: its definition is a register-to-register move, not an
    // immediate load -- multi-hop copy propagation is explicitly out of scope for this tier.
    Assert.IsFalse(dfa.TryGetConstantValue(cmpIdx, "ebx", out _));

    // Backward slice from the cmp should transitively include both prior defining instructions.
    var slice = dfa.BackwardSlice(cmpIdx);
    Console.WriteLine($"Slice: [{string.Join(",", slice)}]");
    CollectionAssert.AreEquivalent(new[] { movEaxIdx, movEbxIdx, cmpIdx }, slice);
  }

  // ---------------------------------------------------------------------------------------------
  // If/else diamond with disagreeing eax definitions on each branch, merging before a read:
  //   [0x200] 39 D8            cmp eax, ebx
  //   [0x202] 74 07            je  +7   -> 0x20B (else)
  //   [0x204] B8 01 00 00 00   mov eax, 1        (then)
  //   [0x209] EB 05            jmp +5   -> 0x210 (merge)
  //   [0x20B] B8 02 00 00 00   mov eax, 2        (else)
  //   [0x210] 83 F8 00         cmp eax, 0        (merge -- reads eax; ambiguous reaching def)
  //   [0x213] C3               ret
  // ---------------------------------------------------------------------------------------------
  private static byte[] BuildAmbiguousMergeText() {
    return new byte[] {
      0x39, 0xD8,                   // cmp eax, ebx
      0x74, 0x07,                   // je +7 -> 0x20B
      0xB8, 0x01, 0x00, 0x00, 0x00, // mov eax, 1
      0xEB, 0x05,                   // jmp +5 -> 0x210
      0xB8, 0x02, 0x00, 0x00, 0x00, // mov eax, 2
      0x83, 0xF8, 0x00,             // cmp eax, 0
      0xC3                          // ret
    };
  }

  [TestMethod]
  public void AmbiguousMergeAcrossBranches_IsReportedHonestly_NotGuessed() {
    var text = BuildAmbiguousMergeText();
    using var fixture = BuildFixture(text, "SyntheticDataFlowAmbiguous.dll");
    int rva = TextRva(text);

    using var disasm = Disassembler.CreateForBinary(fixture.Path, null, null, enableSemanticDetail: true);
    Assert.IsNotNull(disasm);
    var instructions = disasm!.DisassembleToSemanticList(rva, text.Length);

    var cfg = FunctionControlFlowGraph.Build(instructions, rva, rva + text.Length);
    Assert.IsNotNull(cfg);
    Assert.AreEqual(0, cfg!.Loops.Count, "This diamond has no back edges.");

    var dfa = FunctionDataFlowAnalysis.Build(cfg);

    int mergeCmpIdx = dfa.FindInstructionIndexByRva(rva + 0x10)!.Value;

    var eaxAtMerge = dfa.GetReachingDefinition(mergeCmpIdx, "eax");
    Console.WriteLine($"eax reaching def at merge: {eaxAtMerge.State}");
    Assert.AreEqual(ReachingDefinitionState.Ambiguous, eaxAtMerge.State,
      "eax is defined differently on the 'then' (1) and 'else' (2) branches -- must be Ambiguous, not silently one of them.");

    Assert.IsFalse(dfa.TryGetConstantValue(mergeCmpIdx, "eax", out _),
      "An ambiguous reaching definition must never be reported as a known constant.");

    // Backward slice must stop at the ambiguous merge -- it should not include either branch's
    // "mov eax, N" as if one were authoritative.
    var slice = dfa.BackwardSlice(mergeCmpIdx);
    Console.WriteLine($"Slice: [{string.Join(",", slice)}]");
    CollectionAssert.AreEquivalent(new[] { mergeCmpIdx }, slice);
  }

  [TestMethod]
  public void BackwardSlice_RespectsMaxInstructionsBound() {
    var text = BuildChainText();
    using var fixture = BuildFixture(text, "SyntheticDataFlowBounded.dll");
    int rva = TextRva(text);

    using var disasm = Disassembler.CreateForBinary(fixture.Path, null, null, enableSemanticDetail: true);
    var instructions = disasm!.DisassembleToSemanticList(rva, text.Length);
    var cfg = FunctionControlFlowGraph.Build(instructions, rva, rva + text.Length);
    var dfa = FunctionDataFlowAnalysis.Build(cfg!);

    int cmpIdx = dfa.FindInstructionIndexByRva(rva + 0x07)!.Value;

    var boundedSlice = dfa.BackwardSlice(cmpIdx, maxInstructions: 1);
    Assert.AreEqual(1, boundedSlice.Count, "A maxInstructions=1 bound must stop expansion immediately.");
  }
}
