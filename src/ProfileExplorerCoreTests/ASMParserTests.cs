// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.CoreTests;

[TestClass]
public class ASMParserTests {
  [TestMethod]
  public void Parse_ARM64ConditionalBranch_CreatesBasicBlocksAndCFGEdges() {
    string text =
      """
      1000:    54000040  b.eq #0x1008
      1004:    d503201f  nop
      1008:    d65f03c0  ret
      """;

    var function = ParseARM64(text, 12);

    Assert.AreEqual(3, function.BlockCount);
    Assert.AreEqual(2, function.EntryBlock.Successors.Count);
    CollectionAssert.Contains(function.EntryBlock.Successors, function.Blocks[1]);
    CollectionAssert.Contains(function.EntryBlock.Successors, function.Blocks[2]);
    Assert.IsTrue(function.EntryBlock.FirstInstruction.IsBranch);
    Assert.IsTrue(function.Blocks[2].FirstInstruction.IsReturn);
  }

  [DataTestMethod]
  [DataRow("1000:    b4000040  cbz x0, #0x1008")]
  [DataRow("1000:    b5000040  cbnz x0, #0x1008")]
  [DataRow("1000:    36000040  tbz w0, #0, #0x1008")]
  [DataRow("1000:    37000040  tbnz w0, #0, #0x1008")]
  public void Parse_ARM64CompareAndTestBranch_CreatesBasicBlocksAndCFGEdges(string branchInstruction) {
    string text =
      $"""
      {branchInstruction}
      1004:    d503201f  nop
      1008:    d65f03c0  ret
      """;

    var function = ParseARM64(text, 12);

    Assert.AreEqual(3, function.BlockCount);
    Assert.AreEqual(2, function.EntryBlock.Successors.Count);
    CollectionAssert.Contains(function.EntryBlock.Successors, function.Blocks[1]);
    CollectionAssert.Contains(function.EntryBlock.Successors, function.Blocks[2]);
  }

  [TestMethod]
  public void Parse_ARM64UnconditionalBranch_CreatesOnlyBranchTargetEdge() {
    string text =
      """
      1000:    14000002  b #0x1008
      1004:    d503201f  nop
      1008:    d65f03c0  ret
      """;

    var function = ParseARM64(text, 12);

    Assert.AreEqual(1, function.EntryBlock.Successors.Count);
    Assert.IsTrue(function.EntryBlock.Successors[0].FirstInstruction.IsReturn);
  }

  [TestMethod]
  public void DisassemblerSectionLoader_SelectsIRModeFromNativeBinaryArchitecture() {
    string repositoryRoot = FindRepositoryRoot();
    string arm64BinaryPath = Path.Combine(repositoryRoot, "src", "external", "arm64", "msdia140.dll");
    string x64BinaryPath = Path.Combine(repositoryRoot, "src", "external", "msdia140.dll");
    var x64Session = new ASMCompilerInfoProvider(IRMode.x86_64);
    var arm64Session = new ASMCompilerInfoProvider(IRMode.ARM64);

    using var arm64Loader = new DisassemblerSectionLoader(arm64BinaryPath, x64Session, null, false);
    using var x64Loader = new DisassemblerSectionLoader(x64BinaryPath, arm64Session, null, false);

    Assert.AreEqual(IRMode.ARM64, GetIRMode(arm64Loader));
    Assert.AreEqual(IRMode.x86_64, GetIRMode(x64Loader));
  }

  [TestMethod]
  public void DisassemblerSectionLoader_UsesManagedJitArchitectureForAnyCpuImage() {
    string binaryPath = typeof(ASMParserTests).Assembly.Location;
    var binaryInfo = PEBinaryInfoProvider.GetBinaryFileInfo(binaryPath);
    Assert.IsNotNull(binaryInfo);
    Assert.AreEqual(Machine.I386, binaryInfo.Architecture);
    Assert.IsTrue(binaryInfo.IsManagedImage);

    var compilerInfo = new ASMCompilerInfoProvider(IRMode.x86_64);
    var debugInfo = new DotNetDebugInfoProvider(Machine.Arm64);
    using var loader = new DisassemblerSectionLoader(binaryPath, compilerInfo, debugInfo, false, true);

    Assert.AreEqual(IRMode.ARM64, GetIRMode(loader));
  }

  [TestMethod]
  public void DisassemblerSectionLoader_UsesSessionIRUntilManagedDebugInfoArrives() {
    string binaryPath = typeof(ASMParserTests).Assembly.Location;
    var compilerInfo = new ASMCompilerInfoProvider(IRMode.x86_64);
    using var loader = new DisassemblerSectionLoader(binaryPath, compilerInfo, null, false, true);

    Assert.AreEqual(IRMode.x86_64, GetIRMode(loader));

    loader.DebugInfo = new DotNetDebugInfoProvider(Machine.Arm64);

    Assert.AreEqual(IRMode.ARM64, GetIRMode(loader));
  }

  [TestMethod]
  public void DisassemblerSectionLoader_FallsBackToSessionIRForUnknownManagedArchitecture() {
    string binaryPath = typeof(ASMParserTests).Assembly.Location;
    var compilerInfo = new ASMCompilerInfoProvider(IRMode.ARM64);
    var debugInfo = new DotNetDebugInfoProvider(Machine.Unknown);
    using var loader = new DisassemblerSectionLoader(binaryPath, compilerInfo, debugInfo, false, true);

    Assert.AreEqual(IRMode.ARM64, GetIRMode(loader));
  }

  private static FunctionIR ParseARM64(string text, long functionSize) {
    var irInfo = new ASMCompilerIRInfo(IRMode.ARM64);
    var parentFunction = new IRTextFunction("test");
    var output = new IRPassOutput(0, text.Length, 1, text.Split('\n').Length);
    var section = new IRTextSection(parentFunction, parentFunction.Name, output);
    var parser = new ASMIRSectionParser(functionSize, irInfo, irInfo.CreateParsingErrorHandler());

    return parser.ParseSection(section, text);
  }

  private static IRMode GetIRMode(DisassemblerSectionLoader loader) {
    var irInfoField = typeof(IRTextSectionLoader).GetField("irInfo_", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.IsNotNull(irInfoField);
    return ((ICompilerIRInfo)irInfoField.GetValue(loader)).Mode;
  }

  private static string FindRepositoryRoot() {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory != null) {
      if (File.Exists(Path.Combine(directory.FullName, ".gitmodules"))) {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find the repository root from the test output directory.");
  }
}
