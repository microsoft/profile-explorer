// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ProfileExplorer.Core.Binary;

/// <summary>How much evidence a <see cref="FunctionAnalysisPackage"/> should include -- a context
/// budget control for AI-facing consumers with limited prompt space.</summary>
public enum FunctionAnalysisDetailLevel {
  /// <summary>Blocks, signature, and facts only -- no per-instruction detail.</summary>
  Compact,
  /// <summary>Everything except full register/reference detail on every instruction.</summary>
  Standard,
  /// <summary>Full per-instruction detail: registers, resolved targets/references, block ids.</summary>
  Full
}

/// <summary>Where a fact in <see cref="FunctionAnalysisPackage.Facts"/> came from.</summary>
public enum EvidenceProvenance { Pdb, PeMetadata, Capstone, KnownApi, Heuristic }

/// <summary>How much a fact should be trusted -- callers (and any AI prompt built from a package)
/// must never upgrade a Low/Medium fact to certainty.</summary>
public enum EvidenceConfidence { High, Medium, Low }

/// <summary>One evidence-backed fact, gap, or warning about the analyzed function.</summary>
public sealed record EvidenceNote(string Fact, EvidenceProvenance Provenance, EvidenceConfidence Confidence);

/// <summary>One instruction enriched with everything resolvable from Stages 1-6: which basic
/// block it belongs to, and (when a target/memory reference exists) what it resolves to.</summary>
public sealed record AnalyzedInstruction(
  long Rva, long Address, int Size, string Mnemonic, string OperandText, int BlockId,
  InstructionGroupFlags Groups,
  IReadOnlyList<string> RegistersRead, IReadOnlyList<string> RegistersWritten, bool HasRegisterAccessDetail,
  long? TargetRva, string? TargetName,
  long? MemoryReferenceRva, ResolvedReference? MemoryReference);

/// <summary>One basic block, as seen by the AI evidence package (a thin projection of <see cref="BasicBlock"/>).</summary>
public sealed record AnalyzedBasicBlock(int Id, long StartRva, long EndRva, IReadOnlyList<int> SuccessorIds,
                                        IReadOnlyList<int> PredecessorIds, bool IsExit, bool EndsInReturn,
                                        bool EndsInUnresolvedIndirectTransfer, bool EndsInOutOfRangeTransfer);

/// <summary>
/// The reverse-engineering enhancement plan's "Function Evidence Generator" output: a versioned,
/// self-contained package of static facts about one caller-selected function, assembled from
/// Stages 1-6 (semantic instructions, PE imports/exports/strings, CFG, DIA signatures, register
/// reaching-definitions). Contains no dynamic observations and makes no whole-program claims --
/// only what is decodable/resolvable for this one function's bounded range. Never contains
/// AI-generated pseudocode itself; it is the evidence an external caller (e.g. FUN AI) supplies to
/// an LLM, with facts and provenance kept separate from inference throughout.
/// </summary>
public sealed class FunctionAnalysisPackage {
  /// <summary>Schema version -- bump when the shape changes in a way a consumer should react to.</summary>
  public string FormatVersion { get; init; } = "1.0";

  public string? ModuleName { get; init; }
  public string? ImagePath { get; init; }
  public Machine? Architecture { get; init; }

  public long FunctionStartRva { get; init; }
  public long FunctionEndRva { get; init; }
  public FunctionBoundaryProvenance BoundaryProvenance { get; init; }

  /// <summary>PDB-resolved name when available, else the stable unresolved form from
  /// <see cref="NativeAddressDisassemblyResult.QualifiedName"/> (e.g. "module!&lt;unknown+0xRVA&gt;").</summary>
  public string? QualifiedName { get; init; }

  /// <summary>PDB/DIA-recovered signature (see <see cref="PdbSymbolProvider.TryGetFunctionSignature"/>).
  /// Null when no PDB was supplied or the function has no function-type record -- never fabricated.</summary>
  public FunctionTypeInfo? Signature { get; init; }

  public IReadOnlyList<AnalyzedInstruction> Instructions { get; init; } = Array.Empty<AnalyzedInstruction>();
  public IReadOnlyList<AnalyzedBasicBlock> Blocks { get; init; } = Array.Empty<AnalyzedBasicBlock>();
  public IReadOnlyList<NaturalLoop> Loops { get; init; } = Array.Empty<NaturalLoop>();

  /// <summary>Explicit facts, gaps, and warnings -- e.g. "no PDB signature available", "block 3 ends
  /// in an unresolved indirect call". Absence of evidence is itself evidence; see the plan's design
  /// principle that unresolved facts are first-class output, never silently omitted.</summary>
  public IReadOnlyList<EvidenceNote> Facts { get; init; } = Array.Empty<EvidenceNote>();

  public FunctionAnalysisDetailLevel DetailLevel { get; init; }

  /// <summary>
  /// Renders a compact, deterministic, human/LLM-readable **Markdown** view of this package:
  /// signature, facts, and a block list with successor/predecessor edges and (for Full detail)
  /// per-instruction assembly -- the "CFG as structured text, never an image" design decision.
  /// Genuinely valid Markdown, not merely plain text: headers/bullets for prose sections, and the
  /// per-block assembly listing wrapped in a fenced code block so raw disassembly text (which can
  /// contain `*`, `_`, `[`, `]`, `&lt;`/`&gt;` -- e.g. "char*", "[rip+N]", decorated import names)
  /// is preserved literally and never misinterpreted as Markdown syntax by a renderer. This is a
  /// convenience rendering alongside the fully structured object model, not a replacement for it.
  /// </summary>
  public string ToPromptMarkdown() {
    var sb = new StringBuilder();
    string functionLabel = QualifiedName ?? "<unresolved>";
    sb.AppendLine($"## Function {WrapInlineCode(functionLabel)}");
    sb.AppendLine();
    sb.AppendLine($"- Module: {WrapInlineCode(ModuleName ?? "unknown")}");
    sb.AppendLine($"- Address: {WrapInlineCode($"+0x{FunctionStartRva:X}")} (architecture: {Architecture?.ToString() ?? "unknown"})");
    sb.AppendLine($"- Bounds provenance: {BoundaryProvenance}");
    sb.AppendLine();

    sb.AppendLine("### Signature");
    sb.AppendLine();

    if (Signature != null) {
      string paramList = string.Join(", ", Signature.Parameters.Select(p => $"{p.TypeName ?? "?"} {p.Name ?? "?"}"));
      sb.AppendLine(WrapInlineCode($"{Signature.CallingConvention} {Signature.ReturnTypeName} ({paramList})"));
    }
    else {
      sb.AppendLine("*No PDB signature available.*");
    }

    sb.AppendLine();
    sb.AppendLine("### Loops");
    sb.AppendLine();

    if (Loops.Count == 0) {
      sb.AppendLine("*None.*");
    }
    else {
      foreach (var loop in Loops) {
        sb.AppendLine($"- Header=B{loop.HeaderBlockId}, BackEdgeFrom=B{loop.BackEdgeSourceBlockId}, " +
                      $"Body=[{string.Join(",", loop.BodyBlockIds.Select(b => $"B{b}"))}]");
      }
    }

    sb.AppendLine();
    sb.AppendLine("### Facts");
    sb.AppendLine();

    if (Facts.Count == 0) {
      sb.AppendLine("*None.*");
    }
    else {
      foreach (var fact in Facts) {
        sb.AppendLine($"- **[{fact.Confidence}/{fact.Provenance}]** {EscapeMarkdownProse(fact.Fact)}");
      }
    }

    sb.AppendLine();
    sb.AppendLine("### Basic Blocks");

    foreach (var block in Blocks) {
      string flags = block.IsExit ? " *(exit)*" : "";
      sb.AppendLine();
      sb.AppendLine($"**Block B{block.Id}**{flags} — range {WrapInlineCode($"[0x{block.StartRva:X}..0x{block.EndRva:X})")}, " +
                    $"successors=[{string.Join(",", block.SuccessorIds.Select(s => $"B{s}"))}], " +
                    $"predecessors=[{string.Join(",", block.PredecessorIds.Select(p => $"B{p}"))}]");

      if (DetailLevel == FunctionAnalysisDetailLevel.Full) {
        // A fenced code block: assembly text is preserved exactly (whitespace, brackets, `<`/`>`,
        // asterisks in e.g. "char*") and is never reinterpreted as Markdown by any conformant
        // renderer, unlike the plain paragraph text this method used to emit.
        sb.AppendLine("```text");

        foreach (var instr in Instructions) {
          if (instr.BlockId != block.Id) {
            continue;
          }

          string target = instr.TargetName != null ? $"  ; -> {instr.TargetName}"
            : instr.MemoryReference is { Kind: not ReferenceKind.Unknown } mr
              ? $"  ; -> {mr.Name ?? mr.Text ?? mr.Kind.ToString()}"
              : "";

          sb.AppendLine($"+0x{instr.Rva:X6}  {instr.Mnemonic} {instr.OperandText}{target}");
        }

        sb.AppendLine("```");
      }
    }

    return sb.ToString();
  }

  /// <summary>
  /// Wraps <paramref name="text"/> as a Markdown inline code span, choosing a backtick-fence
  /// length one longer than the longest run of backticks already present in the text (per
  /// CommonMark's inline-code-span rule) so the span can never be broken out of early. Needed
  /// because compiler-demangled C++ names can themselves contain backticks (e.g. the MSVC
  /// "`vector deleting destructor'" special-member-function name).
  /// </summary>
  private static string WrapInlineCode(string text) {
    int longestRun = 0;
    int currentRun = 0;

    foreach (char c in text) {
      if (c == '`') {
        currentRun++;
        longestRun = Math.Max(longestRun, currentRun);
      }
      else {
        currentRun = 0;
      }
    }

    string fence = new string('`', longestRun + 1);
    // CommonMark also requires a space-padded fence when the content starts/ends with a backtick
    // or is empty, to avoid the fence visually merging with the content.
    bool needsPadding = text.Length == 0 || text[0] == '`' || text[^1] == '`';
    return needsPadding ? $"{fence} {text} {fence}" : $"{fence}{text}{fence}";
  }

  /// <summary>
  /// Escapes Markdown inline-emphasis/link/heading-triggering characters in free-form prose text
  /// (the <see cref="Facts"/> bullet list) so a fact that happens to embed a resolved name/path
  /// (e.g. containing underscores or asterisks) can never be misinterpreted as emphasis or a link.
  /// Not used for the per-block assembly listing, which is already protected by a fenced code
  /// block instead (escaping would be both unnecessary and would corrupt the literal text there).
  /// </summary>
  private static string EscapeMarkdownProse(string text) {
    var sb = new StringBuilder(text.Length);

    foreach (char c in text) {
      if (c is '*' or '_' or '`' or '[' or ']' or '<' or '>') {
        sb.Append('\\');
      }

      sb.Append(c);
    }

    return sb.ToString();
  }
}
