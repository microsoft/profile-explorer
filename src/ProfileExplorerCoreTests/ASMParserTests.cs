// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core;
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

  private static FunctionIR ParseARM64(string text, long functionSize) {
    var irInfo = new ASMCompilerIRInfo(IRMode.ARM64);
    var parentFunction = new IRTextFunction("test");
    var output = new IRPassOutput(0, text.Length, 1, text.Split('\n').Length);
    var section = new IRTextSection(parentFunction, parentFunction.Name, output);
    var parser = new ASMIRSectionParser(functionSize, irInfo, irInfo.CreateParsingErrorHandler());

    return parser.ParseSection(section, text);
  }
}
