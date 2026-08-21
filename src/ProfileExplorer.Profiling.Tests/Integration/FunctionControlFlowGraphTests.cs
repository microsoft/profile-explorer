// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Linq;
using System.Reflection.PortableExecutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="FunctionControlFlowGraph.Build"/> against hand-encoded x64
/// instruction sequences with known, by-hand-computed control flow: an if/else diamond and a
/// counted loop. Validates basic-block splitting, successor/predecessor wiring, immediate
/// dominators, natural-loop (back edge) detection, and that unreachable blocks are excluded from
/// dominance -- all against exact expected values computed from the fixture's own encoding, not
/// just "did it not throw".
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class FunctionControlFlowGraphTests {
  // ---------------------------------------------------------------------------------------------
  // If/else diamond (x64):
  //   [0x200] 39 D8            cmp eax, ebx
  //   [0x202] 74 07            je  +7   -> 0x20B (else)
  //   [0x204] B8 01 00 00 00   mov eax, 1        (then)
  //   [0x209] EB 05            jmp +5   -> 0x210 (merge)
  //   [0x20B] B8 02 00 00 00   mov eax, 2        (else)
  //   [0x210] C3               ret               (merge)
  // ---------------------------------------------------------------------------------------------
  private static byte[] BuildIfElseText() {
    return new byte[] {
      0x39, 0xD8,                   // cmp eax, ebx      @0x200
      0x74, 0x07,                   // je +7 -> 0x20B    @0x202
      0xB8, 0x01, 0x00, 0x00, 0x00, // mov eax, 1        @0x204
      0xEB, 0x05,                   // jmp +5 -> 0x210   @0x209
      0xB8, 0x02, 0x00, 0x00, 0x00, // mov eax, 2        @0x20B
      0xC3                          // ret               @0x210
    };
  }

  private static SyntheticPeFile BuildFixture(byte[] text, string fileName) {
    var sections = new List<SyntheticPeBuilder.Section> { new(".text", text, IsExecutableCode: true) };
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections);
    return new SyntheticPeFile(bytes, fileName);
  }

  private static int TextRva(byte[] text) => SyntheticPeBuilder.ComputeSectionRvas(1, new[] { text.Length })[0];

  [TestMethod]
  public void IfElseDiamond_ProducesExpectedBlocksEdgesAndDominators() {
    var text = BuildIfElseText();
    using var fixture = BuildFixture(text, "SyntheticIfElse.dll");
    int rva = TextRva(text);

    using var disasm = Disassembler.CreateForBinary(fixture.Path, null, null, enableSemanticDetail: true);
    Assert.IsNotNull(disasm);
    var instructions = disasm!.DisassembleToSemanticList(rva, text.Length);
    Assert.AreEqual(6, instructions.Count);

    var cfg = FunctionControlFlowGraph.Build(instructions, rva, rva + text.Length);
    Assert.IsNotNull(cfg);

    foreach (var block in cfg!.Blocks) {
      Console.WriteLine($"Block {block.Id} [0x{block.StartRva:X}..0x{block.EndRva:X}) " +
                        $"succ=[{string.Join(",", block.SuccessorIds)}] pred=[{string.Join(",", block.PredecessorIds)}] " +
                        $"return={block.EndsInReturn}");
    }

    Assert.AreEqual(4, cfg.Blocks.Count, "Expected 4 blocks: header, then, else, merge.");

    var header = cfg.Blocks.Single(b => b.StartRva == rva);
    var thenBlock = cfg.Blocks.Single(b => b.StartRva == rva + 0x04);
    var elseBlock = cfg.Blocks.Single(b => b.StartRva == rva + 0x0B);
    var mergeBlock = cfg.Blocks.Single(b => b.StartRva == rva + 0x10);

    Assert.AreEqual(cfg.EntryBlockId, header.Id);

    // Header: fallthrough to "then", je-target to "else".
    CollectionAssert.AreEquivalent(new[] { thenBlock.Id, elseBlock.Id }, header.SuccessorIds);

    // "then": only the jmp target (merge) -- no fallthrough after an unconditional jump.
    CollectionAssert.AreEquivalent(new[] { mergeBlock.Id }, thenBlock.SuccessorIds);

    // "else": plain fallthrough into merge (no explicit branch at its end).
    CollectionAssert.AreEquivalent(new[] { mergeBlock.Id }, elseBlock.SuccessorIds);

    // merge (ret): no successors -- an exit block.
    Assert.AreEqual(0, mergeBlock.SuccessorIds.Count);
    Assert.IsTrue(mergeBlock.IsExit);
    Assert.IsTrue(mergeBlock.EndsInReturn);

    // Dominators: header dominates everything (only entry point); merge's idom is header (not
    // "then" or "else" individually, since it has two distinct predecessors).
    Assert.AreEqual(header.Id, cfg.ImmediateDominator[thenBlock.Id]);
    Assert.AreEqual(header.Id, cfg.ImmediateDominator[elseBlock.Id]);
    Assert.AreEqual(header.Id, cfg.ImmediateDominator[mergeBlock.Id]);
    Assert.IsTrue(cfg.Dominates(header.Id, mergeBlock.Id));
    Assert.IsFalse(cfg.Dominates(thenBlock.Id, mergeBlock.Id), "'then' alone must not dominate merge (else bypasses it).");

    // No back edges anywhere in an if/else diamond -- no natural loops.
    Assert.AreEqual(0, cfg.Loops.Count);
  }

  // ---------------------------------------------------------------------------------------------
  // Counted loop (x64), with one unreachable trailing byte to verify dead-code exclusion:
  //   [0x200] B8 00 00 00 00   mov eax, 0                (entry)
  //   [0x205] 83 F8 0A         cmp eax, 0x0A              (loop header)
  //   [0x208] 7D 05            jge +5 -> 0x20F (exit)
  //   [0x20A] 83 C0 01         add eax, 1                 (loop body)
  //   [0x20D] EB F6            jmp -10 -> 0x205 (back edge to header)
  //   [0x20F] C3               ret                         (exit)
  //   [0x210] CC               int3 -- unreachable: nothing branches here, no fallthrough reaches it.
  // ---------------------------------------------------------------------------------------------
  private static byte[] BuildLoopText() {
    return new byte[] {
      0xB8, 0x00, 0x00, 0x00, 0x00, // mov eax, 0        @0x200
      0x83, 0xF8, 0x0A,             // cmp eax, 0x0A     @0x205
      0x7D, 0x05,                   // jge +5 -> 0x20F   @0x208
      0x83, 0xC0, 0x01,             // add eax, 1        @0x20A
      0xEB, 0xF6,                   // jmp -10 -> 0x205  @0x20D
      0xC3,                         // ret               @0x20F
      0xCC                          // int3 -- unreachable padding @0x210
    };
  }

  [TestMethod]
  public void CountedLoop_DetectsBackEdgeAndNaturalLoop_AndExcludesUnreachableBlock() {
    var text = BuildLoopText();
    using var fixture = BuildFixture(text, "SyntheticLoop.dll");
    int rva = TextRva(text);

    using var disasm = Disassembler.CreateForBinary(fixture.Path, null, null, enableSemanticDetail: true);
    Assert.IsNotNull(disasm);
    var instructions = disasm!.DisassembleToSemanticList(rva, text.Length);

    var cfg = FunctionControlFlowGraph.Build(instructions, rva, rva + text.Length);
    Assert.IsNotNull(cfg);

    foreach (var block in cfg!.Blocks) {
      Console.WriteLine($"Block {block.Id} [0x{block.StartRva:X}..0x{block.EndRva:X}) " +
                        $"succ=[{string.Join(",", block.SuccessorIds)}] pred=[{string.Join(",", block.PredecessorIds)}]");
    }

    var entryBlock = cfg.Blocks.Single(b => b.StartRva == rva);
    var headerBlock = cfg.Blocks.Single(b => b.StartRva == rva + 0x05);
    var bodyBlock = cfg.Blocks.Single(b => b.StartRva == rva + 0x0A);
    var exitBlock = cfg.Blocks.Single(b => b.StartRva == rva + 0x0F);
    // The unreachable int3 byte forms its own trailing block (nothing branches or falls into it).
    var deadBlock = cfg.Blocks.SingleOrDefault(b => b.StartRva == rva + 0x10);

    Assert.AreEqual(cfg.EntryBlockId, entryBlock.Id);
    CollectionAssert.AreEquivalent(new[] { headerBlock.Id }, entryBlock.SuccessorIds);
    CollectionAssert.AreEquivalent(new[] { bodyBlock.Id, exitBlock.Id }, headerBlock.SuccessorIds);
    CollectionAssert.AreEquivalent(new[] { headerBlock.Id }, bodyBlock.SuccessorIds); // Back edge.
    Assert.AreEqual(0, exitBlock.SuccessorIds.Count);

    Assert.AreEqual(entryBlock.Id, cfg.ImmediateDominator[headerBlock.Id]);
    Assert.AreEqual(headerBlock.Id, cfg.ImmediateDominator[bodyBlock.Id]);
    Assert.AreEqual(headerBlock.Id, cfg.ImmediateDominator[exitBlock.Id]);

    Assert.AreEqual(1, cfg.Loops.Count, "Expected exactly one natural loop.");
    var loop = cfg.Loops[0];
    Assert.AreEqual(headerBlock.Id, loop.HeaderBlockId);
    Assert.AreEqual(bodyBlock.Id, loop.BackEdgeSourceBlockId);
    CollectionAssert.AreEquivalent(new[] { headerBlock.Id, bodyBlock.Id }, loop.BodyBlockIds.ToList());

    if (deadBlock != null) {
      Assert.AreEqual(0, deadBlock.PredecessorIds.Count, "The int3 padding must have no predecessors.");
      Assert.IsFalse(cfg.ImmediateDominator.ContainsKey(deadBlock.Id),
        "Unreachable blocks must be excluded from dominance, not assigned a fabricated dominator.");
    }
  }

  [TestMethod]
  public void Build_EmptyInstructionList_ReturnsNull() {
    var cfg = FunctionControlFlowGraph.Build(new List<SemanticInstruction>(), 0x200, 0x210);
    Assert.IsNull(cfg);
  }
}
