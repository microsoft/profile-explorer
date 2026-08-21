// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Core.Binary;

/// <summary>Why <see cref="FunctionAnalysisPackageBuilder.Build"/> could not produce a package.</summary>
public enum FunctionAnalysisFailure {
  None,
  /// <summary>Address/bounds resolution failed; see <see cref="FunctionAnalysisResult.AddressResolutionFailure"/>
  /// for the specific <see cref="NativeAddressDisassemblyFailure"/>.</summary>
  AddressResolutionFailed,
  /// <summary>Bounds resolved, but semantic re-disassembly of that exact range produced nothing.</summary>
  NoInstructionsDecoded,
  /// <summary>Instructions decoded, but no basic blocks could be constructed from them.</summary>
  CfgConstructionFailed,
  Canceled
}

/// <summary>Result of <see cref="FunctionAnalysisPackageBuilder.Build"/>: either a package, or an
/// explicit failure reason -- never a partially-fabricated package.</summary>
public sealed class FunctionAnalysisResult {
  public bool Success { get; init; }
  public FunctionAnalysisFailure FailureReason { get; init; } = FunctionAnalysisFailure.None;
  public string? ErrorMessage { get; init; }
  public NativeAddressDisassemblyFailure? AddressResolutionFailure { get; init; }
  public FunctionAnalysisPackage? Package { get; init; }

  public static FunctionAnalysisResult Failure(FunctionAnalysisFailure reason, string message,
                                               NativeAddressDisassemblyFailure? addressFailure = null) {
    return new FunctionAnalysisResult {
      Success = false, FailureReason = reason, ErrorMessage = message, AddressResolutionFailure = addressFailure
    };
  }
}

/// <summary>
/// Public, non-GUI, headless entry point for the reverse-engineering enhancement plan's "Function
/// Evidence Generator": given an exact binary path/identity and a single address (module RVA or
/// absolute IP), assembles a <see cref="FunctionAnalysisPackage"/> from every static-analysis tier
/// built in Stages 1-6 (semantic instructions, PE imports/exports/strings, control flow graph, DIA
/// signatures, register reaching-definitions). Lives entirely in ProfileExplorer.Profiling with no
/// TraceEvent/dynamic-observation dependency, per the plan's headless-architecture requirement.
/// </summary>
public static class FunctionAnalysisPackageBuilder {
  public static FunctionAnalysisResult Build(
      string binaryPath,
      long address,
      NativeAddressForm addressForm = NativeAddressForm.ModuleRva,
      FrameAddressKind frameKind = FrameAddressKind.InstructionPointer,
      long? imageBase = null,
      ISymbolDebugInfo? symbolDebugInfo = null,
      FunctionAnalysisDetailLevel detailLevel = FunctionAnalysisDetailLevel.Standard,
      CancellationToken cancellationToken = default) {
    if (cancellationToken.IsCancellationRequested) {
      return FunctionAnalysisResult.Failure(FunctionAnalysisFailure.Canceled, "Canceled before starting.");
    }

    // Reuse the proven bounds-resolution logic (PDB/DIA, then .pdata fallback, return-address
    // normalization, image-identity/ASLR handling) rather than duplicating it.
    var addressResult = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = binaryPath,
      Address = address,
      AddressForm = addressForm,
      FrameKind = frameKind,
      ImageBase = imageBase,
      SymbolDebugInfo = symbolDebugInfo
    });

    if (!addressResult.Success || addressResult.Range is not { } range) {
      return FunctionAnalysisResult.Failure(FunctionAnalysisFailure.AddressResolutionFailed,
        addressResult.ErrorMessage ?? "Address/bounds resolution failed.", addressResult.FailureReason);
    }

    if (cancellationToken.IsCancellationRequested) {
      return FunctionAnalysisResult.Failure(FunctionAnalysisFailure.Canceled, "Canceled after bounds resolution.");
    }

    // Re-disassemble the exact resolved range with semantic detail enabled, needed for CFG/dataflow
    // (the address-resolution pass above only produces DisassembledInstruction, not
    // SemanticInstruction -- see NativeAddressDisassembler's own text-rendering-focused contract).
    using var disassembler = Disassembler.CreateForBinary(binaryPath, symbolDebugInfo!, null,
                                                           enableSemanticDetail: true);

    if (disassembler == null) {
      return FunctionAnalysisResult.Failure(FunctionAnalysisFailure.NoInstructionsDecoded,
        "Failed to initialize the disassembler for semantic re-decoding.");
    }

    var semanticInstructions = disassembler.DisassembleToSemanticList(range.StartRva, range.Size);

    if (semanticInstructions.Count == 0) {
      return FunctionAnalysisResult.Failure(FunctionAnalysisFailure.NoInstructionsDecoded,
        $"Semantic re-disassembly of the resolved range [0x{range.StartRva:X}, 0x{range.EndRva:X}) " +
        "produced no instructions.");
    }

    if (cancellationToken.IsCancellationRequested) {
      return FunctionAnalysisResult.Failure(FunctionAnalysisFailure.Canceled, "Canceled after decoding.");
    }

    var cfg = FunctionControlFlowGraph.Build(semanticInstructions, range.StartRva, range.EndRva);

    if (cfg == null) {
      return FunctionAnalysisResult.Failure(FunctionAnalysisFailure.CfgConstructionFailed,
        "Could not construct a control flow graph from the decoded instructions.");
    }

    var facts = new List<EvidenceNote>();

    // Reference/target resolution needs the PE for imports/exports/strings; reuse one instance.
    using var peInfo = new PEBinaryInfoProvider(binaryPath);
    bool peInitialized = peInfo.Initialize();
    List<ImportedFunctionReference>? imports = null;
    List<ExportedFunctionReference>? exports = null;

    if (peInitialized) {
      imports = peInfo.GetImportedFunctions();
      exports = peInfo.GetExportedFunctions();
    }

    var blockIdByInstructionRva = new Dictionary<long, int>();

    foreach (var block in cfg.Blocks) {
      foreach (var instr in block.Instructions) {
        blockIdByInstructionRva[instr.Rva] = block.Id;
      }
    }

    var analyzedInstructions = new List<AnalyzedInstruction>(semanticInstructions.Count);

    foreach (var instr in semanticInstructions) {
      string? targetName = null;

      if (instr.TargetRva is long targetRva) {
        targetName = symbolDebugInfo?.FindFunctionByRVA(targetRva)?.Name;

        if (targetName == null && peInitialized) {
          var targetRef = ReferenceResolver.Resolve(peInfo, targetRva, imports, exports);

          if (targetRef.Kind != ReferenceKind.Unknown) {
            targetName = targetRef.Name ?? targetRef.Text;
          }
        }
      }

      ResolvedReference? memoryReference = null;

      if (instr.MemoryReferenceRva is long memRva && peInitialized) {
        var resolved = ReferenceResolver.Resolve(peInfo, memRva, imports, exports);

        if (resolved.Kind != ReferenceKind.Unknown) {
          memoryReference = resolved;
        }
      }

      analyzedInstructions.Add(new AnalyzedInstruction(
        instr.Rva, instr.Address, instr.Size, instr.Mnemonic, instr.OperandText,
        blockIdByInstructionRva.TryGetValue(instr.Rva, out int bid) ? bid : -1,
        instr.Groups, instr.RegistersRead, instr.RegistersWritten, instr.HasRegisterAccessDetail,
        instr.TargetRva, targetName, instr.MemoryReferenceRva, memoryReference));
    }

    var analyzedBlocks = cfg.Blocks.Select(b => new AnalyzedBasicBlock(
      b.Id, b.StartRva, b.EndRva, b.SuccessorIds.ToList(), b.PredecessorIds.ToList(),
      b.IsExit, b.EndsInReturn, b.EndsInUnresolvedIndirectTransfer, b.EndsInOutOfRangeTransfer)).ToList();

    // Explicit, honest facts -- absence of evidence is itself evidence (plan design principle).
    facts.Add(new EvidenceNote($"Function bounds resolved via {range.Provenance}.",
      range.Provenance == FunctionBoundaryProvenance.PdbSymbols ? EvidenceProvenance.Pdb : EvidenceProvenance.PeMetadata,
      EvidenceConfidence.High));

    FunctionTypeInfo? signature = null;

    if (symbolDebugInfo is PdbSymbolProvider pdbProvider) {
      var funcInfo = new FunctionDebugInfo(range.FunctionName ?? "", range.StartRva, (uint)range.Size);
      signature = pdbProvider.TryGetFunctionSignature(funcInfo);
    }

    facts.Add(signature != null
      ? new EvidenceNote("PDB/DIA function signature resolved.", EvidenceProvenance.Pdb, EvidenceConfidence.High)
      : new EvidenceNote("No PDB signature available for this function (no PDB supplied, or no " +
                        "SymTagFunction/type record present -- e.g. a public-symbols-only PDB).",
                        EvidenceProvenance.Pdb, EvidenceConfidence.High));

    foreach (var block in cfg.Blocks) {
      if (block.EndsInUnresolvedIndirectTransfer) {
        facts.Add(new EvidenceNote(
          $"Block B{block.Id} (ending at RVA 0x{block.EndRva:X}) ends in an indirect call/jump whose " +
          "target could not be statically resolved.", EvidenceProvenance.Capstone, EvidenceConfidence.High));
      }

      if (block.EndsInOutOfRangeTransfer) {
        facts.Add(new EvidenceNote(
          $"Block B{block.Id} (ending at RVA 0x{block.EndRva:X}) transfers control outside this " +
          "function's analyzed range (e.g. a tail call).", EvidenceProvenance.Capstone, EvidenceConfidence.High));
      }
    }

    var package = new FunctionAnalysisPackage {
      ModuleName = addressResult.ModuleName,
      ImagePath = binaryPath,
      Architecture = addressResult.Architecture,
      FunctionStartRva = range.StartRva,
      FunctionEndRva = range.EndRva,
      BoundaryProvenance = range.Provenance,
      QualifiedName = addressResult.QualifiedName,
      Signature = signature,
      Instructions = analyzedInstructions,
      Blocks = analyzedBlocks,
      Loops = cfg.Loops,
      Facts = facts,
      DetailLevel = detailLevel
    };

    return new FunctionAnalysisResult { Success = true, Package = package };
  }
}
