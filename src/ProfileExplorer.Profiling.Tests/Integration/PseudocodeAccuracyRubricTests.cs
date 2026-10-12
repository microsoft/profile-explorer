// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;

namespace ProfileExplorer.Profiling.Tests.Integration;

/// <summary>
/// Structural tests for the pseudocode accuracy rubric schema. This validates only the schema's
/// own mechanics (aggregation math, "no evaluations" honesty) -- it cannot and does not test real
/// scoring, since that requires an actual LLM-generated explanation and an expert human reviewer,
/// neither of which is available in this test suite. See <see cref="PseudocodeEvaluation"/>'s
/// remarks for why real evaluation data must come from a downstream process.
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class PseudocodeAccuracyRubricTests {
  [TestMethod]
  public void Summarize_ComputesMeanScorePerArmAndDimension() {
    var evaluations = new List<PseudocodeEvaluation> {
      new("CaseA", PseudocodeEvidenceArm.AssemblyOnly,
        new List<DimensionScore> {
          new(AccuracyDimension.ControlFlow, 2, "Missed the loop entirely."),
          new(AccuracyDimension.CallsAndApis, 1, "Hallucinated an API call.")
        }, "reviewer1", DateTime.UtcNow),
      new("CaseA", PseudocodeEvidenceArm.EnhancedAnalysisPackage,
        new List<DimensionScore> {
          new(AccuracyDimension.ControlFlow, 5, "Correct loop structure."),
          new(AccuracyDimension.CallsAndApis, 5, "Correctly identified ExAllocatePool2.")
        }, "reviewer1", DateTime.UtcNow),
      new("CaseB", PseudocodeEvidenceArm.EnhancedAnalysisPackage,
        new List<DimensionScore> {
          new(AccuracyDimension.ControlFlow, 4, null),
          new(AccuracyDimension.CallsAndApis, 5, null)
        }, "reviewer2", DateTime.UtcNow)
    };

    var summary = PseudocodeAccuracyReport.Summarize(evaluations);

    var assemblyOnlyControlFlow = summary.Single(s =>
      s.Arm == PseudocodeEvidenceArm.AssemblyOnly && s.Dimension == AccuracyDimension.ControlFlow);
    Assert.AreEqual(1, assemblyOnlyControlFlow.EvaluationCount);
    Assert.AreEqual(2.0, assemblyOnlyControlFlow.MeanScore);

    var enhancedControlFlow = summary.Single(s =>
      s.Arm == PseudocodeEvidenceArm.EnhancedAnalysisPackage && s.Dimension == AccuracyDimension.ControlFlow);
    Assert.AreEqual(2, enhancedControlFlow.EvaluationCount);
    Assert.AreEqual(4.5, enhancedControlFlow.MeanScore);

    // The core comparison this schema exists to support: enhanced package must show as a real,
    // inspectable delta over the assembly-only control arm, not a single blended claim.
    Assert.IsTrue(enhancedControlFlow.MeanScore > assemblyOnlyControlFlow.MeanScore);
  }

  [TestMethod]
  public void Summarize_NoEvaluations_ReturnsEmptyNotFabricated() {
    var summary = PseudocodeAccuracyReport.Summarize(new List<PseudocodeEvaluation>());
    Assert.AreEqual(0, summary.Count);
  }

  [TestMethod]
  public void PseudocodeEvaluation_MeanScore_NoDimensions_IsZeroNotException() {
    var evaluation = new PseudocodeEvaluation("CaseC", PseudocodeEvidenceArm.GhidraDecompiler,
      new List<DimensionScore>(), "reviewer1", DateTime.UtcNow);

    Assert.AreEqual(0.0, evaluation.MeanScore);
  }
}
