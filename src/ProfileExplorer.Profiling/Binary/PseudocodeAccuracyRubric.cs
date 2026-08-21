// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// Which evidence variant was shown to the AI/human when producing a pseudocode explanation --
/// the plan's required comparison arms, so accuracy gains are always measured as a delta over a
/// permanent baseline rather than in isolation.
/// </summary>
public enum PseudocodeEvidenceArm {
  /// <summary>Raw assembly only -- the permanent control arm every other arm must beat.</summary>
  AssemblyOnly,
  /// <summary>Profile Explorer's pre-existing annotated disassembly (symbols/source lines, no
  /// CFG/reference/data-flow evidence).</summary>
  CurrentProfileExplorerAnnotations,
  /// <summary><see cref="FunctionAnalysisPackage"/> (this plan's Stages 1-7).</summary>
  EnhancedAnalysisPackage,
  /// <summary>Ghidra's native decompiler/context, used as the external reference ceiling.</summary>
  GhidraDecompiler
}

/// <summary>
/// One scored dimension of a pseudocode explanation's accuracy, per the plan's rubric (behavior,
/// control flow, calls/APIs, data accesses, loop bounds, error handling, externally visible
/// effects) -- scored independently rather than as one blended number, since a semantically correct
/// explanation with a wrong API call is a very different failure from a correct control-flow
/// skeleton with hallucinated data accesses.
/// </summary>
public enum AccuracyDimension {
  OverallBehavior,
  ControlFlow,
  CallsAndApis,
  DataAccesses,
  LoopBounds,
  ErrorHandling,
  ExternallyVisibleEffects
}

/// <summary>
/// A single dimension score from an expert human reviewer (never an automated text-similarity
/// metric such as BLEU -- the paper this plan cites found those unreliable for semantically valid
/// but textually different explanations). <see cref="Score"/> is expected to be in the range 0-5:
/// 0 = fabricated/wrong, 3 = partially correct with a caveat, 5 = fully correct and verifiable
/// against source. Not runtime-enforced (this is a data schema for a human-populated score, not a
/// security boundary) -- reviewers/tooling populating this type are expected to respect the range.
/// </summary>
public sealed record DimensionScore(AccuracyDimension Dimension, int Score, string? ReviewerNote);

/// <summary>
/// One human-scored evaluation of a pseudocode explanation produced from one
/// <see cref="PseudocodeEvidenceArm"/> for one benchmark case. This is a data schema only -- no
/// scores are fabricated or estimated by this codebase. Populating <see cref="Dimensions"/>
/// requires (a) an LLM call that actually generates the pseudocode/explanation from the arm's
/// evidence text, and (b) an expert human reviewer comparing it against the case's real source,
/// neither of which this engineering session has access to run. This type exists so that pipeline
/// -- once wired to an LLM and a reviewer -- has a concrete, structured place to record results
/// consistent with the rest of this plan's evidence-vs-inference discipline.
/// </summary>
public sealed record PseudocodeEvaluation(
  string CaseName, PseudocodeEvidenceArm Arm, IReadOnlyList<DimensionScore> Dimensions, string ReviewerId, DateTime ScoredAtUtc) {
  /// <summary>Unweighted mean across all scored dimensions -- a summary convenience, not a
  /// replacement for looking at individual dimensions (a low ErrorHandling score can hide behind a
  /// high mean if OverallBehavior/ControlFlow dominate).</summary>
  public double MeanScore => Dimensions.Count == 0 ? 0 : Dimensions.Average(d => d.Score);
}

/// <summary>
/// Aggregates <see cref="PseudocodeEvaluation"/> results across arms for one benchmark corpus,
/// reporting each arm's mean score per dimension so an "enhanced package vs. assembly-only" (or
/// "vs. Ghidra") comparison is a real, inspectable delta rather than a single blended claim.
/// </summary>
public static class PseudocodeAccuracyReport {
  public sealed record ArmDimensionSummary(PseudocodeEvidenceArm Arm, AccuracyDimension Dimension,
                                           int EvaluationCount, double MeanScore);

  /// <summary>
  /// Summarizes a set of completed evaluations. Returns an empty list (not a fabricated summary)
  /// for any (arm, dimension) pair with zero evaluations -- absence of evidence is itself evidence,
  /// consistent with the rest of this plan.
  /// </summary>
  public static IReadOnlyList<ArmDimensionSummary> Summarize(IReadOnlyList<PseudocodeEvaluation> evaluations) {
    return evaluations
      .SelectMany(e => e.Dimensions.Select(d => (e.Arm, d.Dimension, d.Score)))
      .GroupBy(x => (x.Arm, x.Dimension))
      .Select(g => new ArmDimensionSummary(g.Key.Arm, g.Key.Dimension, g.Count(), g.Average(x => x.Score)))
      .OrderBy(s => s.Arm).ThenBy(s => s.Dimension)
      .ToList();
  }
}
