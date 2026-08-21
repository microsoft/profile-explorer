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
  /// Renders a compact, deterministic, human/LLM-readable text view of this package: signature,
  /// facts, and a block list with successor/predecessor edges and (for Full detail) per-instruction
  /// text -- the plan's "CFG as structured text, never an image" design decision. This is a
  /// convenience rendering alongside the structured object model, not a replacement for it.
  /// </summary>
  public string ToPromptText() {
    var sb = new StringBuilder();
    sb.AppendLine($"Function: {QualifiedName ?? "<unresolved>"} @ {ModuleName ?? "<unknown-module>"}+0x{FunctionStartRva:X} " +
                  $"(arch: {Architecture?.ToString() ?? "unknown"}, bounds: {BoundaryProvenance})");

    if (Signature != null) {
      string paramList = string.Join(", ", Signature.Parameters.Select(p => $"{p.TypeName ?? "?"} {p.Name ?? "?"}"));
      sb.AppendLine($"Signature: {Signature.CallingConvention} {Signature.ReturnTypeName} (" + paramList + ")");
    }
    else {
      sb.AppendLine("Signature: <no PDB signature available>");
    }

    if (Loops.Count > 0) {
      sb.AppendLine($"Loops: {Loops.Count}");
      foreach (var loop in Loops) {
        sb.AppendLine($"  Header=B{loop.HeaderBlockId} BackEdgeFrom=B{loop.BackEdgeSourceBlockId} " +
                      $"Body=[{string.Join(",", loop.BodyBlockIds.Select(b => $"B{b}"))}]");
      }
    }
    else {
      sb.AppendLine("Loops: none");
    }

    sb.AppendLine();
    sb.AppendLine("Facts:");

    if (Facts.Count == 0) {
      sb.AppendLine("  (none)");
    }
    else {
      foreach (var fact in Facts) {
        sb.AppendLine($"  [{fact.Confidence}/{fact.Provenance}] {fact.Fact}");
      }
    }

    sb.AppendLine();
    sb.AppendLine("Basic Blocks:");

    foreach (var block in Blocks) {
      string flags = block.IsExit ? " [exit]" : "";
      sb.AppendLine($"Block B{block.Id}{flags} [0x{block.StartRva:X}..0x{block.EndRva:X}) " +
                    $"succ=[{string.Join(",", block.SuccessorIds.Select(s => $"B{s}"))}] " +
                    $"pred=[{string.Join(",", block.PredecessorIds.Select(p => $"B{p}"))}]");

      if (DetailLevel == FunctionAnalysisDetailLevel.Full) {
        foreach (var instr in Instructions) {
          if (instr.BlockId != block.Id) {
            continue;
          }

          string target = instr.TargetName != null ? $"  ; -> {instr.TargetName}"
            : instr.MemoryReference is { Kind: not ReferenceKind.Unknown } mr
              ? $"  ; -> {mr.Name ?? mr.Text ?? mr.Kind.ToString()}"
              : "";

          sb.AppendLine($"  +0x{instr.Rva:X6}  {instr.Mnemonic} {instr.OperandText}{target}");
        }
      }
    }

    return sb.ToString();
  }
}
