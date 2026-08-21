// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// How much debug/symbol information is expected to be available for a
/// <see cref="BenchmarkCase"/>'s binary -- the primary stratification axis the reverse-engineering
/// enhancement plan calls for (alongside optimization level and inlining depth), since it is the
/// single biggest driver of achievable evidence quality for real third-party code.
/// </summary>
public enum SymbolAvailabilityTier {
  /// <summary>A full private PDB with SymTagFunction/type records is available.</summary>
  FullPdb,
  /// <summary>A PDB exists but has no function-type records (e.g. a public-symbols-only PDB,
  /// common for shipped binaries) -- confirmed to occur in this repo's own MSO test fixture.</summary>
  PublicSymbolsOnlyPdb,
  /// <summary>No PDB at all -- function bounds must come from the PE exception directory
  /// (.pdata), and no signature/parameter names are available. The realistic worst case for
  /// third-party code the plan is built around.</summary>
  NoPdb
}

/// <summary>
/// One benchmark corpus entry: an exact binary + optional PDB + a function to analyze, tagged with
/// its expected <see cref="SymbolAvailabilityTier"/> so results can be stratified rather than
/// averaged into a single misleading number (the plan's "avoid survivorship bias" requirement,
/// informed by the REFORGE paper's finding that ignoring optimization/alignment uncertainty
/// overstates accuracy). <paramref name="KnownFunctionRva"/> is required for the
/// <see cref="SymbolAvailabilityTier.NoPdb"/> tier (there is, by definition, no PDB to resolve a
/// name from) -- exactly matching the real scenario this plan targets, where FUN AI already knows
/// the RVA from an ETW stack/allocation site rather than looking it up by name.
/// </summary>
public sealed record BenchmarkCase(
  string Name, string BinaryPath, string? PdbPath, string FunctionName,
  SymbolAvailabilityTier ExpectedTier, long? KnownFunctionRva = null);

/// <summary>
/// Automatable outcome for one <see cref="BenchmarkCase"/>: whether
/// <see cref="FunctionAnalysisPackageBuilder"/> succeeded, and what evidence it actually produced.
/// This measures *coverage* (how much evidence the static pipeline can produce) -- it does not (and
/// cannot, without an LLM call and a human rubric) measure AI-generated pseudocode accuracy. See
/// <see cref="BenchmarkReport"/>'s remarks for why that final step is explicitly out of scope here.
/// </summary>
public sealed record BenchmarkCaseResult(
  string CaseName, SymbolAvailabilityTier ExpectedTier, bool Success,
  string? FailureReason, bool HasSignature, FunctionBoundaryProvenance? BoundaryProvenance,
  int InstructionCount, int BlockCount, int LoopCount,
  int ResolvedCallOrReferenceCount, int UnresolvedIndirectTransferCount,
  string? AssemblyOnlyText, string? EnhancedPromptText);

/// <summary>
/// Aggregated coverage-rate metrics for one <see cref="SymbolAvailabilityTier"/> across a
/// benchmark run -- the "third-party coverage rate" metric called for once the binary/PDB
/// availability risk was identified during this project's development.
/// </summary>
public sealed record BenchmarkTierSummary(
  SymbolAvailabilityTier Tier, int CaseCount, int SucceededCount, int HasSignatureCount) {
  public double SuccessRate => CaseCount == 0 ? 0 : (double)SucceededCount / CaseCount;
  public double SignatureAvailabilityRate => CaseCount == 0 ? 0 : (double)HasSignatureCount / CaseCount;
}

/// <summary>
/// The full result of a benchmark run: per-case results plus per-tier summaries.
/// <para>
/// <b>Scope boundary (intentional, not a gap to silently paper over):</b> this harness measures
/// static-evidence *coverage* -- whether <see cref="FunctionAnalysisPackageBuilder"/> can produce
/// bounds/CFG/signature/reference evidence for a case -- which is fully automatable and requires no
/// external dependency. It deliberately does NOT attempt to score AI-generated pseudocode accuracy
/// against a human rubric: that requires (a) an LLM call to actually generate pseudocode from
/// <see cref="BenchmarkCaseResult.EnhancedPromptText"/>/<see cref="BenchmarkCaseResult.AssemblyOnlyText"/>,
/// and (b) an expert human reviewer scoring the result against known source -- neither of which
/// this engineering session can fabricate or simulate. <see cref="BenchmarkCaseResult"/> exposes
/// both prompt variants specifically so that downstream step (a permanent assembly-only control
/// arm vs. the enhanced package) can be wired up by a caller with LLM/human-review access.
/// </para>
/// </summary>
public sealed class BenchmarkReport {
  public IReadOnlyList<BenchmarkCaseResult> CaseResults { get; init; } = Array.Empty<BenchmarkCaseResult>();
  public IReadOnlyList<BenchmarkTierSummary> TierSummaries { get; init; } = Array.Empty<BenchmarkTierSummary>();
}

/// <summary>
/// Runs a corpus of <see cref="BenchmarkCase"/> entries through <see cref="FunctionAnalysisPackageBuilder"/>
/// and reports automatable coverage metrics, stratified by <see cref="SymbolAvailabilityTier"/>.
/// This is the reverse-engineering enhancement plan's benchmark *harness infrastructure*; growing
/// the corpus (more functions, optimization levels, architectures) and wiring an actual LLM +
/// human-rubric scoring step on top of <see cref="BenchmarkCaseResult"/> are follow-on work.
/// </summary>
public static class BenchmarkRunner {
  public static BenchmarkReport Run(IReadOnlyList<BenchmarkCase> cases,
                                    Func<string, ISymbolDebugInfo?>? symbolProviderFactory = null) {
    var results = new List<BenchmarkCaseResult>(cases.Count);

    foreach (var benchmarkCase in cases) {
      results.Add(RunOne(benchmarkCase, symbolProviderFactory));
    }

    var summaries = results
      .GroupBy(r => r.ExpectedTier)
      .Select(g => new BenchmarkTierSummary(g.Key, g.Count(), g.Count(r => r.Success),
                                            g.Count(r => r.HasSignature)))
      .OrderBy(s => s.Tier)
      .ToList();

    return new BenchmarkReport { CaseResults = results, TierSummaries = summaries };
  }

  private static BenchmarkCaseResult RunOne(BenchmarkCase benchmarkCase,
                                            Func<string, ISymbolDebugInfo?>? symbolProviderFactory) {
    ISymbolDebugInfo? symbolDebugInfo = null;

    if (benchmarkCase.PdbPath != null && symbolProviderFactory != null) {
      symbolDebugInfo = symbolProviderFactory(benchmarkCase.PdbPath);
    }

    long? functionRva = benchmarkCase.KnownFunctionRva;

    if (functionRva == null) {
      functionRva = symbolDebugInfo?.FindFunction(benchmarkCase.FunctionName)?.RVA;
    }

    if (functionRva == null) {
      // Fall back to enumerating all functions and matching by name -- FindFunction may miss
      // demangling/lookup edge cases some providers have (see PdbSymbolProvider's own fallback).
      functionRva = symbolDebugInfo?.GetSortedFunctions()
        .FirstOrDefault(f => f.Name != null && f.Name.Contains(benchmarkCase.FunctionName))?.RVA;
    }

    if (functionRva == null) {
      return new BenchmarkCaseResult(benchmarkCase.Name, benchmarkCase.ExpectedTier, false,
        $"Could not resolve function '{benchmarkCase.FunctionName}' by name (no PDB supplied, or " +
        "not found) -- caller must supply BenchmarkCase.KnownFunctionRva for the NoPdb tier.",
        false, null, 0, 0, 0, 0, 0, null, null);
    }

    var result = FunctionAnalysisPackageBuilder.Build(benchmarkCase.BinaryPath, functionRva.Value,
      NativeAddressForm.ModuleRva, FrameAddressKind.InstructionPointer, imageBase: null,
      symbolDebugInfo: symbolDebugInfo, detailLevel: FunctionAnalysisDetailLevel.Full);

    if (!result.Success || result.Package is not { } package) {
      return new BenchmarkCaseResult(benchmarkCase.Name, benchmarkCase.ExpectedTier, false,
        result.ErrorMessage, false, null, 0, 0, 0, 0, 0, null, null);
    }

    int resolvedCount = package.Instructions.Count(i =>
      (i.TargetRva != null && i.TargetName != null) || i.MemoryReference is { Kind: not ReferenceKind.Unknown });
    int unresolvedIndirectCount = package.Blocks.Count(b => b.EndsInUnresolvedIndirectTransfer);

    string assemblyOnlyText = string.Join("\n", package.Instructions.Select(i => $"{i.Mnemonic} {i.OperandText}"));

    return new BenchmarkCaseResult(benchmarkCase.Name, benchmarkCase.ExpectedTier, true, null,
      package.Signature != null, package.BoundaryProvenance,
      package.Instructions.Count, package.Blocks.Count, package.Loops.Count,
      resolvedCount, unresolvedIndirectCount, assemblyOnlyText, package.ToPromptText());
  }
}
