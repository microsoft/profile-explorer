// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.ETW;
using ProfileExplorer.Core.Profile.Processing;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.CoreTests;

/// <summary>
/// Verifies that samples from dynamically generated code
/// (LUA JIT, Java JIT, etc.) whose IPs don't map to any loaded module are
/// properly attributed rather than silently dropped.
///
/// Integration tests exercise the actual ETWProfileDataProvider code path.
/// Processor tests verify FunctionProfileProcessor and FunctionsForSamplesProcessor
/// handle the resulting frames correctly (these processors are not covered by
/// the existing SyntheticProfileTests in the UI test project).
/// </summary>
[TestClass]
public class ETWUnmappedFrameResolutionTests {
  private static readonly BindingFlags NonPublic =
    BindingFlags.Instance | BindingFlags.NonPublic;

  #region Integration tests — actual ETWProfileDataProvider code path

  [TestMethod]
  public async Task ProcessUnresolvedStack_UnmappedIP_CreatesAttributedFrame() {
    var (rawProfile, process, context) = CreateRawProfile(1000, 2000, "test.exe", "WorkerThread");
    rawProfile.AddImageToProcess(1000, new ProfileImage("test.exe", "test.exe",
      0x10000, 0x10000, 0x1000, 0, 0));
    rawProfile.LoadingCompleted();

    var provider = CreateProvider();
    GetProfileData(provider).AddThreads(process.Threads(rawProfile));
    InvokePreCreateUnknownModule(provider, rawProfile, 1000);

    var stack = new ProfileStack { ContextId = 1, FramePointers = new long[] { 0xAAAAAA } };
    var resolved = await InvokeProcessUnresolvedStack(provider, stack, context, rawProfile);

    Assert.AreEqual(1, resolved.FrameCount);
    var frame = resolved.StackFrames[0];
    Assert.IsFalse(frame.IsUnknown, "Unmapped IP should produce an attributed frame, not Unknown");
    Assert.IsNotNull(frame.FrameDetails.Image);
    Assert.IsNotNull(frame.FrameDetails.Function);
    StringAssert.Contains(frame.FrameDetails.Function.Name, "JIT");
  }

  [TestMethod]
  public async Task ProcessUnresolvedStack_MixedStack_BothFramesAttributed() {
    var (rawProfile, process, context) = CreateRawProfile(1001, 2001, "mixed.exe", null);
    rawProfile.AddImageToProcess(1001, new ProfileImage("mixed.exe", "mixed.exe",
      0x10000, 0x10000, 0x10000, 0, 0));
    rawProfile.LoadingCompleted();

    var provider = CreateProvider();
    GetProfileData(provider).AddThreads(process.Threads(rawProfile));
    InvokePreCreateUnknownModule(provider, rawProfile, 1001);

    // Frame 0 (leaf): unmapped. Frame 1 (root): inside mixed.exe.
    var stack = new ProfileStack { ContextId = 1, FramePointers = new long[] { 0xCCCCCC, 0x10500 } };
    var resolved = await InvokeProcessUnresolvedStack(provider, stack, context, rawProfile);

    Assert.AreEqual(2, resolved.FrameCount);
    Assert.IsFalse(resolved.StackFrames[0].IsUnknown, "Unmapped frame should not be Unknown");
    Assert.IsFalse(resolved.StackFrames[1].IsUnknown, "Mapped frame should not be Unknown");
    Assert.AreEqual("mixed.exe", resolved.StackFrames[1].FrameDetails.Image.ModuleName);
  }

  [TestMethod]
  public async Task ProcessUnresolvedStack_ConsecutiveUnmappedIPs_CollapsedToOneFrame() {
    var (rawProfile, process, context) = CreateRawProfile(1002, 2002, "collapse.exe", null);
    rawProfile.AddImageToProcess(1002, new ProfileImage("collapse.exe", "collapse.exe",
      0x90000, 0x90000, 0x10000, 0, 0));
    rawProfile.LoadingCompleted();

    var provider = CreateProvider();
    GetProfileData(provider).AddThreads(process.Threads(rawProfile));
    InvokePreCreateUnknownModule(provider, rawProfile, 1002);

    // 3 consecutive unmapped IPs followed by a known frame.
    var stack = new ProfileStack { ContextId = 1, FramePointers = new long[] { 0xBB0001, 0xBB0002, 0xBB0003, 0x90500 } };
    var resolved = await InvokeProcessUnresolvedStack(provider, stack, context, rawProfile);

    // Consecutive unmapped frames should be collapsed to 1 JIT frame + 1 known frame = 2 total
    Assert.AreEqual(2, resolved.FrameCount,
      "3 consecutive unmapped IPs should collapse to 1 JIT frame");
    StringAssert.Contains(resolved.StackFrames[0].FrameDetails.Function.Name, "JIT");
    Assert.AreEqual("collapse.exe", resolved.StackFrames[1].FrameDetails.Image.ModuleName);
  }

  [TestMethod]
  public async Task ProcessUnresolvedStack_KernelUnmappedIP_RoutedToUnknownNotJit() {
    var (rawProfile, process, context) = CreateRawProfile(1003, 2003, "kernel.exe", null);
    rawProfile.LoadingCompleted();

    var provider = CreateProvider();
    GetProfileData(provider).AddThreads(process.Threads(rawProfile));
    InvokePreCreateUnknownModule(provider, rawProfile, 1003);

    // Kernel-range IP that doesn't map to any module.
    var stack = new ProfileStack { ContextId = 1, FramePointers = new long[] { unchecked((long)0xFFFFF80012340000UL) } };
    var resolved = await InvokeProcessUnresolvedStack(provider, stack, context, rawProfile);

    Assert.AreEqual(1, resolved.FrameCount);
    Assert.IsTrue(resolved.StackFrames[0].IsUnknown,
      "Unmapped kernel IP should be Unknown, not attributed as JIT");
  }

  [TestMethod]
  public async Task ProcessUnresolvedStack_NoPreCreate_FallsBackToUnknown() {
    var (rawProfile, process, context) = CreateRawProfile(1004, 2004, "fallback.exe", null);
    rawProfile.LoadingCompleted();

    var provider = CreateProvider();
    GetProfileData(provider).AddThreads(process.Threads(rawProfile));
    // Deliberately NOT calling InvokePreCreateUnknownModule.

    var stack = new ProfileStack { ContextId = 1, FramePointers = new long[] { 0xDD0001 } };
    var resolved = await InvokeProcessUnresolvedStack(provider, stack, context, rawProfile);

    Assert.AreEqual(1, resolved.FrameCount);
    Assert.IsTrue(resolved.StackFrames[0].IsUnknown,
      "Without pre-created module, unmapped IP should fall back to Unknown");
  }

  [TestMethod]
  public void PreCreateUnknownModule_ProducesConsistentState() {
    var rawProfile = new RawProfileData("test.etl");
    rawProfile.TraceInfo.PointerSize = 8;
    rawProfile.GetOrCreateProcess(3000);
    rawProfile.LoadingCompleted();

    var provider = CreateProvider();
    InvokePreCreateUnknownModule(provider, rawProfile, 3000);

    var getModule = typeof(ETWProfileDataProvider).GetMethod("GetUnknownModule", NonPublic);
    var state = getModule.Invoke(provider, new object[] { 3000 });
    Assert.IsNotNull(state, "Pre-created module should be retrievable");
  }

  [TestMethod]
  public void GetOrCreateThreadFunction_PerThreadIsolation() {
    var rawProfile = new RawProfileData("test.etl");
    rawProfile.TraceInfo.PointerSize = 8;
    rawProfile.GetOrCreateProcess(3001);
    rawProfile.LoadingCompleted();

    var provider = CreateProvider();
    var profileData = GetProfileData(provider);
    for (int t = 0; t < 5; t++)
      profileData.Threads[4000 + t] = new ProfileThread(4000 + t, 3001, $"Worker{t}");

    InvokePreCreateUnknownModule(provider, rawProfile, 3001);
    var getModule = typeof(ETWProfileDataProvider).GetMethod("GetUnknownModule", NonPublic);
    var unknownState = getModule.Invoke(provider, new object[] { 3001 });
    var getFunc = unknownState.GetType().GetMethod("GetOrCreateThreadFunction");

    var results = new ConcurrentBag<(int Tid, string Name)>();
    var tasks = new List<Task>();
    for (int round = 0; round < 10; round++)
      for (int t = 0; t < 5; t++) {
        int tid = 4000 + t;
        tasks.Add(Task.Run(() => {
          var r = ((IRTextFunction, FunctionDebugInfo))getFunc.Invoke(
            unknownState, new object[] { tid });
          results.Add((tid, r.Item1.Name));
        }));
      }
    Task.WaitAll(tasks.ToArray());

    Assert.AreEqual(5, results.Select(r => r.Name).Distinct().Count(),
      "Each thread should get a distinct function");
    foreach (var g in results.GroupBy(r => r.Tid))
      Assert.AreEqual(1, g.Select(r => r.Name).Distinct().Count(),
        $"Thread {g.Key} should always return the same function");
  }

  #endregion

  #region Processor tests — FunctionProfileProcessor / FunctionsForSamplesProcessor

  [TestMethod]
  public void FunctionProfileProcessor_ExclusiveWeightGoesToLeafNotCaller() {
    // Core bug: before the fix, Unknown leaf frames were skipped and
    // ExclusiveWeight was mis-attributed to the deepest known caller.
    var profileData = new ProfileData();
    var unknownImage = CreateUnknownModuleImage();
    profileData.Modules[RealImage.Id] = RealImage;
    profileData.Modules[unknownImage.Id] = unknownImage;

    var mainFunc = new IRTextFunction("main");
    var mainInfo = new FunctionDebugInfo("main", 0x100, 64);
    var unknownFunc = new IRTextFunction("[JIT Thread 500]");
    var unknownInfo = new FunctionDebugInfo("[JIT Thread 500]", 500, 1);

    for (int i = 0; i < 2; i++) {
      var stack = new ProfileStack(contextId: 1, framePtrs: new long[2]);
      var resolved = new ResolvedProfileStack(2, new ProfileContext(100, 500, 0));
      resolved.AddFrame(unknownFunc, 0x50000 + i, unknownInfo.RVA, 0,
        new ResolvedProfileStackFrameKey(unknownInfo, unknownImage, false), stack, 8);
      resolved.AddFrame(mainFunc, 0x51100 + i, mainInfo.RVA, 1,
        new ResolvedProfileStackFrameKey(mainInfo, RealImage, false), stack, 8);
      profileData.Samples.Add((
        new ProfileSample(0x50000 + i, TimeSpan.FromMilliseconds(i * 10),
          TimeSpan.FromMilliseconds(10), false, 0), resolved));
    }
    profileData.ComputeThreadSampleRanges();

    var result = FunctionProfileProcessor.Compute(profileData, new ProfileSampleFilter());

    Assert.AreEqual(TimeSpan.FromMilliseconds(20),
      result.FunctionProfiles[unknownFunc].ExclusiveWeight,
      "Leaf (JIT) should get exclusive weight");
    Assert.AreEqual(TimeSpan.Zero,
      result.FunctionProfiles[mainFunc].ExclusiveWeight,
      "Caller (main) should NOT get exclusive weight — it's not the leaf");
    Assert.AreEqual(TimeSpan.FromMilliseconds(20),
      result.FunctionProfiles[mainFunc].Weight,
      "Caller (main) should get inclusive weight");
  }

  [TestMethod]
  public void FunctionProfileProcessor_AllUnmappedStack_WeightPreserved() {
    var profileData = new ProfileData();
    var unknownImage = CreateUnknownModuleImage();
    profileData.Modules[unknownImage.Id] = unknownImage;

    var unknownFunc = new IRTextFunction("[JIT Thread 600]");
    var unknownInfo = new FunctionDebugInfo("[JIT Thread 600]", 600, 1);

    var stack = new ProfileStack(contextId: 1, framePtrs: new long[1]);
    var resolved = new ResolvedProfileStack(1, new ProfileContext(100, 600, 0));
    resolved.AddFrame(unknownFunc, 0xCA01, unknownInfo.RVA, 0,
      new ResolvedProfileStackFrameKey(unknownInfo, unknownImage, false), stack, 8);
    profileData.Samples.Add((
      new ProfileSample(0xCA01, TimeSpan.Zero, TimeSpan.FromMilliseconds(5), false, 0),
      resolved));
    profileData.ComputeThreadSampleRanges();

    var result = FunctionProfileProcessor.Compute(profileData, new ProfileSampleFilter());

    Assert.AreEqual(TimeSpan.FromMilliseconds(5), result.ProfileWeight,
      "Profile weight must include unmapped-code-only samples");
    Assert.AreEqual(TimeSpan.FromMilliseconds(5),
      result.FunctionProfiles[unknownFunc].ExclusiveWeight);
  }

  [TestMethod]
  public void FunctionsForSamplesProcessor_IncludesUnmappedCodeFunctions() {
    var profileData = new ProfileData();
    var unknownImage = CreateUnknownModuleImage();
    profileData.Modules[unknownImage.Id] = unknownImage;

    var unknownFunc = new IRTextFunction("[JIT Thread 800]");
    var unknownInfo = new FunctionDebugInfo("[JIT Thread 800]", 800, 1);

    var stack = new ProfileStack(contextId: 1, framePtrs: new long[1]);
    var resolved = new ResolvedProfileStack(1, new ProfileContext(100, 800, 0));
    resolved.AddFrame(unknownFunc, 0xBA01, unknownInfo.RVA, 0,
      new ResolvedProfileStackFrameKey(unknownInfo, unknownImage, false), stack, 8);
    profileData.Samples.Add((
      new ProfileSample(0xBA01, TimeSpan.Zero, TimeSpan.FromMilliseconds(1), false, 0),
      resolved));
    profileData.ComputeThreadSampleRanges();

    Assert.IsTrue(
      FunctionsForSamplesProcessor.Compute(new ProfileSampleFilter(), profileData).Contains(unknownFunc),
      "Unmapped code function must appear in the function set");
  }

  #endregion

  #region Helpers

  private static readonly ProfileImage RealImage =
    new("app.exe", "app.exe", 0x1000, 0x1000, 0x100000, 0, 0xABCDEF);

  private static ProfileImage CreateUnknownModuleImage() =>
    new("[Unknown Module]", "[Unknown Module]", 0, 0, 0, 0, 0) { Id = 9999 };

  private static (RawProfileData, ProfileProcess, ProfileContext) CreateRawProfile(
    int processId, int threadId, string processName, string threadName) {
    var rawProfile = new RawProfileData("synthetic.etl");
    rawProfile.TraceInfo.PointerSize = 8;
    var process = rawProfile.GetOrCreateProcess(processId);
    process.Name = processName;
    process.ImageFileName = processName;
    rawProfile.AddThreadToProcess(processId, new ProfileThread(threadId, processId, threadName));
    var context = new ProfileContext(processId, threadId, 0);
    typeof(RawProfileData).GetMethod("AddContext", NonPublic)!
      .Invoke(rawProfile, new object[] { context });
    return (rawProfile, process, context);
  }

  private static ETWProfileDataProvider CreateProvider() {
    var provider = new ETWProfileDataProvider();
    SetField(provider, "report_", new ProfileDataReport());
    SetField(provider, "options_", new ProfileDataProviderOptions());
    SetField(provider, "compilerInfoProvider_", new ASMCompilerInfoProvider(IRMode.x86_64));
    return provider;
  }

  private static ProfileData GetProfileData(ETWProfileDataProvider provider) =>
    (ProfileData)typeof(ETWProfileDataProvider).GetField("profileData_", NonPublic)!.GetValue(provider)!;

  private static async Task<ResolvedProfileStack> InvokeProcessUnresolvedStack(
    ETWProfileDataProvider provider, ProfileStack stack,
    ProfileContext context, RawProfileData rawProfile) =>
    await (Task<ResolvedProfileStack>)typeof(ETWProfileDataProvider)
      .GetMethod("ProcessUnresolvedStackAsync", NonPublic)!
      .Invoke(provider, new object[] { stack, context, rawProfile, new SymbolFileSourceSettings() })!;

  private static void InvokePreCreateUnknownModule(ETWProfileDataProvider provider, RawProfileData rawProfile, int processId) =>
    typeof(ETWProfileDataProvider).GetMethod("PreCreateUnknownModule", NonPublic)!
      .Invoke(provider, new object[] { rawProfile, processId });

  private static void SetField(object obj, string name, object value) =>
    typeof(ETWProfileDataProvider).GetField(name, NonPublic)!.SetValue(obj, value);

  #endregion
}
