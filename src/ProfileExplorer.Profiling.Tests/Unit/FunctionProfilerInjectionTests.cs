// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;      // FunctionDebugInfo
using ProfileExplorer.Core.Profile;      // ProfileFunctionId
using ProfileExplorer.Profiling;         // FunctionProfiler, ProfilerOptions, IProfileSample
using ProfileExplorer.Profiling.Symbols; // ISymbolFileLocator
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Unit;

/// <summary>
/// Verifies the host-symbol-injection path (Stage 3): when the host (e.g. Profile Explorer, which
/// owns symbol acquisition via TraceEvent) provides pre-read function lists via
/// <see cref="FunctionProfiler.AddResolvedFunctions"/>, the engine resolves and aggregates against
/// them with no symbol download of its own.
/// </summary>
[TestClass]
public class FunctionProfilerInjectionTests {
  /// <summary>Symbol locator that never resolves anything — proves no download path is taken.</summary>
  private sealed class NoSymbolLocator : ISymbolFileLocator {
    public Task<string?> FindSymbolFileAsync(string pdbName, Guid guid, int age, CancellationToken ct = default) =>
      Task.FromResult<string?>(null);

    public Task<string?> FindBinaryFileAsync(string binaryName, int timeDateStamp, long imageSize,
                                             CancellationToken ct = default) =>
      Task.FromResult<string?>(null);
  }

  [TestMethod]
  public void AddResolvedFunctions_ResolvesAndAggregates_WithoutSymbolDownload() {
    const string module = "app.dll";
    const long baseAddr = 0x140000000;
    const int size = 0x100000;

    var options = new ProfilerOptions {
      SymbolPaths = new[] { "srv*https://symbols.invalid" }, // only needs to be non-empty for Validate()
      IncludeManagedCode = false,
      IncludePerformanceCounters = false
    };

    using var profiler = new FunctionProfiler(options, new NoSymbolLocator());

    profiler.AddImages(SyntheticSampleBuilder.CreateImages((module, baseAddr, size)));
    profiler.AddResolvedFunctions(module, baseAddr, new List<FunctionDebugInfo> {
      new("Main", 0x1000, 0x800),
      new("Foo", 0x2000, 0x800),
      new("Bar", 0x3000, 0x800)
    });

    // One 10ms sample, stack leaf-first: Bar(@0x40) -> Foo(@0x80) -> Main(@0x100).
    long leafIp = baseAddr + 0x3000 + 0x40;
    long fooIp = baseAddr + 0x2000 + 0x80;
    long mainIp = baseAddr + 0x1000 + 0x100;
    var sample = new SyntheticSample(leafIp, TimeSpan.FromMilliseconds(10), 1, 1, module, baseAddr,
                                     new long[] { leafIp, fooIp, mainIp });
    profiler.AddSamples(new IProfileSample[] { sample });

    var report = profiler.GetReport();

    var bar = report.Functions[new ProfileFunctionId(module, "Bar")];
    var foo = report.Functions[new ProfileFunctionId(module, "Foo")];
    var main = report.Functions[new ProfileFunctionId(module, "Main")];

    // Leaf gets exclusive + inclusive; callers get inclusive; per-instruction attributed at call sites.
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), bar.ExclusiveWeight, "Bar exclusive (leaf)");
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), bar.Weight, "Bar inclusive");
    Assert.AreEqual(TimeSpan.Zero, foo.ExclusiveWeight, "Foo exclusive (caller)");
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), foo.Weight, "Foo inclusive");
    Assert.AreEqual(TimeSpan.Zero, main.ExclusiveWeight, "Main exclusive (caller)");
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), main.Weight, "Main inclusive");
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), report.TotalWeight, "total sampled weight");

    Assert.AreEqual(TimeSpan.FromMilliseconds(10), bar.InstructionWeight[0x40], "Bar leaf @0x40");
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), foo.InstructionWeight[0x80], "Foo call-site @0x80");
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), main.InstructionWeight[0x100], "Main call-site @0x100");
  }

  [TestMethod]
  public void AddResolvedFunctions_InvalidArguments_Throw() {
    const string module = "app.dll";
    const long baseAddr = 0x140000000;
    const int size = 0x100000;
    var options = new ProfilerOptions { SymbolPaths = new[] { "srv*https://symbols.invalid" } };
    using var profiler = new FunctionProfiler(options, new NoSymbolLocator());
    profiler.AddImages(SyntheticSampleBuilder.CreateImages((module, baseAddr, size)));

    // Null function list.
    Assert.ThrowsException<ArgumentNullException>(() =>
      profiler.AddResolvedFunctions(module, baseAddr, null!));
    // Empty module name.
    Assert.ThrowsException<ArgumentException>(() =>
      profiler.AddResolvedFunctions("", baseAddr, new List<FunctionDebugInfo>()));
    // Base address was never registered via AddImages.
    Assert.ThrowsException<ArgumentException>(() =>
      profiler.AddResolvedFunctions(module, 0xDEADBEEF, new List<FunctionDebugInfo>()));
    // Module name does not match the image registered at that base (wrong-module guard).
    Assert.ThrowsException<ArgumentException>(() =>
      profiler.AddResolvedFunctions("wrong.dll", baseAddr, new List<FunctionDebugInfo>()));
  }

  [TestMethod]
  public void AddResolvedFunctions_ModuleNameCaseMismatch_Throws() {
    const string module = "App.DLL";
    const long baseAddr = 0x140000000;
    const int size = 0x100000;
    var options = new ProfilerOptions {
      SymbolPaths = new[] { "srv*https://symbols.invalid" },
      IncludeManagedCode = false,
      IncludePerformanceCounters = false
    };
    using var profiler = new FunctionProfiler(options, new NoSymbolLocator());
    profiler.AddImages(SyntheticSampleBuilder.CreateImages((module, baseAddr, size)));

    // Module identity is case-sensitive (Ordinal) throughout the library: a case-only difference denotes
    // a different binary (the WinUI vs UWP xaml pair), so the wrong-module guard rejects it.
    Assert.ThrowsException<ArgumentException>(() =>
      profiler.AddResolvedFunctions("app.dll", baseAddr,
        new List<FunctionDebugInfo> { new("Foo", 0x1000, 0x800) }));
  }

  [TestMethod]
  public void AddImages_DuplicateSameImage_IsIdempotent() {
    const string module = "app.dll";
    const long baseAddr = 0x140000000;
    const int size = 0x100000;
    var options = new ProfilerOptions {
      SymbolPaths = new[] { "srv*https://symbols.invalid" },
      IncludeManagedCode = false,
      IncludePerformanceCounters = false
    };
    using var profiler = new FunctionProfiler(options, new NoSymbolLocator());

    // The same image reported twice (e.g. an ImageDCStart rundown followed by its ImageLoad) is
    // deduped, matching old Core — resolution still works normally afterward.
    profiler.AddImages(SyntheticSampleBuilder.CreateImages((module, baseAddr, size)));
    profiler.AddImages(SyntheticSampleBuilder.CreateImages((module, baseAddr, size)));

    profiler.AddResolvedFunctions(module, baseAddr,
      new List<FunctionDebugInfo> { new("Foo", 0x1000, 0x800) });
    profiler.AddSamples(new IProfileSample[] {
      new SyntheticSample(baseAddr + 0x1000 + 0x10, TimeSpan.FromMilliseconds(5), 1, 1, module, baseAddr)
    });

    var report = profiler.GetReport();
    Assert.AreEqual(TimeSpan.FromMilliseconds(5),
      report.Functions[new ProfileFunctionId(module, "Foo")].ExclusiveWeight);
  }

  [TestMethod]
  public void AddImages_SameBaseDifferentImage_ThrowsByDefault() {
    const long baseAddr = 0x140000000;
    const int size = 0x100000;
    var options = new ProfilerOptions {
      SymbolPaths = new[] { "srv*https://symbols.invalid" },
      IncludeManagedCode = false,
      IncludePerformanceCounters = false
    };
    using var profiler = new FunctionProfiler(options, new NoSymbolLocator());

    // Two DIFFERENT binaries at the same base means the caller didn't de-dupe per sampling window;
    // silently keeping one would mis-attribute the other's samples, so this throws by default.
    profiler.AddImages(SyntheticSampleBuilder.CreateImages(("first.dll", baseAddr, size)));
    Assert.ThrowsException<InvalidOperationException>(() =>
      profiler.AddImages(SyntheticSampleBuilder.CreateImages(("second.dll", baseAddr, size))));
  }

  [TestMethod]
  public void AddImages_SameBaseDifferentImage_LenientMode_LastWins() {
    const long baseAddr = 0x140000000;
    const int size = 0x100000;
    var options = new ProfilerOptions {
      SymbolPaths = new[] { "srv*https://symbols.invalid" },
      IncludeManagedCode = false,
      IncludePerformanceCounters = false,
      ThrowOnImageBaseCollision = false
    };
    using var profiler = new FunctionProfiler(options, new NoSymbolLocator());

    // Opt-in lenient mode: the base-keyed resolver can hold only one, so the latest registration wins
    // (warned, non-fatal). Attaching functions to the displaced image is then rejected by the guard.
    profiler.AddImages(SyntheticSampleBuilder.CreateImages(("first.dll", baseAddr, size)));
    profiler.AddImages(SyntheticSampleBuilder.CreateImages(("second.dll", baseAddr, size)));

    profiler.AddResolvedFunctions("second.dll", baseAddr,
      new List<FunctionDebugInfo> { new("Bar", 0x1000, 0x800) });
    Assert.ThrowsException<ArgumentException>(() =>
      profiler.AddResolvedFunctions("first.dll", baseAddr,
        new List<FunctionDebugInfo> { new("Foo", 0x1000, 0x800) }));
  }

  [TestMethod]
  public void AddSamples_WithInstancePath_FocusesOnMatchingStacks() {
    const string module = "app.dll";
    const long baseAddr = 0x140000000;
    const int size = 0x100000;

    var options = new ProfilerOptions {
      SymbolPaths = new[] { "srv*https://symbols.invalid" },
      IncludeManagedCode = false,
      IncludePerformanceCounters = false
    };

    using var profiler = new FunctionProfiler(options, new NoSymbolLocator());
    profiler.AddImages(SyntheticSampleBuilder.CreateImages((module, baseAddr, size)));
    profiler.AddResolvedFunctions(module, baseAddr, new List<FunctionDebugInfo> {
      new("Main", 0x1000, 0x800),
      new("Foo", 0x2000, 0x800),
      new("Bar", 0x3000, 0x800),
      new("Baz", 0x4000, 0x800)
    });

    long Ip(long rva, long off) => baseAddr + rva + off;

    // Stack 1 (10ms): Bar <- Foo <- Main   → matches instance path Main -> Foo.
    var s1 = new SyntheticSample(Ip(0x3000, 0x40), TimeSpan.FromMilliseconds(10), 1, 1, module, baseAddr,
      new long[] { Ip(0x3000, 0x40), Ip(0x2000, 0x80), Ip(0x1000, 0x100) });
    // Stack 2 (20ms): Baz <- Main           → does NOT match (Main then Baz, not Foo).
    var s2 = new SyntheticSample(Ip(0x4000, 0x50), TimeSpan.FromMilliseconds(20), 1, 1, module, baseAddr,
      new long[] { Ip(0x4000, 0x50), Ip(0x1000, 0x104) });

    // Root-first instance path: focus on Main -> Foo.
    var instancePath = new List<ProfileFunctionId> {
      new(module, "Main"),
      new(module, "Foo")
    };

    profiler.AddSamples(new IProfileSample[] { s1, s2 }, instancePath);
    var report = profiler.GetReport();

    Assert.AreEqual(TimeSpan.FromMilliseconds(10), report.TotalWeight, "only the matching stack is counted");
    Assert.IsTrue(report.Functions.ContainsKey(new ProfileFunctionId(module, "Bar")), "Bar (in focused path)");
    Assert.IsTrue(report.Functions.ContainsKey(new ProfileFunctionId(module, "Foo")), "Foo (in focused path)");
    Assert.IsTrue(report.Functions.ContainsKey(new ProfileFunctionId(module, "Main")), "Main (in focused path)");
    Assert.IsFalse(report.Functions.ContainsKey(new ProfileFunctionId(module, "Baz")), "Baz stack filtered out");
    Assert.AreEqual(TimeSpan.FromMilliseconds(10),
      report.Functions[new ProfileFunctionId(module, "Bar")].ExclusiveWeight, "Bar exclusive from matching stack");

    // Call tree is focused the same way: single root Main -> Foo -> Bar.
    Assert.AreEqual(1, report.CallTree.RootNodes.Count, "single focused root");
    Assert.AreEqual("Main", report.CallTree.RootNodes[0].FunctionName);
  }
}
