// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="FunctionAnalysisPackageBuilder"/> -- the capstone "Function
/// Evidence Generator" API that assembles Stages 1-6 (semantic instructions, PE imports, CFG, DIA
/// signatures, register data flow) into one versioned, headless, non-GUI package. Uses a synthetic
/// PE with no PDB (the third-party/no-PDB scenario the whole plan is built around): function
/// bounds come from the PE exception directory (.pdata), and an indirect call through an IAT slot
/// resolves to a real imported API name with its documented KnownApiFacts side effect.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class FunctionAnalysisPackageBuilderTests {
  private static void WriteAsciiZ(byte[] buffer, int offset, string text) {
    var bytes = Encoding.ASCII.GetBytes(text);
    Array.Copy(bytes, 0, buffer, offset, bytes.Length);
    buffer[offset + bytes.Length] = 0;
  }

  // ---------------------------------------------------------------------------------------------
  // Fixture: a 2-instruction function with no PDB.
  //   .text  [0] FF 15 <disp32>   call qword ptr [rip+disp32] -> IAT slot for NTOSKRNL.EXE!ExAllocatePool2
  //          [6] C3               ret
  //   .pdata one RUNTIME_FUNCTION entry covering the whole .text range (bounds fallback, no PDB).
  //   .idata standard import descriptor/ILT/IAT/name layout (same shape as ReferenceResolverTests).
  // ---------------------------------------------------------------------------------------------
  private static (SyntheticPeFile Fixture, int TextRva, long IatRva) BuildFixture() {
    const string moduleName = "NTOSKRNL.EXE";
    const string functionName = "ExAllocatePool2";

    var textBytes = new byte[7]; // call (6 bytes) + ret (1 byte).

    int importByNameOffset = 72;
    int importByNameSize = 2 + functionName.Length + 1;
    int moduleNameOffset = importByNameOffset + importByNameSize;
    int idataSize = moduleNameOffset + moduleName.Length + 1;
    var idataBytes = new byte[idataSize];

    var pdataBytes = new byte[12]; // one RUNTIME_FUNCTION entry: BeginAddress, EndAddress, UnwindInfoAddress.

    var rvas = SyntheticPeBuilder.ComputeSectionRvas(3, new[] { textBytes.Length, pdataBytes.Length, idataSize });
    int textRva = rvas[0];
    int pdataRva = rvas[1];
    int idataRva = rvas[2];

    long ilRva = idataRva + 40;
    long iatRva = idataRva + 56;
    long importByNameRva = idataRva + importByNameOffset;
    long moduleNameRva = idataRva + moduleNameOffset;

    BitConverter.GetBytes((uint)ilRva).CopyTo(idataBytes, 0);
    BitConverter.GetBytes((uint)moduleNameRva).CopyTo(idataBytes, 12);
    BitConverter.GetBytes((uint)iatRva).CopyTo(idataBytes, 16);
    BitConverter.GetBytes((long)importByNameRva).CopyTo(idataBytes, 40);
    BitConverter.GetBytes((long)importByNameRva).CopyTo(idataBytes, 56);
    BitConverter.GetBytes((ushort)0).CopyTo(idataBytes, importByNameOffset);
    WriteAsciiZ(idataBytes, importByNameOffset + 2, functionName);
    WriteAsciiZ(idataBytes, moduleNameOffset, moduleName);

    long instrEndRva = textRva + 6;
    int disp32 = (int)(iatRva - instrEndRva);
    textBytes[0] = 0xFF;
    textBytes[1] = 0x15;
    BitConverter.GetBytes(disp32).CopyTo(textBytes, 2);
    textBytes[6] = 0xC3; // ret

    BitConverter.GetBytes((uint)textRva).CopyTo(pdataBytes, 0);
    BitConverter.GetBytes((uint)(textRva + textBytes.Length)).CopyTo(pdataBytes, 4);
    BitConverter.GetBytes((uint)0).CopyTo(pdataBytes, 8); // UnwindInfoAddress -- unused by RuntimeFunctionTable.

    var sections = new List<SyntheticPeBuilder.Section> {
      new(".text", textBytes, IsExecutableCode: true),
      new(".pdata", pdataBytes, IsExecutableCode: false),
      new(".idata", idataBytes, IsExecutableCode: false)
    };

    var peBytes = SyntheticPeBuilder.Build((ushort)Machine.Amd64, sections,
      exceptionTableSectionIndex: 1, exceptionTableSize: (uint)pdataBytes.Length,
      importTableSectionIndex: 2, importTableSize: (uint)idataSize);

    var fixture = new SyntheticPeFile(peBytes, "SyntheticAnalysisPackage.dll");
    return (fixture, textRva, iatRva);
  }

  [TestMethod]
  public void Build_NoPdbFixtureWithImportCall_ProducesCompletePackage() {
    var (fixture, textRva, iatRva) = BuildFixture();
    using var _ = fixture;

    var result = FunctionAnalysisPackageBuilder.Build(fixture.Path, textRva,
      NativeAddressForm.ModuleRva, FrameAddressKind.InstructionPointer,
      imageBase: null, symbolDebugInfo: null, detailLevel: FunctionAnalysisDetailLevel.Full);

    Assert.IsTrue(result.Success, result.ErrorMessage);
    var package = result.Package!;

    Console.WriteLine(package.ToPromptMarkdown());

    Assert.AreEqual(Machine.Amd64, package.Architecture);
    Assert.AreEqual(textRva, package.FunctionStartRva);
    Assert.AreEqual(textRva + 7, package.FunctionEndRva);
    Assert.AreEqual(FunctionBoundaryProvenance.ExceptionDirectory, package.BoundaryProvenance);
    Assert.IsNull(package.Signature, "No PDB was supplied -- signature must be null, not fabricated.");

    Assert.AreEqual(2, package.Instructions.Count);
    var call = package.Instructions[0];
    var ret = package.Instructions[1];

    Assert.IsTrue(call.Groups.HasFlag(InstructionGroupFlags.Call));
    Assert.AreEqual(iatRva, call.MemoryReferenceRva);
    Assert.IsNotNull(call.MemoryReference);
    Assert.AreEqual(ReferenceKind.ImportedFunction, call.MemoryReference!.Kind);
    Assert.AreEqual("NTOSKRNL.EXE!ExAllocatePool2", call.MemoryReference.Name);
    Assert.IsNotNull(call.MemoryReference.ApiFact);
    Assert.AreEqual(ApiSideEffectCategory.MemoryAllocation, call.MemoryReference.ApiFact!.Category);

    Assert.IsTrue(ret.Groups.HasFlag(InstructionGroupFlags.Return));

    // Single straight-line block: no branches, so both instructions share one block, ending in return.
    Assert.AreEqual(1, package.Blocks.Count);
    Assert.AreEqual(call.BlockId, ret.BlockId);
    Assert.IsTrue(package.Blocks[0].IsExit);
    Assert.IsTrue(package.Blocks[0].EndsInReturn);
    Assert.AreEqual(0, package.Loops.Count);

    // Explicit, honest facts: bounds provenance and the no-PDB-signature gap must both be present.
    Assert.IsTrue(package.Facts.Any(f => f.Fact.Contains("ExceptionDirectory") && f.Confidence == EvidenceConfidence.High));
    Assert.IsTrue(package.Facts.Any(f => f.Fact.Contains("No PDB signature available")));

    string promptText = package.ToPromptMarkdown();
    StringAssert.Contains(promptText, "No PDB signature available");
    StringAssert.Contains(promptText, "### Basic Blocks");
    StringAssert.Contains(promptText, "NTOSKRNL.EXE!ExAllocatePool2");
    StringAssert.Contains(promptText, "```text");
  }

  /// <summary>
  /// Verifies <see cref="FunctionAnalysisPackage.ToPromptMarkdown"/> produces genuinely
  /// well-formed Markdown, not merely plain text: real headers, a balanced fenced code block
  /// around the assembly listing, and the "&lt;unresolved&gt;"-style placeholder (which looks like
  /// an HTML tag to a naive renderer) always wrapped in backticks rather than left bare. Confirmed
  /// end-to-end against a real CommonMark parser (marked.js) during development: the previous
  /// plain-text rendering collapsed every line into one paragraph and lost the bare
  /// "&lt;no PDB signature available&gt;" placeholder entirely.
  /// </summary>
  [TestMethod]
  public void ToPromptMarkdown_ProducesWellFormedMarkdownStructure() {
    var (fixture, textRva, _) = BuildFixture();
    using var _ = fixture;

    var result = FunctionAnalysisPackageBuilder.Build(fixture.Path, textRva,
      detailLevel: FunctionAnalysisDetailLevel.Full);
    Assert.IsTrue(result.Success, result.ErrorMessage);

    string markdown = result.Package!.ToPromptMarkdown();
    var lines = markdown.Split('\n');

    Assert.IsTrue(lines.Any(l => l.StartsWith("## Function")), "Expected an H2 function header.");
    Assert.IsTrue(lines.Any(l => l.StartsWith("### Signature")), "Expected an H3 Signature header.");
    Assert.IsTrue(lines.Any(l => l.StartsWith("### Facts")), "Expected an H3 Facts header.");
    Assert.IsTrue(lines.Any(l => l.StartsWith("### Basic Blocks")), "Expected an H3 Basic Blocks header.");
    Assert.IsTrue(lines.Any(l => l.StartsWith("### Suggested Analysis Questions")),
      "Expected an H3 Suggested Analysis Questions header for Full detail level.");

    // The fenced code block around the assembly listing must open and close in balanced pairs.
    int fenceCount = lines.Count(l => l.TrimEnd() == "```text" || l.TrimEnd() == "```");
    Assert.IsTrue(fenceCount > 0 && fenceCount % 2 == 0, $"Expected a balanced number of code fences, got {fenceCount}.");

    // The "<unresolved>"/"<unknown+0xRVA>" style placeholder must never appear bare -- it must
    // always be wrapped in backticks, or a naive Markdown-to-HTML renderer will swallow it as an
    // unrecognized HTML tag (confirmed with marked.js: this exact bug is why this test exists).
    var functionHeaderLine = lines.Single(l => l.StartsWith("## Function"));
    Assert.IsTrue(functionHeaderLine.Contains('`'), "The function label must be wrapped in backticks.");
    int firstBacktick = functionHeaderLine.IndexOf('`');
    int lastBacktick = functionHeaderLine.LastIndexOf('`');
    Assert.IsTrue(firstBacktick < functionHeaderLine.IndexOf('<') && functionHeaderLine.IndexOf('<') < lastBacktick,
      "Any '<' in the function label must fall inside the backtick-wrapped span, not bare in prose.");
  }

  /// <summary>
  /// Verifies the "Suggested Analysis Questions" section -- one question per
  /// <see cref="AccuracyDimension"/>, matching the rubric these evaluations are later scored
  /// against -- appears for Standard/Full detail but is omitted at Compact, respecting the
  /// caller's stated context budget.
  /// </summary>
  [TestMethod]
  public void ToPromptMarkdown_SuggestedQuestions_PresentUnlessCompact() {
    var (fixture, textRva, _) = BuildFixture();
    using var _ = fixture;

    var standardResult = FunctionAnalysisPackageBuilder.Build(fixture.Path, textRva,
      detailLevel: FunctionAnalysisDetailLevel.Standard);
    Assert.IsTrue(standardResult.Success, standardResult.ErrorMessage);
    string standardMarkdown = standardResult.Package!.ToPromptMarkdown();
    StringAssert.Contains(standardMarkdown, "### Suggested Analysis Questions");
    StringAssert.Contains(standardMarkdown, "how many times it runs"); // sanity check a real question rendered.

    var compactResult = FunctionAnalysisPackageBuilder.Build(fixture.Path, textRva,
      detailLevel: FunctionAnalysisDetailLevel.Compact);
    Assert.IsTrue(compactResult.Success, compactResult.ErrorMessage);
    string compactMarkdown = compactResult.Package!.ToPromptMarkdown();
    Assert.IsFalse(compactMarkdown.Contains("Suggested Analysis Questions"),
      "Compact detail level should omit the suggested-questions section to respect a minimal context budget.");
  }

  [TestMethod]
  public void Build_RvaOutsideImage_ReturnsExplicitFailureNotException() {
    var (fixture, _, _) = BuildFixture();
    using var _ = fixture;

    var result = FunctionAnalysisPackageBuilder.Build(fixture.Path, 0x0FFF_FFFF);

    Assert.IsFalse(result.Success);
    Assert.AreEqual(FunctionAnalysisFailure.AddressResolutionFailed, result.FailureReason);
    Assert.AreEqual(NativeAddressDisassemblyFailure.RvaOutOfImageRange, result.AddressResolutionFailure);
    Assert.IsNull(result.Package);
  }

  [TestMethod]
  public void Build_NonexistentBinary_ReturnsExplicitFailure() {
    var result = FunctionAnalysisPackageBuilder.Build(@"C:\nonexistent\fake.dll", 0x1000);

    Assert.IsFalse(result.Success);
    Assert.AreEqual(FunctionAnalysisFailure.AddressResolutionFailed, result.FailureReason);
    Assert.AreEqual(NativeAddressDisassemblyFailure.BinaryNotFound, result.AddressResolutionFailure);
  }

  [TestMethod]
  public void Build_AlreadyCanceledToken_ReturnsCanceledFailureWithoutThrowing() {
    var (fixture, textRva, _) = BuildFixture();
    using var _ = fixture;

    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var result = FunctionAnalysisPackageBuilder.Build(fixture.Path, textRva, cancellationToken: cts.Token);

    Assert.IsFalse(result.Success);
    Assert.AreEqual(FunctionAnalysisFailure.Canceled, result.FailureReason);
  }
}
