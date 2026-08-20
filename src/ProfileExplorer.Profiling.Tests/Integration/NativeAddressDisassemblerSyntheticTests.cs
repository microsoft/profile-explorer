// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection.PortableExecutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="NativeAddressDisassembler"/> against hand-built, from-scratch
/// synthetic PE images (via <see cref="SyntheticPeBuilder"/>) rather than real compiled binaries.
/// This gives deterministic coverage of:
/// <list type="bullet">
/// <item>x64 PE exception-directory (.pdata) function-bounds resolution and a real forward-scan
/// return-address normalization, including a deliberate "gap" region covered by no .pdata entry
/// (<see cref="NativeAddressDisassemblyFailure.FunctionBoundsNotResolved"/>).</item>
/// <item>ARM64 packed .pdata function-bounds resolution and the fixed-width (-4) return-address
/// normalization, verified against real Capstone ARM64 decoding of hand-encoded instruction bytes.</item>
/// <item>An unsupported machine type (I386), exercised without needing a real x86 binary.</item>
/// </list>
/// No real fixtures/PDBs are required, so these tests always run (never <c>Assert.Inconclusive</c>).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class NativeAddressDisassemblerSyntheticTests {
  // ---------------------------------------------------------------------------------------------
  // x64 fixture: .text contains two 16-byte functions back-to-back, plus a 16-byte "gap" region of
  // valid code bytes deliberately left uncovered by any .pdata entry.
  //
  //   F1 [0x200, 0x210): push rbp; mov rbp,rsp; call F2; pop rbp; ret; int3 * 5 (padding)
  //   F2 [0x210, 0x220): nop; ret; int3 * 14 (padding)
  //   Gap [0x220, 0x230): nop * 16 -- valid code, but NOT described by any RUNTIME_FUNCTION entry.
  //
  // .pdata declares only F1 and F2's RUNTIME_FUNCTION entries (12 bytes each), covering [0x200,0x220).
  // ---------------------------------------------------------------------------------------------
  private static byte[] BuildAmd64Text() {
    var text = new List<byte>();
    text.Add(0x55);                                   // push rbp                  (RVA 0x200)
    text.AddRange(new byte[] {0x48, 0x89, 0xE5});      // mov rbp, rsp              (RVA 0x201)
    text.AddRange(new byte[] {0xE8, 0x07, 0x00, 0x00, 0x00}); // call +7 -> F2 (0x210)  (RVA 0x204)
    text.Add(0x5D);                                   // pop rbp                   (RVA 0x209)
    text.Add(0xC3);                                   // ret                       (RVA 0x20A)
    text.AddRange(new byte[] {0xCC, 0xCC, 0xCC, 0xCC, 0xCC}); // padding to 16 bytes (RVA 0x20B-0x20F)
    text.Add(0x90);                                   // nop                       (RVA 0x210)
    text.Add(0xC3);                                   // ret                       (RVA 0x211)
    for (int i = 0; i < 14; i++) text.Add(0xCC);       // padding to 16 bytes       (RVA 0x212-0x21F)
    for (int i = 0; i < 16; i++) text.Add(0x90);       // gap: valid code, no pdata (RVA 0x220-0x22F)
    return text.ToArray();
  }

  private static byte[] BuildAmd64Pdata(int textRva) {
    var pdata = new List<byte>();
    void AddEntry(uint begin, uint end) {
      pdata.AddRange(BitConverter.GetBytes(begin));
      pdata.AddRange(BitConverter.GetBytes(end));
      pdata.AddRange(BitConverter.GetBytes(0u)); // UnwindInfoAddress: unused by our bounds parser.
    }
    AddEntry((uint)(textRva + 0x00), (uint)(textRva + 0x10)); // F1
    AddEntry((uint)(textRva + 0x10), (uint)(textRva + 0x20)); // F2
    return pdata.ToArray();
  }

  private static SyntheticPeFile BuildAmd64Fixture() {
    var text = BuildAmd64Text();
    var rvas = SyntheticPeBuilder.ComputeSectionRvas(2, new[] {text.Length, 0 /*pdata size placeholder*/});
    var pdata = BuildAmd64Pdata(rvas[0]);

    var sections = new List<SyntheticPeBuilder.Section> {
      new(".text", text, IsExecutableCode: true),
      new(".pdata", pdata, IsExecutableCode: false)
    };

    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections, exceptionTableSectionIndex: 1,
                                        exceptionTableSize: (uint)pdata.Length);
    return new SyntheticPeFile(bytes, "SyntheticAmd64.dll");
  }

  [TestMethod]
  public void Amd64_ResolvesBoundsFromPdata_AndDisassemblesFunction() {
    using var fixture = BuildAmd64Fixture();

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x200, // F1 start RVA.
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.InstructionPointer
    });

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.AreEqual(Machine.Amd64, result.Architecture);
    Assert.IsNotNull(result.Range);
    Assert.AreEqual(FunctionBoundaryProvenance.ExceptionDirectory, result.Range.Value.Provenance);
    Assert.AreEqual(RuntimeFunctionKind.Amd64, result.Range.Value.RuntimeFunctionKind);
    Assert.AreEqual(0x200, result.Range.Value.StartRva);
    Assert.AreEqual(0x210, result.Range.Value.EndRva);
    Assert.IsNull(result.Range.Value.FunctionName); // No PDB supplied -> no name, only bounds.
    StringAssert.Contains(result.QualifiedName, "<unknown+0x200>");
    Assert.IsTrue(result.Instructions.Count > 0);

    // First instruction should be the "push rbp" we encoded.
    Assert.AreEqual(0x200, result.Instructions[0].Rva);
    StringAssert.StartsWith(result.Instructions[0].Mnemonic, "push");
  }

  [TestMethod]
  public void Amd64_NormalizesReturnAddress_ToPrecedingCallInstruction() {
    using var fixture = BuildAmd64Fixture();

    // The return address after "call F2" is RVA 0x209 (call at 0x204, 5-byte instruction).
    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x209,
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.ReturnAddress
    });

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.IsTrue(result.WasNormalized, "x64 return address immediately after a resolved call should normalize.");
    Assert.AreEqual(0x204, result.NormalizedRva);
    Assert.AreEqual(0x209, result.RequestedRva);

    // The normalized instruction, if located in the returned instruction list, should be the call.
    var normInstr = result.Instructions.Find(i => i.Rva == 0x204);
    Assert.IsNotNull(normInstr);
    StringAssert.StartsWith(normInstr.Mnemonic, "call");
    Assert.IsNotNull(normInstr.Target);
    Assert.IsTrue(normInstr.Target.IsCall);
    Assert.AreEqual(0x210, normInstr.Target.Rva); // Target resolves to F2's start.
  }

  [TestMethod]
  public void Amd64_InstructionPointerFrame_NeverNormalized() {
    using var fixture = BuildAmd64Fixture();

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x209,
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.InstructionPointer
    });

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.IsFalse(result.WasNormalized);
    Assert.AreEqual(0x209, result.NormalizedRva);
  }

  [TestMethod]
  public void Amd64_GapRegion_ReturnsFunctionBoundsNotResolved() {
    using var fixture = BuildAmd64Fixture();

    // RVA 0x224 is inside the "gap" region: valid code section, but no .pdata entry covers it and
    // no PDB was supplied -- must be an explicit failure, never a guess/unbounded scan.
    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x224,
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.InstructionPointer
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.FunctionBoundsNotResolved, result.FailureReason);
    Assert.AreEqual(0, result.Instructions.Count);
  }

  [TestMethod]
  public void Amd64_AbsoluteInstructionPointer_RequiresImageBase() {
    using var fixture = BuildAmd64Fixture();

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x1400_0200,
      AddressForm = NativeAddressForm.AbsoluteInstructionPointer,
      FrameKind = FrameAddressKind.InstructionPointer
      // ImageBase intentionally omitted.
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.ImageBaseRequired, result.FailureReason);
  }

  [TestMethod]
  public void Amd64_AbsoluteInstructionPointer_ConvertsUsingSuppliedImageBase() {
    using var fixture = BuildAmd64Fixture();
    const long runtimeBase = 0x7FF7_0000_0000; // Deliberately different from the PE's static ImageBase.

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = runtimeBase + 0x200,
      AddressForm = NativeAddressForm.AbsoluteInstructionPointer,
      ImageBase = runtimeBase,
      FrameKind = FrameAddressKind.InstructionPointer
    });

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.AreEqual(0x200, result.RequestedRva);
    Assert.AreEqual(runtimeBase + 0x200, result.RequestedAddress);
  }

  [TestMethod]
  public void Amd64_ImageIdentityMismatch_FailsExplicitly() {
    using var fixture = BuildAmd64Fixture();

    var wrongIdentity = new BinaryFileDescriptor {
      ImageName = System.IO.Path.GetFileName(fixture.Path),
      TimeStamp = 0x12345678, // Deliberately wrong.
      ImageSize = 99999999
    };

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      ExpectedImageIdentity = wrongIdentity,
      Address = 0x200,
      AddressForm = NativeAddressForm.ModuleRva
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.ImageIdentityMismatch, result.FailureReason);
  }

  [TestMethod]
  public void Amd64_RvaOutsideImage_ReturnsExplicitFailure() {
    using var fixture = BuildAmd64Fixture();

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x0FFF_FFFF, // Far beyond the tiny synthetic image.
      AddressForm = NativeAddressForm.ModuleRva
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.RvaOutOfImageRange, result.FailureReason);
  }

  [TestMethod]
  public void Amd64_RvaNotInCodeSection_ReturnsExplicitFailure() {
    using var fixture = BuildAmd64Fixture();

    // RVA 0x10 falls within the PE header region (before the first section) -- not part of any
    // executable section.
    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x10,
      AddressForm = NativeAddressForm.ModuleRva
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.RvaNotInCodeSection, result.FailureReason);
  }

  // ---------------------------------------------------------------------------------------------
  // ARM64 fixture: a single 12-byte function (fixed 4-byte instruction width):
  //   [0x200] nop
  //   [0x204] bl #0   (self-branch; only the call/target classification matters for these tests)
  //   [0x208] ret
  // .pdata declares one packed ARM64 RUNTIME_FUNCTION entry covering the whole 12 bytes.
  // ---------------------------------------------------------------------------------------------
  private static byte[] BuildArm64Text() {
    return new byte[] {
      0x1F, 0x20, 0x03, 0xD5, // nop
      0x00, 0x00, 0x00, 0x94, // bl #0
      0xC0, 0x03, 0x5F, 0xD6  // ret
    };
  }

  private static byte[] BuildArm64Pdata(int textRva) {
    // Packed entry: Flag=1, FunctionLength=3 (words) -> 12 bytes, all other subfields zero.
    uint unwindData = 1u | (3u << 2);
    var pdata = new List<byte>();
    pdata.AddRange(BitConverter.GetBytes((uint)textRva));
    pdata.AddRange(BitConverter.GetBytes(unwindData));
    return pdata.ToArray();
  }

  private static SyntheticPeFile BuildArm64Fixture() {
    var text = BuildArm64Text();
    var rvas = SyntheticPeBuilder.ComputeSectionRvas(2, new[] {text.Length, 0});
    var pdata = BuildArm64Pdata(rvas[0]);

    var sections = new List<SyntheticPeBuilder.Section> {
      new(".text", text, IsExecutableCode: true),
      new(".pdata", pdata, IsExecutableCode: false)
    };

    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Arm64, sections, exceptionTableSectionIndex: 1,
                                        exceptionTableSize: (uint)pdata.Length);
    return new SyntheticPeFile(bytes, "SyntheticArm64.dll");
  }

  [TestMethod]
  public void Arm64_ResolvesBoundsFromPackedPdata_AndDisassemblesFunction() {
    using var fixture = BuildArm64Fixture();

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x200,
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.InstructionPointer
    });

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.AreEqual(Machine.Arm64, result.Architecture);
    Assert.IsNotNull(result.Range);
    Assert.AreEqual(FunctionBoundaryProvenance.ExceptionDirectory, result.Range.Value.Provenance);
    Assert.AreEqual(RuntimeFunctionKind.Arm64Packed, result.Range.Value.RuntimeFunctionKind);
    Assert.AreEqual(0x200, result.Range.Value.StartRva);
    Assert.AreEqual(0x20C, result.Range.Value.EndRva);
    Assert.AreEqual(3, result.Instructions.Count);
    StringAssert.StartsWith(result.Instructions[0].Mnemonic, "nop");
    StringAssert.StartsWith(result.Instructions[1].Mnemonic, "bl");
    StringAssert.StartsWith(result.Instructions[2].Mnemonic, "ret");
  }

  [TestMethod]
  public void Arm64_NormalizesReturnAddress_UsingFixedFourByteWidth() {
    using var fixture = BuildArm64Fixture();

    // The instruction after "bl" is "ret" at RVA 0x208 -- a genuine return address for the bl at 0x204.
    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x208,
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.ReturnAddress
    });

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.IsTrue(result.WasNormalized, "ARM64 return addresses should always normalize (fixed 4-byte width).");
    Assert.AreEqual(0x204, result.NormalizedRva);
    Assert.AreEqual(0x208, result.RequestedRva);
  }

  [TestMethod]
  public void Arm64_ReturnAddressNotPrecededByCall_DoesNotNormalize() {
    using var fixture = BuildArm64Fixture();

    // The instruction after "nop" (RVA 0x200) is "bl" at RVA 0x204 -- not itself preceded by a
    // call, so treating 0x204 as a "return address" must not produce a false-positive normalization.
    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x204,
      AddressForm = NativeAddressForm.ModuleRva,
      FrameKind = FrameAddressKind.ReturnAddress
    });

    Assert.IsTrue(result.Success, result.ErrorMessage);
    Assert.IsFalse(result.WasNormalized);
    Assert.AreEqual(0x204, result.NormalizedRva);
  }

  // ---------------------------------------------------------------------------------------------
  // Unsupported architecture: the API only supports x64/ARM64; I386 must fail explicitly and early
  // (before any RVA/bounds processing), without needing a real x86 binary.
  // ---------------------------------------------------------------------------------------------
  [TestMethod]
  public void UnsupportedArchitecture_I386_ReturnsExplicitFailure() {
    var sections = new List<SyntheticPeBuilder.Section> {
      new(".text", new byte[] {0x90, 0x90, 0x90, 0x90}, IsExecutableCode: true)
    };
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.I386, sections);
    using var fixture = new SyntheticPeFile(bytes, "SyntheticI386.dll");

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x200,
      AddressForm = NativeAddressForm.ModuleRva
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.UnsupportedArchitecture, result.FailureReason);
    Assert.AreEqual(Machine.I386, result.Architecture);
  }

  [TestMethod]
  public void BinaryNotFound_ReturnsExplicitFailure() {
    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = @"S:\this\path\does\not\exist\fake.dll",
      Address = 0x1000
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.BinaryNotFound, result.FailureReason);
  }

  [TestMethod]
  public void BinaryPathInvalid_EmptyPath_ReturnsExplicitFailure() {
    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = "",
      Address = 0x1000
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.BinaryPathInvalid, result.FailureReason);
  }

  [TestMethod]
  public void MalformedPe_InvalidImage_ReturnsExplicitFailure() {
    using var fixture = new SyntheticPeFile(new byte[] {0x01, 0x02, 0x03, 0x04}, "NotAPe.dll");

    var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = fixture.Path,
      Address = 0x1000
    });

    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.InvalidPeImage, result.FailureReason);
  }
}
