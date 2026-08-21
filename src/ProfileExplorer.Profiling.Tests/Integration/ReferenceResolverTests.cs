// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="ReferenceResolver"/> and <see cref="KnownApiFacts"/>: resolving
/// an RVA (typically <see cref="SemanticInstruction.MemoryReferenceRva"/> from an x64 RIP-relative
/// call) to a real import/export/string identity. The key scenario
/// (<see cref="RipRelativeCallToImportedFunction_ResolvesToKnownApi"/>) chains Capstone semantic
/// decoding (stage 1), PE import parsing (stage 2), and this stage's resolver together, turning
/// "call qword ptr [rip+N]" into "NTOSKRNL.EXE!ExAllocatePool2" with its documented API fact --
/// exactly the "pure gold" scenario the reverse-engineering enhancement plan calls out for
/// third-party/no-PDB analysis.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class ReferenceResolverTests {
  private static void WriteAsciiZ(byte[] buffer, int offset, string text) {
    var bytes = Encoding.ASCII.GetBytes(text);
    Array.Copy(bytes, 0, buffer, offset, bytes.Length);
    buffer[offset + bytes.Length] = 0;
  }

  [TestMethod]
  public void RipRelativeCallToImportedFunction_ResolvesToKnownApi() {
    const string moduleName = "NTOSKRNL.EXE";
    const string functionName = "ExAllocatePool2";

    int importByNameOffset = 72;
    int importByNameSize = 2 + functionName.Length + 1;
    int moduleNameOffset = importByNameOffset + importByNameSize;
    int idataSize = moduleNameOffset + moduleName.Length + 1;

    var idata = new byte[idataSize];
    var textBytes = new byte[6]; // FF 15 <disp32> -- "call qword ptr [rip+disp32]"

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(2, new[] { textBytes.Length, idataSize });
    int textRva = rvas[0];
    int idataRva = rvas[1];

    long ilRva = idataRva + 40;
    long iatRva = idataRva + 56;
    long importByNameRva = idataRva + importByNameOffset;
    long moduleNameRva = idataRva + moduleNameOffset;

    // IMAGE_IMPORT_DESCRIPTOR[0].
    BitConverter.GetBytes((uint)ilRva).CopyTo(idata, 0);
    BitConverter.GetBytes((uint)0).CopyTo(idata, 4);
    BitConverter.GetBytes((uint)0).CopyTo(idata, 8);
    BitConverter.GetBytes((uint)moduleNameRva).CopyTo(idata, 12);
    BitConverter.GetBytes((uint)iatRva).CopyTo(idata, 16);
    // IMAGE_IMPORT_DESCRIPTOR[1]: zero terminator (bytes 20-39 already zero).

    BitConverter.GetBytes((long)importByNameRva).CopyTo(idata, 40); // ILT entry.
    BitConverter.GetBytes((long)importByNameRva).CopyTo(idata, 56); // IAT entry (pre-load).

    BitConverter.GetBytes((ushort)0).CopyTo(idata, importByNameOffset); // Hint.
    WriteAsciiZ(idata, importByNameOffset + 2, functionName);
    WriteAsciiZ(idata, moduleNameOffset, moduleName);

    // "call qword ptr [rip+disp32]" whose effective address == the IAT slot's RVA.
    long instrEndRva = textRva + textBytes.Length;
    int disp32 = (int)(iatRva - instrEndRva);
    textBytes[0] = 0xFF;
    textBytes[1] = 0x15;
    BitConverter.GetBytes(disp32).CopyTo(textBytes, 2);

    var sections = new List<SyntheticPeBuilder.Section> {
      new(".text", textBytes, IsExecutableCode: true),
      new(".idata", idata, IsExecutableCode: false)
    };

    var peBytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections,
      importTableSectionIndex: 1, importTableOffsetInSection: 0, importTableSize: (uint)idataSize);

    using var fixture = new SyntheticPeFile(peBytes, "SyntheticImportCall.dll");

    using var disasm = Disassembler.CreateForBinary(fixture.Path, null, null, enableSemanticDetail: true);
    Assert.IsNotNull(disasm);
    var instructions = disasm!.DisassembleToSemanticList(textRva, textBytes.Length);
    Assert.AreEqual(1, instructions.Count);

    var call = instructions[0];
    Console.WriteLine($"{call.Mnemonic} {call.OperandText} MemoryReferenceRva=0x{call.MemoryReferenceRva:X}");
    Assert.IsTrue(call.IsCall, $"Expected a call instruction, got groups={call.Groups} mnemonic={call.Mnemonic}");
    Assert.IsNotNull(call.MemoryReferenceRva, "Expected the RIP-relative memory operand to resolve.");
    Assert.AreEqual(iatRva, call.MemoryReferenceRva!.Value);

    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    var resolved = ReferenceResolver.Resolve(peInfo, call.MemoryReferenceRva.Value);
    Console.WriteLine($"Resolved: Kind={resolved.Kind} Name={resolved.Name} Fact={resolved.ApiFact?.Category}");

    Assert.AreEqual(ReferenceKind.ImportedFunction, resolved.Kind);
    Assert.AreEqual($"{moduleName}!{functionName}", resolved.Name);
    Assert.IsNotNull(resolved.ApiFact, "ExAllocatePool2 should have a documented KnownApiFacts entry.");
    Assert.AreEqual(ApiSideEffectCategory.MemoryAllocation, resolved.ApiFact!.Category);
  }

  [TestMethod]
  public void Resolve_ImportWithNoKnownApiFact_ReturnsImportedFunctionWithNullFact() {
    const string moduleName = "SOMEDRIVER.SYS";
    const string functionName = "SomeUndocumentedInternalRoutine";

    int importByNameOffset = 72;
    int importByNameSize = 2 + functionName.Length + 1;
    int moduleNameOffset = importByNameOffset + importByNameSize;
    int idataSize = moduleNameOffset + moduleName.Length + 1;
    var idata = new byte[idataSize];

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(1, new[] { idataSize });
    int idataRva = rvas[0];
    long ilRva = idataRva + 40;
    long iatRva = idataRva + 56;
    long importByNameRva = idataRva + importByNameOffset;
    long moduleNameRva = idataRva + moduleNameOffset;

    BitConverter.GetBytes((uint)ilRva).CopyTo(idata, 0);
    BitConverter.GetBytes((uint)moduleNameRva).CopyTo(idata, 12);
    BitConverter.GetBytes((uint)iatRva).CopyTo(idata, 16);
    BitConverter.GetBytes((long)importByNameRva).CopyTo(idata, 40);
    BitConverter.GetBytes((long)importByNameRva).CopyTo(idata, 56);
    BitConverter.GetBytes((ushort)0).CopyTo(idata, importByNameOffset);
    WriteAsciiZ(idata, importByNameOffset + 2, functionName);
    WriteAsciiZ(idata, moduleNameOffset, moduleName);

    var sections = new List<SyntheticPeBuilder.Section> { new(".idata", idata, IsExecutableCode: false) };
    var peBytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections,
      importTableSectionIndex: 0, importTableSize: (uint)idataSize);

    using var fixture = new SyntheticPeFile(peBytes, "SyntheticUnknownImport.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    var resolved = ReferenceResolver.Resolve(peInfo, iatRva);
    Assert.AreEqual(ReferenceKind.ImportedFunction, resolved.Kind);
    Assert.AreEqual($"{moduleName}!{functionName}", resolved.Name);
    Assert.IsNull(resolved.ApiFact);
  }

  [TestMethod]
  public void Resolve_ExportedFunctionRva_ReturnsExportedFunctionKind() {
    const int sectionSize = 64;
    var data = new byte[sectionSize];
    data[0] = 0xC3; // ret -- the exported function's body.

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(1, new[] { sectionSize });
    int sectionRva = rvas[0];
    long functionRva = sectionRva + 0;

    BitConverter.GetBytes((uint)1).CopyTo(data, 1 + 16);  // Base = 1.
    BitConverter.GetBytes((uint)1).CopyTo(data, 1 + 20);  // NumberOfFunctions = 1.
    BitConverter.GetBytes((uint)1).CopyTo(data, 1 + 24);  // NumberOfNames = 1.
    BitConverter.GetBytes((uint)(sectionRva + 41)).CopyTo(data, 1 + 28);
    BitConverter.GetBytes((uint)(sectionRva + 45)).CopyTo(data, 1 + 32);
    BitConverter.GetBytes((uint)(sectionRva + 49)).CopyTo(data, 1 + 36);
    BitConverter.GetBytes((uint)functionRva).CopyTo(data, 41);
    BitConverter.GetBytes((uint)(sectionRva + 51)).CopyTo(data, 45);
    BitConverter.GetBytes((ushort)0).CopyTo(data, 49);
    WriteAsciiZ(data, 51, "SomeExport");

    var sections = new List<SyntheticPeBuilder.Section> { new(".text", data, IsExecutableCode: true) };
    var peBytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections,
      exportTableSectionIndex: 0, exportTableOffsetInSection: 1, exportTableSize: 40);

    using var fixture = new SyntheticPeFile(peBytes, "SyntheticExportResolve.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    var resolved = ReferenceResolver.Resolve(peInfo, functionRva);
    Assert.AreEqual(ReferenceKind.ExportedFunction, resolved.Kind);
    Assert.AreEqual("SomeExport", resolved.Name);
  }

  [TestMethod]
  public void Resolve_AsciiStringReference_ReturnsAsciiStringKind() {
    var data = new byte[32];
    WriteAsciiZ(data, 4, "Hello, world!");

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(1, new[] { data.Length });
    int sectionRva = rvas[0];

    var sections = new List<SyntheticPeBuilder.Section> { new(".rdata", data, IsExecutableCode: false) };
    var peBytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections);

    using var fixture = new SyntheticPeFile(peBytes, "SyntheticAsciiString.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    var resolved = ReferenceResolver.Resolve(peInfo, sectionRva + 4);
    Assert.AreEqual(ReferenceKind.AsciiString, resolved.Kind);
    Assert.AreEqual("Hello, world!", resolved.Text);
  }

  [TestMethod]
  public void Resolve_Utf16StringReference_ReturnsUtf16StringKind() {
    var data = new byte[64];
    var wideBytes = Encoding.Unicode.GetBytes("Wide string test\0");
    wideBytes.CopyTo(data, 4);

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(1, new[] { data.Length });
    int sectionRva = rvas[0];

    var sections = new List<SyntheticPeBuilder.Section> { new(".rdata", data, IsExecutableCode: false) };
    var peBytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections);

    using var fixture = new SyntheticPeFile(peBytes, "SyntheticUtf16String.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    var resolved = ReferenceResolver.Resolve(peInfo, sectionRva + 4);
    Assert.AreEqual(ReferenceKind.Utf16String, resolved.Kind);
    Assert.AreEqual("Wide string test", resolved.Text);
  }

  [TestMethod]
  public void Resolve_UnrecognizedData_ReturnsUnknownKind() {
    // Non-printable binary data -- must not be misclassified as a string.
    var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(1, new[] { data.Length });
    int sectionRva = rvas[0];

    var sections = new List<SyntheticPeBuilder.Section> { new(".data", data, IsExecutableCode: false) };
    var peBytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections);

    using var fixture = new SyntheticPeFile(peBytes, "SyntheticUnknownData.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    var resolved = ReferenceResolver.Resolve(peInfo, sectionRva);
    Assert.AreEqual(ReferenceKind.Unknown, resolved.Kind);
    Assert.IsNull(resolved.Name);
    Assert.IsNull(resolved.Text);
  }

  [TestMethod]
  public void KnownApiFacts_TryGetFact_NullOrEmptyOrUnknownName_ReturnsFalse() {
    Assert.IsFalse(KnownApiFacts.TryGetFact(null, out var f1));
    Assert.IsNull(f1);
    Assert.IsFalse(KnownApiFacts.TryGetFact("", out var f2));
    Assert.IsNull(f2);
    Assert.IsFalse(KnownApiFacts.TryGetFact("ThisIsNotARealApiName", out var f3));
    Assert.IsNull(f3);
  }

  [TestMethod]
  public void KnownApiFacts_TryGetFact_IsCaseInsensitive() {
    Assert.IsTrue(KnownApiFacts.TryGetFact("exallocatepool2", out var fact));
    Assert.IsNotNull(fact);
    Assert.AreEqual(ApiSideEffectCategory.MemoryAllocation, fact!.Category);
  }
}
