// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Profile.Spgo;

namespace ProfileExplorer.CoreTests;

[TestClass]
public class SptSampleIndexTests {
  [TestMethod]
  public void IndexesOnlyMatchingProgramsAndAttributesBranchesToTheirSource() {
    SptSegment[] segments = [
      new(0, 0, [
        new(SptEventKind.InstructionPointer, 3, [0x100U]),
        new(SptEventKind.RetiredInstructions, 1, [0x100U]),
        new(SptEventKind.LastBranchRecord, 2, [0x100U, 0x110U, 0x120U, 0x110U]),
        new(SptEventKind.CallStack, 8, [0x100U])]),
      new(1, 0, [new(SptEventKind.InstructionPointer, 30, [0x100U])])
    ];
    var profile = new SptProfile(new Dictionary<string, uint>(), [], segments);
    var index = SptSampleIndex.Create(profile, [0], 0x140000000, 0x2000);

    Assert.AreEqual(4UL, index.IpSamples[0x100].Count);
    Assert.AreEqual(3UL, index.IpSamples[0x100].Sources[SptEventKind.InstructionPointer]);
    Assert.AreEqual(1, index.Branches.Count);
    Assert.AreEqual(4UL, index.Branches[0x110].Count);
    Assert.AreEqual(2UL, index.Branches[0x110].Destinations[0x100]);
    Assert.AreEqual(2UL, index.Branches[0x110].Destinations[0x120]);
  }
}
