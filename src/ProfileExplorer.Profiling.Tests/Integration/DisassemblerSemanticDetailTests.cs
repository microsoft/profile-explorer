// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Linq;
using System.Reflection.PortableExecutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="Disassembler.DisassembleToSemanticList"/> against hand-built
/// synthetic PE images (via <see cref="SyntheticPeBuilder"/>). Validates the first tier of the
/// semantic instruction model added for the reverse-engineering enhancement plan:
/// <list type="bullet">
/// <item>Exact combined implicit + explicit register read/write facts via Capstone's
/// <c>cs_regs_access</c>, for x64 (whose encoding is precise and hand-verifiable) and, more
/// shallowly, ARM64.</item>
/// <item>Structured control-transfer classification (call/unconditional jump/conditional
/// branch/return), for both x64 and ARM64.</item>
/// <item>That <see cref="SemanticInstruction.HasRegisterAccessDetail"/> is false and register lists
/// are empty -- never fabricated -- when the disassembler was created without semantic detail
/// enabled, while group classification (which doesn't depend on Capstone detail mode) still works.</item>
/// </list>
/// No PDB/.pdata is needed: unlike <see cref="NativeAddressDisassemblerSyntheticTests"/>, these
/// tests call <see cref="Disassembler.DisassembleToSemanticList(long, long)"/> directly with known
/// RVA/size, bypassing function-bounds resolution entirely.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class DisassemblerSemanticDetailTests {
  // ---------------------------------------------------------------------------------------------
  // x64 fixture: a deliberately simple, hand-verifiable instruction sequence.
  //   [0] 89 D8                mov eax, ebx        (reads ebx, writes eax)
  //   [2] 39 D8                cmp eax, ebx        (reads eax+ebx, writes eflags)
  //   [4] 74 00                je +0               (conditional branch)
  //   [6] EB 00                jmp +0              (unconditional jump)
  //   [8] E8 00 00 00 00       call +0 (rel32)     (call)
  //   [13] C3                  ret                 (return)
  // ---------------------------------------------------------------------------------------------
  private static byte[] BuildAmd64Text() {
    return new byte[] {
      0x89, 0xD8,                   // mov eax, ebx
      0x39, 0xD8,                   // cmp eax, ebx
      0x74, 0x00,                   // je +0
      0xEB, 0x00,                   // jmp +0
      0xE8, 0x00, 0x00, 0x00, 0x00, // call +0
      0xC3                          // ret
    };
  }

  private static SyntheticPeFile BuildAmd64Fixture() {
    var text = BuildAmd64Text();
    var sections = new List<SyntheticPeBuilder.Section> { new(".text", text, IsExecutableCode: true) };
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections);
    return new SyntheticPeFile(bytes, "SyntheticAmd64SemanticDetail.dll");
  }

  private static int Amd64TextRva(SyntheticPeFile fixture) {
    // Single-section image -> the .text RVA is whatever ComputeSectionRvas assigns for 1 section.
    return SyntheticPeBuilder.ComputeSectionRvas(1, new[] { BuildAmd64Text().Length })[0];
  }

  [TestMethod]
  public void Amd64_SemanticDetail_ReportsExactRegisterAccessAndGroups() {
    using var fixture = BuildAmd64Fixture();
    int rva = Amd64TextRva(fixture);

    using var disasm = Disassembler.CreateForBinary(fixture.Path, null, null, enableSemanticDetail: true);
    Assert.IsNotNull(disasm, "Should create a disassembler for a valid synthetic binary.");

    var instructions = disasm!.DisassembleToSemanticList(rva, BuildAmd64Text().Length);
    Assert.AreEqual(6, instructions.Count, "Expected exactly 6 decoded instructions.");

    foreach (var ins in instructions) {
      Console.WriteLine($"+0x{ins.Rva:X} {ins.Mnemonic} {ins.OperandText} " +
                        $"groups={ins.Groups} read=[{string.Join(",", ins.RegistersRead)}] " +
                        $"written=[{string.Join(",", ins.RegistersWritten)}]");
    }

    // mov eax, ebx: reads ebx, writes eax, no control-transfer groups.
    var mov = instructions[0];
    StringAssert.StartsWith(mov.Mnemonic, "mov");
    Assert.IsTrue(mov.HasRegisterAccessDetail);
    Assert.AreEqual(InstructionGroupFlags.None, mov.Groups);
    Assert.IsTrue(mov.RegistersRead.Any(r => r.Equals("ebx", StringComparison.OrdinalIgnoreCase)),
      $"Expected 'ebx' in reads: [{string.Join(",", mov.RegistersRead)}]");
    Assert.IsTrue(mov.RegistersWritten.Any(r => r.Equals("eax", StringComparison.OrdinalIgnoreCase)),
      $"Expected 'eax' in writes: [{string.Join(",", mov.RegistersWritten)}]");

    // cmp eax, ebx: reads eax+ebx, writes rflags (implicit). Capstone reports the full 64-bit
    // "rflags" name for the flags register in 64-bit disassemble mode, regardless of the 32-bit
    // operand width used here -- confirmed against the real decode, not assumed.
    var cmp = instructions[1];
    StringAssert.StartsWith(cmp.Mnemonic, "cmp");
    Assert.IsTrue(cmp.HasRegisterAccessDetail);
    Assert.IsTrue(cmp.RegistersRead.Any(r => r.Equals("eax", StringComparison.OrdinalIgnoreCase)));
    Assert.IsTrue(cmp.RegistersRead.Any(r => r.Equals("ebx", StringComparison.OrdinalIgnoreCase)));
    Assert.IsTrue(cmp.RegistersWritten.Any(r => r.Equals("rflags", StringComparison.OrdinalIgnoreCase)),
      $"Expected 'rflags' in writes: [{string.Join(",", cmp.RegistersWritten)}]");

    // je +0: conditional branch only.
    var je = instructions[2];
    StringAssert.StartsWith(je.Mnemonic, "j");
    Assert.IsTrue(je.IsConditionalBranch);
    Assert.IsFalse(je.IsUnconditionalJump);
    Assert.IsFalse(je.IsCall);
    Assert.IsFalse(je.IsReturn);

    // jmp +0: unconditional jump only.
    var jmp = instructions[3];
    StringAssert.StartsWith(jmp.Mnemonic, "jmp");
    Assert.IsTrue(jmp.IsUnconditionalJump);
    Assert.IsFalse(jmp.IsConditionalBranch);

    // call +0: call only.
    var call = instructions[4];
    StringAssert.StartsWith(call.Mnemonic, "call");
    Assert.IsTrue(call.IsCall);
    Assert.IsFalse(call.IsReturn);

    // ret: return only.
    var ret = instructions[5];
    StringAssert.StartsWith(ret.Mnemonic, "ret");
    Assert.IsTrue(ret.IsReturn);
    Assert.IsFalse(ret.IsCall);
  }

  [TestMethod]
  public void Amd64_WithoutSemanticDetail_GroupsStillWork_ButRegisterAccessIsAbsentNotEmpty() {
    using var fixture = BuildAmd64Fixture();
    int rva = Amd64TextRva(fixture);

    // enableSemanticDetail defaults to false -- matches every pre-existing call site unmodified.
    using var disasm = Disassembler.CreateForBinary(fixture.Path, null, null);
    Assert.IsNotNull(disasm);

    var instructions = disasm!.DisassembleToSemanticList(rva, BuildAmd64Text().Length);
    Assert.AreEqual(6, instructions.Count);

    var call = instructions[4];
    StringAssert.StartsWith(call.Mnemonic, "call");
    Assert.IsTrue(call.IsCall, "Group classification must not depend on Capstone detail mode.");

    foreach (var ins in instructions) {
      Assert.IsFalse(ins.HasRegisterAccessDetail,
        "HasRegisterAccessDetail must be false when semantic detail was not enabled.");
      Assert.AreEqual(0, ins.RegistersRead.Count, "Register facts must be absent, not fabricated.");
      Assert.AreEqual(0, ins.RegistersWritten.Count);
    }
  }

  // ---------------------------------------------------------------------------------------------
  // ARM64 fixture: reuses the well-known encodings from NativeAddressDisassemblerSyntheticTests
  // (nop, bl, ret) plus hand-computed "b" (unconditional) and "b.eq" (conditional) branches.
  //   [0]  1F 20 03 D5   nop
  //   [4]  00 00 00 94   bl #0        (call; implicitly writes the link register)
  //   [8]  00 00 00 14   b #0         (unconditional branch: 000101 + imm26=0 -> 0x14000000)
  //   [12] 00 00 00 54   b.eq #0      (conditional branch: 0101010 0 imm19=0 0 cond=0000(EQ) -> 0x54000000)
  //   [16] C0 03 5F D6   ret
  // ---------------------------------------------------------------------------------------------
  private static byte[] BuildArm64Text() {
    return new byte[] {
      0x1F, 0x20, 0x03, 0xD5, // nop
      0x00, 0x00, 0x00, 0x94, // bl #0
      0x00, 0x00, 0x00, 0x14, // b #0
      0x00, 0x00, 0x00, 0x54, // b.eq #0
      0xC0, 0x03, 0x5F, 0xD6  // ret
    };
  }

  private static SyntheticPeFile BuildArm64Fixture() {
    var text = BuildArm64Text();
    var sections = new List<SyntheticPeBuilder.Section> { new(".text", text, IsExecutableCode: true) };
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Arm64, sections);
    return new SyntheticPeFile(bytes, "SyntheticArm64SemanticDetail.dll");
  }

  private static int Arm64TextRva(SyntheticPeFile fixture) {
    return SyntheticPeBuilder.ComputeSectionRvas(1, new[] { BuildArm64Text().Length })[0];
  }

  [TestMethod]
  public void Arm64_SemanticDetail_ReportsGroupsAndRegisterAccess() {
    using var fixture = BuildArm64Fixture();
    int rva = Arm64TextRva(fixture);

    using var disasm = Disassembler.CreateForBinary(fixture.Path, null, null, enableSemanticDetail: true);
    Assert.IsNotNull(disasm, "Should create a disassembler for a valid synthetic binary.");

    var instructions = disasm!.DisassembleToSemanticList(rva, BuildArm64Text().Length);
    Assert.AreEqual(5, instructions.Count, "Expected exactly 5 decoded instructions.");

    foreach (var ins in instructions) {
      Console.WriteLine($"+0x{ins.Rva:X} {ins.Mnemonic} {ins.OperandText} " +
                        $"groups={ins.Groups} read=[{string.Join(",", ins.RegistersRead)}] " +
                        $"written=[{string.Join(",", ins.RegistersWritten)}]");
    }

    StringAssert.StartsWith(instructions[0].Mnemonic, "nop");
    Assert.AreEqual(InstructionGroupFlags.None, instructions[0].Groups);

    var bl = instructions[1];
    StringAssert.StartsWith(bl.Mnemonic, "bl");
    Assert.IsTrue(bl.IsCall);
    Assert.IsTrue(bl.HasRegisterAccessDetail);
    // "bl" always implicitly writes the link register -- assert non-empty rather than the exact
    // Capstone register name spelling, which this test doesn't hard-code as ground truth.
    Assert.IsTrue(bl.RegistersWritten.Count > 0,
      "Expected 'bl' to report at least one implicitly written register (the link register).");

    var b = instructions[2];
    StringAssert.StartsWith(b.Mnemonic, "b");
    Assert.IsTrue(b.IsUnconditionalJump, $"Expected unconditional jump, got groups={b.Groups} mnemonic={b.Mnemonic}");
    Assert.IsFalse(b.IsConditionalBranch);

    var beq = instructions[3];
    Assert.IsTrue(beq.IsConditionalBranch, $"Expected conditional branch, got groups={beq.Groups} mnemonic={beq.Mnemonic}");
    Assert.IsFalse(beq.IsUnconditionalJump);

    var ret = instructions[4];
    StringAssert.StartsWith(ret.Mnemonic, "ret");
    Assert.IsTrue(ret.IsReturn);
  }
}
