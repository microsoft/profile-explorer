// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Profile;
using ProfileExplorer.Core.Profile.Adapters;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.Processing; // ProfileSampleFilter
using ProfileExplorer.Profiling;            // IProfileSample, FunctionProfiler, ResolvedFrame
using ProfileExplorer.Profiling.Profiling;  // IpResolver, SampleAggregator (InternalsVisibleTo)
using ProfileExplorer.Profiling.Symbols;    // ISymbolFileLocator

namespace ProfileExplorer.CoreTests;

/// <summary>
/// Regression test for the profiling-engine deduplication: proves the library's
/// <see cref="SampleAggregator"/> (fed either through the ETW-&gt;library <see cref="RawProfileSampleAdapter"/>
/// or the resolved-stack cutover path) reproduces the hand-computed per-function profile — exclusive
/// weight, inclusive weight, and per-instruction (hot-line) attribution — across multi-function,
/// multi-thread, and recursive stacks. Originally the Stage-2 parity gate against Core's
/// FunctionProfileProcessor; now that Core delegates aggregation to the library, the golden values
/// are asserted directly.
/// </summary>
[TestClass]
public class EngineAggregationParityTests {
  private const string Module = "TestModule.dll";
  private const long Base = 0x140000000;
  private const int ModuleSize = 0x100000;

  private sealed record Fn(string Name, long Rva, uint Size);

  private static readonly Fn Main = new("Main", 0x1000, 0x800);
  private static readonly Fn Foo = new("Foo", 0x2000, 0x800);
  private static readonly Fn Bar = new("Bar", 0x3000, 0x800);
  private static readonly Fn Baz = new("Baz", 0x4000, 0x800);

  [TestMethod]
  [TestCategory("Aggregation")]
  public void LibraryAggregation_MatchesGolden_OnSyntheticStacks() {
    // Static frame-interning caches are shared across the process; reset for deterministic isolation.
    ResolvedProfileStack.ResetCaches();

    // Each stack is leaf-first: frame 0 is the sampled (leaf) instruction; the rest are callers,
    // each at its call-site (return-address) offset within the caller function.
    var stacks = new (int ThreadId, int WeightMs, (Fn Func, long Offset)[] Frames)[] {
      (200, 10, new[] { (Bar, 0x40L), (Foo, 0x80L), (Main, 0x100L) }),
      (200, 10, new[] { (Baz, 0x50L), (Foo, 0x84L), (Main, 0x100L) }),
      (201,  5, new[] { (Bar, 0x44L), (Main, 0x104L) }),
      (201,  7, new[] { (Bar, 0x40L), (Foo, 0x88L), (Foo, 0x8CL), (Main, 0x100L) }), // recursion in Foo
    };

    // Raw path: ETW samples -> RawProfileSampleAdapter -> SampleAggregator (the library's own resolver).
    AssertMatchesGolden(ComputeWithLibrary(stacks));
  }

  [TestMethod]
  [TestCategory("Aggregation")]
  public void ResolvedAggregation_MatchesGolden_OnSyntheticStacks() {
    // The library aggregates over the SAME ResolvedProfileStacks Core builds — this is exactly the
    // ETWProfileDataProvider (option B) cutover path: host resolves; library aggregates the result.
    ResolvedProfileStack.ResetCaches();

    var stacks = new (int ThreadId, int WeightMs, (Fn Func, long Offset)[] Frames)[] {
      (200, 10, new[] { (Bar, 0x40L), (Foo, 0x80L), (Main, 0x100L) }),
      (200, 10, new[] { (Baz, 0x50L), (Foo, 0x84L), (Main, 0x100L) }),
      (201,  5, new[] { (Bar, 0x44L), (Main, 0x104L) }),
      (201,  7, new[] { (Bar, 0x40L), (Foo, 0x88L), (Foo, 0x8CL), (Main, 0x100L) }),
    };

    // Resolved path: project ResolvedProfileStacks -> ResolvedFrame -> FunctionProfiler.AddResolvedSample.
    AssertMatchesGolden(ComputeWithLibraryResolved(BuildCoreInput(stacks)));
  }

  [TestMethod]
  [TestCategory("Aggregation")]
  public void ComputeProfile_ParallelMatchesSequential_OnManyStacks() {
    // Guards the parallel ComputeProfile path: many-worker aggregation + per-worker call-tree merge
    // must produce byte-for-byte the same result as a single-worker (sequential) run. ComputeProfile
    // does not mutate baseProfile, so the same input drives both runs.
    ResolvedProfileStack.ResetCaches();

    var stacks = GenerateStacks(240);
    var input = BuildCoreInput(stacks);

    var sequential = input.ComputeProfile(input, new ProfileSampleFilter(), computeCallTree: true, maxChunks: 1);
    var parallel = input.ComputeProfile(input, new ProfileSampleFilter(), computeCallTree: true, maxChunks: int.MaxValue);

    // Per-function parity: exclusive, inclusive, and per-instruction (hot-line) weights.
    CollectionAssert.AreEquivalent(sequential.FunctionProfiles.Keys.ToList(),
                                   parallel.FunctionProfiles.Keys.ToList(), "function set");

    foreach (var id in sequential.FunctionProfiles.Keys) {
      var seq = sequential.FunctionProfiles[id];
      var par = parallel.FunctionProfiles[id];
      Assert.AreEqual(seq.ExclusiveWeight, par.ExclusiveWeight, $"{id.FunctionName} exclusive");
      Assert.AreEqual(seq.Weight, par.Weight, $"{id.FunctionName} inclusive");
      CollectionAssert.AreEquivalent(seq.InstructionWeight, par.InstructionWeight,
                                     $"{id.FunctionName} per-instruction");
    }

    // Totals + call-tree parity (root set, per-root inclusive weight, and total root weight).
    Assert.AreEqual(sequential.TotalWeight, parallel.TotalWeight, "total weight");
    Assert.AreEqual(sequential.CallTree.TotalRootNodesWeight, parallel.CallTree.TotalRootNodesWeight,
                    "call-tree root weight");

    var seqRoots = sequential.CallTree.RootNodes.ToDictionary(n => n.FunctionId, n => n.Weight);
    var parRoots = parallel.CallTree.RootNodes.ToDictionary(n => n.FunctionId, n => n.Weight);
    CollectionAssert.AreEquivalent(seqRoots.Keys.ToList(), parRoots.Keys.ToList(), "call-tree root set");

    foreach (var pair in seqRoots) {
      Assert.AreEqual(pair.Value, parRoots[pair.Key], $"root {pair.Key.FunctionName} weight");
    }

    // Sanity: the total equals the summed sample weight (nothing dropped by chunk boundaries).
    var expectedTotal = TimeSpan.FromMilliseconds(stacks.Sum(s => s.WeightMs));
    Assert.AreEqual(expectedTotal, parallel.TotalWeight, "parallel total equals summed sample weight");
  }

  [TestMethod]
  [TestCategory("Aggregation")]
  public void ComputeProfile_ImageResolvedButNoFunction_AttributesLeafWeightToModuleZero() {
    // End-to-end guard for the ProfileFunctionId normalization: a frame can resolve to an image (so
    // it is NOT "unknown" and is not skipped) yet have no IRTextFunction — e.g. an address inside a
    // known module with no matching symbol. Its FunctionId normalizes to the empty "unknown" identity,
    // so ComputeProfile must key it by ProfileFunctionId.Unknown and attribute its leaf (exclusive)
    // weight to the "no module" bucket (id 0), never leaking it into the real module's weight.
    ResolvedProfileStack.ResetCaches();

    const int realModuleId = 1;
    var image = new ProfileImage(Module, Module, Base, Base, ModuleSize, timeStamp: 0, checksum: 0);
    var summary = new IRTextSummary(Module);
    var knownIr = new IRTextFunction("Known");
    summary.AddFunction(knownIr);

    var input = new ProfileData();
    input.Modules[realModuleId] = image; // real module -> non-zero id; "" (unknown) -> 0

    var context = new ProfileContext(100, 200, 0);
    var rawStack = new ProfileStack(contextId: 0, framePtrs: new long[2]);
    var resolved = new ResolvedProfileStack(2, context);

    // Leaf: resolved image but function: null -> not unknown, FunctionId normalizes to ("", "").
    resolved.AddFrame(function: null, frameIP: Base + 0x2040, frameRVA: 0x2040, frameIndex: 0,
                      new ResolvedProfileStackFrameKey(new FunctionDebugInfo("", 0x2000, 0x100), image, false),
                      rawStack, pointerSize: 8);
    // Caller: fully resolved function in the real module.
    resolved.AddFrame(knownIr, frameIP: Base + 0x1080, frameRVA: 0x1080, frameIndex: 1,
                      new ResolvedProfileStackFrameKey(new FunctionDebugInfo("Known", 0x1000, 0x100), image, false),
                      rawStack, pointerSize: 8);

    input.Samples.Add((new ProfileSample(Base + 0x2040, TimeSpan.Zero, TimeSpan.FromMilliseconds(10),
                                         isKernelCode: false, contextId: 0), resolved));
    input.ComputeThreadSampleRanges();

    var result = input.ComputeProfile(input, new ProfileSampleFilter(), computeCallTree: false);

    // The image-but-no-function leaf is keyed by the normalized unknown id (default == new("","") == Unknown)
    // and holds the leaf (exclusive) weight.
    Assert.IsTrue(result.FunctionProfiles.ContainsKey(ProfileFunctionId.Unknown),
                  "image-but-no-function leaf is keyed by the normalized unknown id");
    Assert.AreEqual(TimeSpan.FromMilliseconds(10),
                    result.FunctionProfiles[ProfileFunctionId.Unknown].ExclusiveWeight,
                    "unknown leaf holds the exclusive (leaf) weight");

    // That weight is attributed to module 0, and the real module receives none of it.
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), result.ModuleWeights.GetValueOrDefault(0),
                    "unknown-module leaf weight lands in module 0");
    Assert.AreEqual(TimeSpan.Zero, result.ModuleWeights.GetValueOrDefault(realModuleId),
                    "real module receives no exclusive leaf weight from the unknown frame");
  }

  // Deterministic synthetic stacks across several threads with varied depth, offsets, and Foo
  // recursion, sized to span multiple parallel workers.
  private static (int ThreadId, int WeightMs, (Fn Func, long Offset)[] Frames)[] GenerateStacks(int count) {
    var result = new (int ThreadId, int WeightMs, (Fn Func, long Offset)[] Frames)[count];

    for (int i = 0; i < count; i++) {
      int threadId = 200 + (i % 4);
      int weightMs = 1 + (i % 7);
      var frames = new List<(Fn, long)> {
        ((i % 2 == 0) ? Bar : Baz, 0x40 + (i % 16)) // leaf
      };

      if (i % 3 == 0) {
        frames.Add((Foo, 0x88 + (i % 4))); // extra Foo frame -> recursion when combined with the next
      }

      frames.Add((Foo, 0x80 + (i % 8)));
      frames.Add((Main, 0x100 + (i % 4))); // root
      result[i] = (threadId, weightMs, frames.ToArray());
    }

    return result;
  }

  // Projects Core's ResolvedProfileStacks (the exact cutover input) through the library's
  // resolved-aggregation path, skipping unknown frames exactly as Core's aggregation does.
  private static Dictionary<ProfileFunctionId, FunctionProfileData> ComputeWithLibraryResolved(ProfileData input) {
    var options = new ProfilerOptions {
      SymbolPaths = new[] { "srv*https://symbols.invalid" },
      IncludeManagedCode = false,
      IncludePerformanceCounters = false
    };

    using var profiler = new FunctionProfiler(options, new NoLocator());
    var frames = new List<ResolvedFrame>();

    foreach (var (sample, stack) in input.Samples) {
      frames.Clear();

      foreach (var f in stack.StackFrames) {
        if (f.IsUnknown) continue;
        var d = f.FrameDetails;
        frames.Add(new ResolvedFrame(d.FunctionId, d.DebugInfo, f.FrameRVA, d.IsKernelCode, d.IsManagedCode));
      }

      profiler.AddResolvedSample(sample.Weight, stack.Context.ThreadId, frames);
    }

    return new Dictionary<ProfileFunctionId, FunctionProfileData>(profiler.GetReport().Functions);
  }

  private sealed class NoLocator : ISymbolFileLocator {
    public Task<string?> FindSymbolFileAsync(string pdbName, Guid guid, int age, CancellationToken ct = default) =>
      Task.FromResult<string?>(null);

    public Task<string?> FindBinaryFileAsync(string binaryName, int timeDateStamp, long imageSize,
                                             CancellationToken ct = default) =>
      Task.FromResult<string?>(null);
  }

  // Hand-computed golden truth for the shared 4-stack fixture (independent of any engine).
  // Weights: S1=10 S2=10 S3=5 S4=7 (total 32ms).
  private static void AssertMatchesGolden(Dictionary<ProfileFunctionId, FunctionProfileData> result) {
    var mainId = new ProfileFunctionId(Module, "Main");
    var fooId = new ProfileFunctionId(Module, "Foo");
    var barId = new ProfileFunctionId(Module, "Bar");
    var bazId = new ProfileFunctionId(Module, "Baz");

    CollectionAssert.AreEquivalent(new[] { mainId, fooId, barId, bazId }, result.Keys.ToList(), "function set");

    Assert.AreEqual(TimeSpan.FromMilliseconds(22), result[barId].ExclusiveWeight, "Bar exclusive (leaf 10+5+7)");
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), result[bazId].ExclusiveWeight, "Baz exclusive (leaf 10)");
    Assert.AreEqual(TimeSpan.Zero, result[fooId].ExclusiveWeight, "Foo exclusive (never leaf)");
    Assert.AreEqual(TimeSpan.FromMilliseconds(27), result[fooId].Weight, "Foo inclusive (10+10+7)");
    Assert.AreEqual(TimeSpan.FromMilliseconds(32), result[mainId].Weight, "Main inclusive (on every stack)");
    Assert.AreEqual(TimeSpan.Zero, result[mainId].ExclusiveWeight, "Main exclusive (never leaf)");

    // Per-instruction (hot-line) attribution — the reconciled caller call-site behavior.
    Assert.AreEqual(TimeSpan.FromMilliseconds(17), result[barId].InstructionWeight[0x40], "Bar leaf @0x40 (S1 10 + S4 7)");
    Assert.AreEqual(TimeSpan.FromMilliseconds(27), result[mainId].InstructionWeight[0x100], "Main call-site @0x100 (S1 10 + S2 10 + S4 7)");
    Assert.AreEqual(TimeSpan.FromMilliseconds(7), result[fooId].InstructionWeight[0x88], "Foo first recursive call-site @0x88 (S4 7)");
    Assert.IsFalse(result[fooId].InstructionWeight.ContainsKey(0x8C), "Foo second recursive frame @0x8C not double-counted");
  }

  private static ProfileData BuildCoreInput(
      (int ThreadId, int WeightMs, (Fn Func, long Offset)[] Frames)[] stacks) {
    var image = new ProfileImage(Module, Module, Base, Base, ModuleSize, timeStamp: 0, checksum: 0);
    var summary = new IRTextSummary(Module);
    var irByName = new Dictionary<string, IRTextFunction>();
    var dbgByName = new Dictionary<string, FunctionDebugInfo>();

    IRTextFunction Ir(Fn f) {
      if (!irByName.TryGetValue(f.Name, out var ir)) {
        ir = new IRTextFunction(f.Name);
        summary.AddFunction(ir);
        irByName[f.Name] = ir;
      }
      return ir;
    }

    FunctionDebugInfo Dbg(Fn f) {
      if (!dbgByName.TryGetValue(f.Name, out var dbg)) {
        dbg = new FunctionDebugInfo(f.Name, f.Rva, f.Size);
        dbgByName[f.Name] = dbg;
      }
      return dbg;
    }

    var input = new ProfileData();
    const int processId = 100;

    foreach (var s in stacks) {
      var context = new ProfileContext(processId, s.ThreadId, 0);
      var rawStack = new ProfileStack(contextId: 0, framePtrs: new long[s.Frames.Length]);
      var resolved = new ResolvedProfileStack(s.Frames.Length, context);

      for (int i = 0; i < s.Frames.Length; i++) {
        var (fn, offset) = s.Frames[i];
        long frameRva = fn.Rva + offset;
        long frameIp = Base + frameRva;
        var key = new ResolvedProfileStackFrameKey(Dbg(fn), image, isManagedCode: false);
        resolved.AddFrame(Ir(fn), frameIp, frameRva, frameIndex: i, key, rawStack, pointerSize: 8);
      }

      long leafIp = Base + s.Frames[0].Func.Rva + s.Frames[0].Offset;
      var sample = new ProfileSample(leafIp, TimeSpan.Zero, TimeSpan.FromMilliseconds(s.WeightMs),
                                     isKernelCode: false, contextId: 0);
      input.Samples.Add((sample, resolved));
    }

    input.ComputeThreadSampleRanges();
    return input;
  }

  private static Dictionary<ProfileFunctionId, FunctionProfileData> ComputeWithLibrary(
      (int ThreadId, int WeightMs, (Fn Func, long Offset)[] Frames)[] stacks) {
    var ipResolver = new IpResolver();
    ipResolver.AddImage(Module, Base, ModuleSize);
    ipResolver.SetFunctions(Base, new List<FunctionDebugInfo> {
      new(Main.Name, Main.Rva, Main.Size),
      new(Foo.Name, Foo.Rva, Foo.Size),
      new(Bar.Name, Bar.Rva, Bar.Size),
      new(Baz.Name, Baz.Rva, Baz.Size)
    });

    var aggregator = new SampleAggregator(ipResolver);
    var samples = new List<IProfileSample>();

    foreach (var s in stacks) {
      long[] frames = s.Frames.Select(fr => Base + fr.Func.Rva + fr.Offset).ToArray();
      samples.Add(new RawProfileSampleAdapter(
        ip: frames[0], weight: TimeSpan.FromMilliseconds(s.WeightMs),
        processId: 100, threadId: s.ThreadId, imageName: Module, imageBaseAddress: Base, stackFrames: frames));
    }

    aggregator.AddSamples(samples);
    return aggregator.Build();
  }
}
