// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="PEBinaryInfoProvider.GetImportedFunctions"/> and
/// <see cref="PEBinaryInfoProvider.GetExportedFunctions"/> against hand-built synthetic PE images.
/// These are the first tier of "function-relevant PE evidence" from the reverse-engineering
/// enhancement plan: resolving indirect call targets (IAT slots) to real API names, and
/// identifying a selected function's own exported identity/forwarding.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class PEReferenceModelTests {
  private static void WriteAsciiZ(byte[] buffer, int offset, string text) {
    var bytes = Encoding.ASCII.GetBytes(text);
    Array.Copy(bytes, 0, buffer, offset, bytes.Length);
    buffer[offset + bytes.Length] = 0;
  }

  // ---------------------------------------------------------------------------------------------
  // Import fixture (x64, thunk size 8 bytes): one module ("TESTDLL.DLL") with one imported
  // function ("TestImportedFunction"), laid out as:
  //   [0..20)   IMAGE_IMPORT_DESCRIPTOR[0]  (OriginalFirstThunk=ILT, Name=ModuleName, FirstThunk=IAT)
  //   [20..40)  IMAGE_IMPORT_DESCRIPTOR[1]  (all-zero terminator)
  //   [40..56)  ILT: 1 entry (RVA -> IMAGE_IMPORT_BY_NAME) + 8-byte zero terminator
  //   [56..72)  IAT: same layout (pre-load, IAT == ILT contents)
  //   [72..95)  IMAGE_IMPORT_BY_NAME: Hint(2) + "TestImportedFunction\0" (21 bytes)
  //   [95..107) Module name: "TESTDLL.DLL\0" (12 bytes)
  // ---------------------------------------------------------------------------------------------
  [TestMethod]
  public void GetImportedFunctions_ResolvesModuleFunctionNameAndIatRva() {
    const int sectionSize = 107;
    var data = new byte[sectionSize];

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(1, new[] { sectionSize });
    int sectionRva = rvas[0];

    long ilRva = sectionRva + 40;
    long iatRva = sectionRva + 56;
    long importByNameRva = sectionRva + 72;
    long moduleNameRva = sectionRva + 95;

    // IMAGE_IMPORT_DESCRIPTOR[0]: OriginalFirstThunk, TimeDateStamp, ForwarderChain, Name, FirstThunk.
    BitConverter.GetBytes((uint)ilRva).CopyTo(data, 0);
    BitConverter.GetBytes((uint)0).CopyTo(data, 4);
    BitConverter.GetBytes((uint)0).CopyTo(data, 8);
    BitConverter.GetBytes((uint)moduleNameRva).CopyTo(data, 12);
    BitConverter.GetBytes((uint)iatRva).CopyTo(data, 16);
    // IMAGE_IMPORT_DESCRIPTOR[1]: all-zero terminator (bytes 20-39 already zero-initialized).

    // ILT: one 8-byte entry pointing at the IMAGE_IMPORT_BY_NAME, then an 8-byte zero terminator.
    BitConverter.GetBytes((long)importByNameRva).CopyTo(data, 40);
    // IAT: identical contents before the loader resolves it.
    BitConverter.GetBytes((long)importByNameRva).CopyTo(data, 56);

    // IMAGE_IMPORT_BY_NAME: Hint (2 bytes, value irrelevant) + name.
    BitConverter.GetBytes((ushort)0).CopyTo(data, 72);
    WriteAsciiZ(data, 74, "TestImportedFunction");

    WriteAsciiZ(data, 95, "TESTDLL.DLL");

    var sections = new List<SyntheticPeBuilder.Section> { new(".idata", data, IsExecutableCode: false) };
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections,
      importTableSectionIndex: 0, importTableOffsetInSection: 0, importTableSize: (uint)sectionSize);

    using var fixture = new SyntheticPeFile(bytes, "SyntheticImports.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    var imports = peInfo.GetImportedFunctions();

    foreach (var import in imports) {
      Console.WriteLine($"{import.QualifiedName} IatRva=0x{import.IatRva:X}");
    }

    Assert.AreEqual(1, imports.Count);
    Assert.AreEqual("TESTDLL.DLL", imports[0].ModuleName);
    Assert.AreEqual("TestImportedFunction", imports[0].FunctionName);
    Assert.IsNull(imports[0].Ordinal);
    Assert.AreEqual(iatRva, imports[0].IatRva);
    Assert.AreEqual("TESTDLL.DLL!TestImportedFunction", imports[0].QualifiedName);
  }

  [TestMethod]
  public void GetImportedFunctions_NoImportDirectory_ReturnsEmptyList() {
    var sections = new List<SyntheticPeBuilder.Section> { new(".text", new byte[] { 0xC3 }, IsExecutableCode: true) };
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections);

    using var fixture = new SyntheticPeFile(bytes, "SyntheticNoImports.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    Assert.AreEqual(0, peInfo.GetImportedFunctions().Count);
  }

  // ---------------------------------------------------------------------------------------------
  // Export fixture: a single non-forwarder export ("ExportedFunc", ordinal base 1) whose function
  // RVA points at a real byte (0xC3 "ret") preceding the export directory in the same section.
  //   [0]       0xC3 "ret" -- the exported function's body.
  //   [1..41)   IMAGE_EXPORT_DIRECTORY (40 bytes)
  //   [41..45)  AddressOfFunctions[0] = RVA of the "ret" above.
  //   [45..49)  AddressOfNames[0] = RVA of the name string.
  //   [49..51)  AddressOfNameOrdinals[0] = 0.
  //   [51..64)  Name string: "ExportedFunc\0" (13 bytes).
  // ---------------------------------------------------------------------------------------------
  [TestMethod]
  public void GetExportedFunctions_ResolvesNameRvaAndOrdinal() {
    const int sectionSize = 64;
    var data = new byte[sectionSize];
    data[0] = 0xC3; // ret

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(1, new[] { sectionSize });
    int sectionRva = rvas[0];
    long functionRva = sectionRva + 0;
    long directoryRva = sectionRva + 1;
    long addressOfFunctionsRva = sectionRva + 41;
    long addressOfNamesRva = sectionRva + 45;
    long addressOfNameOrdinalsRva = sectionRva + 49;
    long nameStringRva = sectionRva + 51;

    // IMAGE_EXPORT_DIRECTORY, relative to offset 1: Characteristics(4) TimeDateStamp(4) Ver(4)
    // Name(4) Base(4) NumberOfFunctions(4) NumberOfNames(4) AddressOfFunctions(4) AddressOfNames(4)
    // AddressOfNameOrdinals(4).
    BitConverter.GetBytes((uint)1).CopyTo(data, 1 + 16);  // Base = 1.
    BitConverter.GetBytes((uint)1).CopyTo(data, 1 + 20);  // NumberOfFunctions = 1.
    BitConverter.GetBytes((uint)1).CopyTo(data, 1 + 24);  // NumberOfNames = 1.
    BitConverter.GetBytes((uint)addressOfFunctionsRva).CopyTo(data, 1 + 28);
    BitConverter.GetBytes((uint)addressOfNamesRva).CopyTo(data, 1 + 32);
    BitConverter.GetBytes((uint)addressOfNameOrdinalsRva).CopyTo(data, 1 + 36);

    BitConverter.GetBytes((uint)functionRva).CopyTo(data, 41);
    BitConverter.GetBytes((uint)nameStringRva).CopyTo(data, 45);
    BitConverter.GetBytes((ushort)0).CopyTo(data, 49); // Ordinal index 0 -> AddressOfFunctions[0].

    WriteAsciiZ(data, 51, "ExportedFunc");

    var sections = new List<SyntheticPeBuilder.Section> { new(".text", data, IsExecutableCode: true) };
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections,
      exportTableSectionIndex: 0, exportTableOffsetInSection: 1, exportTableSize: 40);

    using var fixture = new SyntheticPeFile(bytes, "SyntheticExports.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    var exports = peInfo.GetExportedFunctions();

    foreach (var export in exports) {
      Console.WriteLine($"{export.FunctionName} Rva=0x{export.Rva:X} Ordinal={export.Ordinal} " +
                        $"IsForwarder={export.IsForwarder}");
    }

    Assert.AreEqual(1, exports.Count);
    Assert.AreEqual("ExportedFunc", exports[0].FunctionName);
    Assert.AreEqual(functionRva, exports[0].Rva);
    Assert.AreEqual(1, exports[0].Ordinal);
    Assert.IsFalse(exports[0].IsForwarder);
    Assert.IsNull(exports[0].ForwarderTarget);
  }

  // ---------------------------------------------------------------------------------------------
  // Forwarder export fixture: the function RVA points *inside* the export directory's own RVA
  // range (spanning the whole 91-byte section via exportTableSize), meaning it's really the RVA
  // of an ASCII forwarder string ("OtherModule.OtherFunction"), not code.
  //   [0..40)   IMAGE_EXPORT_DIRECTORY
  //   [40..44)  AddressOfFunctions[0] = RVA of the forwarder string (>= directory start).
  //   [44..48)  AddressOfNames[0] = RVA of the exported name string.
  //   [48..50)  AddressOfNameOrdinals[0] = 0.
  //   [50..64)  Name string: "ForwardedFunc\0" (14 bytes).
  //   [64..91)  Forwarder target string: "OtherModule.OtherFunction\0" (27 bytes).
  // ---------------------------------------------------------------------------------------------
  [TestMethod]
  public void GetExportedFunctions_DetectsForwarderExport() {
    const int sectionSize = 91;
    var data = new byte[sectionSize];

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(1, new[] { sectionSize });
    int sectionRva = rvas[0];
    long directoryRva = sectionRva + 0;
    long addressOfFunctionsRva = sectionRva + 40;
    long addressOfNamesRva = sectionRva + 44;
    long addressOfNameOrdinalsRva = sectionRva + 48;
    long nameStringRva = sectionRva + 50;
    long forwarderStringRva = sectionRva + 64;

    BitConverter.GetBytes((uint)1).CopyTo(data, 16);  // Base = 1.
    BitConverter.GetBytes((uint)1).CopyTo(data, 20);  // NumberOfFunctions = 1.
    BitConverter.GetBytes((uint)1).CopyTo(data, 24);  // NumberOfNames = 1.
    BitConverter.GetBytes((uint)addressOfFunctionsRva).CopyTo(data, 28);
    BitConverter.GetBytes((uint)addressOfNamesRva).CopyTo(data, 32);
    BitConverter.GetBytes((uint)addressOfNameOrdinalsRva).CopyTo(data, 36);

    BitConverter.GetBytes((uint)forwarderStringRva).CopyTo(data, 40);
    BitConverter.GetBytes((uint)nameStringRva).CopyTo(data, 44);
    BitConverter.GetBytes((ushort)0).CopyTo(data, 48);

    WriteAsciiZ(data, 50, "ForwardedFunc");
    WriteAsciiZ(data, 64, "OtherModule.OtherFunction");

    var sections = new List<SyntheticPeBuilder.Section> { new(".edata", data, IsExecutableCode: false) };
    // exportTableSize spans the *entire* section (91 bytes) -- covering the forwarder string, as a
    // real linker's directory size would -- so the forwarder-detection range check is accurate.
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections,
      exportTableSectionIndex: 0, exportTableOffsetInSection: 0, exportTableSize: (uint)sectionSize);

    using var fixture = new SyntheticPeFile(bytes, "SyntheticForwarderExport.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    var exports = peInfo.GetExportedFunctions();

    foreach (var export in exports) {
      Console.WriteLine($"{export.FunctionName} Rva=0x{export.Rva:X} Ordinal={export.Ordinal} " +
                        $"IsForwarder={export.IsForwarder} ForwarderTarget={export.ForwarderTarget}");
    }

    Assert.AreEqual(1, exports.Count);
    Assert.AreEqual("ForwardedFunc", exports[0].FunctionName);
    Assert.IsTrue(exports[0].IsForwarder);
    Assert.AreEqual("OtherModule.OtherFunction", exports[0].ForwarderTarget);
    Assert.AreEqual(0, exports[0].Rva); // Rva is meaningless for a forwarder -- always reported as 0.
  }

  [TestMethod]
  public void GetExportedFunctions_NoExportDirectory_ReturnsEmptyList() {
    var sections = new List<SyntheticPeBuilder.Section> { new(".text", new byte[] { 0xC3 }, IsExecutableCode: true) };
    var bytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections);

    using var fixture = new SyntheticPeFile(bytes, "SyntheticNoExports.dll");
    using var peInfo = new PEBinaryInfoProvider(fixture.Path);
    Assert.IsTrue(peInfo.Initialize());

    Assert.AreEqual(0, peInfo.GetExportedFunctions().Count);
  }
}
