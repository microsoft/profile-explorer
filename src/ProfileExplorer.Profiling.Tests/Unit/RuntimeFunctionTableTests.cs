// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;

namespace ProfileExplorer.Profiling.Tests.Unit;

/// <summary>
/// Pure, fixture-free unit tests for <see cref="RuntimeFunctionTable"/>: the PE exception
/// directory (.pdata) parser used to resolve trustworthy function bounds for x64/ARM64 when no
/// PDB/DIA symbols are available. Uses hand-built synthetic byte arrays (no real binaries needed)
/// so both architectures and malformed-input paths are covered deterministically.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class RuntimeFunctionTableTests {
  private static byte[] Amd64Entry(uint begin, uint end, uint unwindInfoRva) {
    var bytes = new byte[12];
    BitConverter.GetBytes(begin).CopyTo(bytes, 0);
    BitConverter.GetBytes(end).CopyTo(bytes, 4);
    BitConverter.GetBytes(unwindInfoRva).CopyTo(bytes, 8);
    return bytes;
  }

  private static byte[] Concat(params byte[][] chunks) {
    var result = new List<byte>();
    foreach (var chunk in chunks) result.AddRange(chunk);
    return result.ToArray();
  }

  [TestMethod]
  public void ParseAmd64_ValidEntries_ReturnsSortedRanges() {
    // Two valid entries, deliberately supplied out of address order to verify sorting.
    var data = Concat(
      Amd64Entry(0x2000, 0x2100, 0x5000),
      Amd64Entry(0x1000, 0x1050, 0x5010));

    var ranges = RuntimeFunctionTable.ParseAmd64(data);

    Assert.AreEqual(2, ranges.Count);
    Assert.AreEqual(0x1000, ranges[0].StartRva);
    Assert.AreEqual(0x1050, ranges[0].EndRva);
    Assert.AreEqual(RuntimeFunctionKind.Amd64, ranges[0].Kind);
    Assert.AreEqual(0x2000, ranges[1].StartRva);
    Assert.AreEqual(0x2100, ranges[1].EndRva);
  }

  [TestMethod]
  public void ParseAmd64_SkipsZeroFilledEntries() {
    var data = Concat(
      Amd64Entry(0, 0, 0), // Padding/sentinel row.
      Amd64Entry(0x1000, 0x1050, 0x5000));

    var ranges = RuntimeFunctionTable.ParseAmd64(data);

    Assert.AreEqual(1, ranges.Count);
    Assert.AreEqual(0x1000, ranges[0].StartRva);
  }

  [TestMethod]
  public void ParseAmd64_SkipsNonMonotonicEntry() {
    // EndAddress <= BeginAddress is not a valid function extent -- must be rejected, not guessed at.
    var data = Concat(
      Amd64Entry(0x1000, 0x1000, 0x5000), // Zero length.
      Amd64Entry(0x2000, 0x1900, 0x5010), // End before begin.
      Amd64Entry(0x3000, 0x3040, 0x5020)); // Valid.

    var ranges = RuntimeFunctionTable.ParseAmd64(data);

    Assert.AreEqual(1, ranges.Count);
    Assert.AreEqual(0x3000, ranges[0].StartRva);
  }

  [TestMethod]
  public void ParseAmd64_TruncatedTrailingBytes_IgnoresPartialEntry() {
    // A whole valid entry plus a few extra trailing bytes that don't form a full 12-byte record.
    var data = Concat(Amd64Entry(0x1000, 0x1050, 0x5000), new byte[] {0x01, 0x02, 0x03});

    var ranges = RuntimeFunctionTable.ParseAmd64(data);

    Assert.AreEqual(1, ranges.Count);
    Assert.AreEqual(0x1000, ranges[0].StartRva);
  }

  [TestMethod]
  public void ParseAmd64_EmptyInput_ReturnsEmptyList() {
    var ranges = RuntimeFunctionTable.ParseAmd64(ReadOnlySpan<byte>.Empty);
    Assert.AreEqual(0, ranges.Count);
  }

  private static byte[] Arm64PackedEntry(uint begin, int functionLengthWords, int regF = 0, int regI = 0,
                                         int h = 0, int cr = 0, int frameSize = 0) {
    uint unwindData = 1u; // Flag = 1 (packed).
    unwindData |= (uint)(functionLengthWords & 0x7FF) << 2;
    unwindData |= (uint)(regF & 0x7) << 13;
    unwindData |= (uint)(regI & 0xF) << 16;
    unwindData |= (uint)(h & 0x1) << 20;
    unwindData |= (uint)(cr & 0x3) << 21;
    unwindData |= (uint)(frameSize & 0x1FF) << 23;

    var bytes = new byte[8];
    BitConverter.GetBytes(begin).CopyTo(bytes, 0);
    BitConverter.GetBytes(unwindData).CopyTo(bytes, 4);
    return bytes;
  }

  private static byte[] Arm64UnpackedEntry(uint begin, uint xdataRva) {
    // Flag = 0 -> unpacked; UnwindData is the (4-byte-aligned) RVA of the .xdata record.
    var bytes = new byte[8];
    BitConverter.GetBytes(begin).CopyTo(bytes, 0);
    BitConverter.GetBytes(xdataRva & ~0x3u).CopyTo(bytes, 4);
    return bytes;
  }

  [TestMethod]
  public void ParseArm64_PackedEntry_DecodesFunctionLength() {
    // FunctionLength = 5 words = 20 bytes. Other subfields set to non-zero values to verify the
    // FunctionLength field is extracted independent of RegF/RegI/H/CR/FrameSize bit placement.
    var data = Arm64PackedEntry(0x4000, functionLengthWords: 5, regF: 3, regI: 7, h: 1, cr: 2, frameSize: 100);

    var ranges = RuntimeFunctionTable.ParseArm64(data, _ => null);

    Assert.AreEqual(1, ranges.Count);
    Assert.AreEqual(0x4000, ranges[0].StartRva);
    Assert.AreEqual(0x4000 + 20, ranges[0].EndRva);
    Assert.AreEqual(RuntimeFunctionKind.Arm64Packed, ranges[0].Kind);
  }

  [TestMethod]
  public void ParseArm64_PackedEntry_MaxFunctionLength_DecodesCorrectly() {
    // 11-bit field: max value 0x7FF words = 8188 bytes.
    var data = Arm64PackedEntry(0x1000, functionLengthWords: 0x7FF);

    var ranges = RuntimeFunctionTable.ParseArm64(data, _ => null);

    Assert.AreEqual(1, ranges.Count);
    Assert.AreEqual(0x1000 + 0x7FF * 4, ranges[0].EndRva);
  }

  [TestMethod]
  public void ParseArm64_UnpackedEntry_ReadsXdataHeaderForLength() {
    // Unpacked entry: FunctionLength comes from the low 18 bits of the .xdata header DWORD at
    // xdataRva, read via the caller-supplied bounded accessor.
    const uint xdataRva = 0x9000;
    const int functionLengthWords = 42;
    var data = Arm64UnpackedEntry(0x5000, xdataRva);

    var ranges = RuntimeFunctionTable.ParseArm64(data, rva => rva == xdataRva ? (uint)functionLengthWords : null);

    Assert.AreEqual(1, ranges.Count);
    Assert.AreEqual(0x5000, ranges[0].StartRva);
    Assert.AreEqual(0x5000 + functionLengthWords * 4, ranges[0].EndRva);
    Assert.AreEqual(RuntimeFunctionKind.Arm64Unwind, ranges[0].Kind);
  }

  [TestMethod]
  public void ParseArm64_UnpackedEntry_UnreadableXdata_IsSkipped() {
    // The .xdata header can't be read deterministically (e.g. RVA outside any section) -> the
    // callback returns null -> the entry must be skipped, never guessed at.
    var data = Concat(
      Arm64UnpackedEntry(0x5000, 0x9000),
      Arm64PackedEntry(0x6000, functionLengthWords: 2));

    var ranges = RuntimeFunctionTable.ParseArm64(data, _ => null);

    Assert.AreEqual(1, ranges.Count);
    Assert.AreEqual(0x6000, ranges[0].StartRva);
  }

  [TestMethod]
  public void ParseArm64_SkipsZeroFilledEntries() {
    var data = Concat(
      new byte[8], // All-zero sentinel row.
      Arm64PackedEntry(0x1000, functionLengthWords: 3));

    var ranges = RuntimeFunctionTable.ParseArm64(data, _ => null);

    Assert.AreEqual(1, ranges.Count);
    Assert.AreEqual(0x1000, ranges[0].StartRva);
  }

  [TestMethod]
  public void ParseArm64_NullCallback_Throws() {
    Assert.ThrowsException<ArgumentNullException>(() => RuntimeFunctionTable.ParseArm64(new byte[8], null!));
  }

  [TestMethod]
  public void ParseArm64_MixedPackedAndUnpacked_SortedByStartRva() {
    const uint xdataRva = 0x9500;
    var data = Concat(
      Arm64UnpackedEntry(0x8000, xdataRva),
      Arm64PackedEntry(0x1000, functionLengthWords: 4));

    var ranges = RuntimeFunctionTable.ParseArm64(data, rva => rva == xdataRva ? (uint)10 : null);

    Assert.AreEqual(2, ranges.Count);
    Assert.AreEqual(0x1000, ranges[0].StartRva);
    Assert.AreEqual(0x8000, ranges[1].StartRva);
  }

  [TestMethod]
  public void Find_ReturnsContainingRange() {
    var ranges = RuntimeFunctionTable.ParseAmd64(Concat(
      Amd64Entry(0x1000, 0x1050, 0),
      Amd64Entry(0x2000, 0x2200, 0)));

    var found = RuntimeFunctionTable.Find(ranges, 0x2100);

    Assert.IsNotNull(found);
    Assert.AreEqual(0x2000, found.Value.StartRva);
  }

  [TestMethod]
  public void Find_RvaExactlyAtStart_IsIncluded() {
    var ranges = RuntimeFunctionTable.ParseAmd64(Amd64Entry(0x2000, 0x2200, 0));
    var found = RuntimeFunctionTable.Find(ranges, 0x2000);
    Assert.IsNotNull(found);
  }

  [TestMethod]
  public void Find_RvaExactlyAtEnd_IsExcluded() {
    // EndRva is exclusive -- an RVA equal to EndRva belongs to whatever comes next, not this range.
    var ranges = RuntimeFunctionTable.ParseAmd64(Amd64Entry(0x2000, 0x2200, 0));
    var found = RuntimeFunctionTable.Find(ranges, 0x2200);
    Assert.IsNull(found);
  }

  [TestMethod]
  public void Find_RvaBetweenRanges_ReturnsNull() {
    var ranges = RuntimeFunctionTable.ParseAmd64(Concat(
      Amd64Entry(0x1000, 0x1050, 0),
      Amd64Entry(0x2000, 0x2200, 0)));

    var found = RuntimeFunctionTable.Find(ranges, 0x1800); // Gap between the two functions.

    Assert.IsNull(found);
  }

  [TestMethod]
  public void Find_EmptyList_ReturnsNull() {
    Assert.IsNull(RuntimeFunctionTable.Find(new List<RuntimeFunctionRange>(), 0x1000));
  }
}
