// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Profile.Spgo;

namespace ProfileExplorer.CoreTests;

[TestClass]
public class SptDumpParserTests {
  private const string Prefix = """
    *** File Header ***
    Signature: 0x5350543A
    Version: 0x1
    RawDataId: 0x0
    TargetArch: 0x8664
    StrTableOffset: 0x20
    CbStrTableUsed: 0x8
    CbStrTableCapacity: 0x8
    ProgIdsOffset: 0x28
    ProgIdsUsed: 0x1
    ProgIdsCapacity: 0x1
    *** Strings ***
    Offset[0x0]: app.exe
    *** Program IDs ***
    Index[0] app.exe Guid{12345678-1234-1234-1234-123456789abc} Age[1]
    *** Events ***
    """;

  [TestMethod]
  public async Task RepeatAppliesOnlyToTheNextRecord() {
    using var input = new StringReader(Prefix + "\n" + """
      SPT_OP_BINARY_ID 0 app.exe 22 bytes
      SPT_OP_REPEAT 2
      SPT_OP_ETW_INSTR 1 0x1000
      SPT_OP_ETW_INSTR 1 0x1004
      """);
    var profile = await SptDumpParser.ParseAsync(input);
    var records = profile.Segments[0].Records;
    Assert.AreEqual(2, records.Count);
    Assert.AreEqual(3UL, records[0].Weight);
    Assert.AreEqual(1UL, records[1].Weight);
  }

  [TestMethod]
  public async Task RejectsTruncatedEventSegment() {
    using var input = new StringReader(Prefix + "\nSPT_OP_BINARY_ID 0 app.exe 12 bytes\nSPT_OP_ETW_INSTR 1 0x1000");
    await Assert.ThrowsExceptionAsync<InvalidDataException>(() => SptDumpParser.ParseAsync(input));
  }
}
