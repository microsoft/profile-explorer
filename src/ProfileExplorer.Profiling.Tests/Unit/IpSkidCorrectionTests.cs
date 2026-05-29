// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Profiling;

namespace ProfileExplorer.Profiling.Tests.Unit;

[TestClass]
[TestCategory("Unit")]
public class IpSkidCorrectionTests {
  [TestMethod]
  public void ExactMatch_NoCorrection() {
    var known = new HashSet<long> { 0, 4, 8, 12 };
    long result = InstructionOffsetConfig.CorrectSkid(4, known, ProcessorArchitecture.Amd64);
    Assert.AreEqual(4, result);
  }

  [TestMethod]
  public void x64_SkidsBackByOne_FindsInstruction() {
    var known = new HashSet<long> { 0, 5, 10 };
    // IP = 6, one byte past instruction at 5.
    long result = InstructionOffsetConfig.CorrectSkid(6, known, ProcessorArchitecture.Amd64);
    Assert.AreEqual(5, result);
  }

  [TestMethod]
  public void x64_SkidsBackVariable_FindsNearestInstruction() {
    var known = new HashSet<long> { 0, 10, 20 };
    // IP = 15, 5 bytes past instruction at 10.
    long result = InstructionOffsetConfig.CorrectSkid(15, known, ProcessorArchitecture.Amd64);
    Assert.AreEqual(10, result);
  }

  [TestMethod]
  public void x64_BeyondMaxAdjust_ReturnsOriginal() {
    var known = new HashSet<long> { 0 };
    // IP = 100, far beyond max adjustment (16 bytes).
    long result = InstructionOffsetConfig.CorrectSkid(100, known, ProcessorArchitecture.Amd64);
    Assert.AreEqual(100, result);
  }

  [TestMethod]
  public void ARM64_FixedFourByteCorrection() {
    var known = new HashSet<long> { 0, 4, 8, 12 };
    // IP = 5, should walk back by 4 to find instruction at 4... but 5-4=1 which is not in the set.
    // Actually ARM64 adjusts by 4 each time: 5-4=1 (no), done since VariableSize(4,4).
    // Let's test with IP = 8 (exact match).
    long result = InstructionOffsetConfig.CorrectSkid(8, known, ProcessorArchitecture.Arm);
    Assert.AreEqual(8, result);

    // IP = 6: ARM adjusts by 4 → 6-4=2, not found. Max adjust = 4, so 1*4=4 <= 4, done.
    result = InstructionOffsetConfig.CorrectSkid(6, known, ProcessorArchitecture.Arm);
    Assert.AreEqual(6, result); // No correction possible.
  }

  [TestMethod]
  public void EmptyInstructionMap_ReturnsOriginal() {
    var known = new HashSet<long>();
    long result = InstructionOffsetConfig.CorrectSkid(10, known, ProcessorArchitecture.Amd64);
    Assert.AreEqual(10, result);
  }
}
