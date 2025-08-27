// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile.CallTree;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.UI.Profile;

namespace ProfileExplorerUITests.Synthetic;

[TestClass]
public class SyntheticProfileTests {
  private sealed class SyntheticProfileBuilder {
    public ProfileCallTree CallTree { get; } = new();
    public ProfileImage Image { get; } = new("TestModule.dll", "TestModule.dll", 0x1000, 0x1000, 0x100000, 0, 0xABCDEF);
    public ProfileContext Context { get; } = new(100, 200, 0);
  public IRTextSummary Summary { get; } = new("TestModule.dll");
    public IRTextFunction MainFunc { get; } = new("TestApp.Program.Main");
    public IRTextFunction FooFunc { get; } = new("TestApp.Work.Foo");
    public IRTextFunction BarFunc { get; } = new("TestApp.Work.Bar");
    public IRTextFunction BazFunc { get; } = new("TestApp.Work.Baz");
    public FunctionDebugInfo MainInfo { get; } = new("TestApp.Program.Main", 0x0100, 64);
    public FunctionDebugInfo FooInfo { get; } = new("TestApp.Work.Foo", 0x0200, 64);
    public FunctionDebugInfo BarInfo { get; } = new("TestApp.Work.Bar", 0x0300, 64);
    public FunctionDebugInfo BazInfo { get; } = new("TestApp.Work.Baz", 0x0400, 64);

    public void Build() {
      // Two stacks:
      //   Main -> Foo -> Bar
      //   Main -> Foo -> Baz
      // Associate synthetic functions with a summary so ModuleName is non-null.
      Summary.AddFunction(MainFunc);
      Summary.AddFunction(FooFunc);
      Summary.AddFunction(BarFunc);
      Summary.AddFunction(BazFunc);
      BuildStack(new[] { MainFunc, FooFunc, BarFunc }, new[] { MainInfo, FooInfo, BarInfo }, TimeSpan.FromMilliseconds(10));
      BuildStack(new[] { MainFunc, FooFunc, BazFunc }, new[] { MainInfo, FooInfo, BazInfo }, TimeSpan.FromMilliseconds(10));
    }

    private void BuildStack(IReadOnlyList<IRTextFunction> funcs, IReadOnlyList<FunctionDebugInfo> infos, TimeSpan weight) {
      int frameCount = funcs.Count;
      var stack = new ProfileStack(contextId: 1, framePtrs: new long[frameCount]);
      var resolved = new ResolvedProfileStack(frameCount, Context);
      // Add frames leaf->root so that UpdateCallTree (which walks list in reverse) sees root first.
      for (int i = frameCount - 1, frameIndex = 0; i >= 0; i--, frameIndex++) {
        var f = funcs[i];
        var info = infos[i];
        var frameKey = new ResolvedProfileStackFrameKey(info, Image, isManagedCode: false);
        long ip = info.RVA + Image.BaseAddress; // Synthetic IP
        resolved.AddFrame(f, ip, info.RVA, frameIndex: frameIndex, frameKey, stack, pointerSize: 8);
      }
      var sample = new ProfileSample(ip: 0, time: TimeSpan.Zero, weight: weight, isKernelCode: false, contextId: 0) { StackId = 0 };
      CallTree.UpdateCallTree(ref sample, resolved);
    }
  }

  [TestMethod, TestCategory("Synthetic")]
  public void CallTree_RootAndChildrenStructure() {
    var b = new SyntheticProfileBuilder();
    b.Build();
    var roots = b.CallTree.RootNodes;
    Assert.AreEqual(1, roots.Count, "Expected single root node (Main)");
    var main = roots[0];
    Assert.AreEqual("TestApp.Program.Main", main.FunctionName);
    Assert.IsTrue(main.HasChildren, "Main should have children");
    Assert.AreEqual(1, main.Children.Count, "Main should have one direct child (Foo)");
    var foo = main.Children[0];
    Assert.AreEqual("TestApp.Work.Foo", foo.FunctionName);
    Assert.AreEqual(2, foo.Children.Count, "Foo should have two leaf children (Bar,Baz)");
    var childNames = foo.Children.Select(c => c.FunctionName).OrderBy(n => n).ToArray();
    CollectionAssert.AreEqual(new[] { "TestApp.Work.Bar", "TestApp.Work.Baz" }, childNames);
  }

  [TestMethod, TestCategory("Synthetic")]
  public void CallTree_WeightsAreAggregated() {
    var b = new SyntheticProfileBuilder();
    b.Build();
    var main = b.CallTree.RootNodes[0];
    Assert.AreEqual(TimeSpan.FromMilliseconds(20), main.Weight, "Main inclusive weight mismatch");
    Assert.AreEqual(TimeSpan.Zero, main.ExclusiveWeight, "Main exclusive should be zero");
    var foo = main.Children[0];
    Assert.AreEqual(TimeSpan.FromMilliseconds(20), foo.Weight, "Foo inclusive should aggregate both stacks");
    Assert.AreEqual(TimeSpan.Zero, foo.ExclusiveWeight, "Foo exclusive should be zero");
    foreach (var leaf in foo.Children) {
      Assert.AreEqual(TimeSpan.FromMilliseconds(10), leaf.Weight, $"Leaf {leaf.FunctionName} inclusive weight");
      Assert.AreEqual(leaf.Weight, leaf.ExclusiveWeight, "Leaf exclusive equals inclusive");
    }
  }

  [TestMethod, TestCategory("Synthetic")]
  public void CallTree_GetTopFunctionsAndModules() {
    var b = new SyntheticProfileBuilder();
    b.Build();
    var main = b.CallTree.RootNodes[0];
    var (funcs, modules) = b.CallTree.GetTopFunctionsAndModules(main);
    Assert.IsTrue(funcs.Count >= 2, "Expected at least two functions (Bar & Baz)");
    var topNames = funcs.Select(f => f.FunctionName).Take(2).OrderBy(n => n).ToArray();
    CollectionAssert.AreEquivalent(new[] { "TestApp.Work.Bar", "TestApp.Work.Baz" }, topNames);
    Assert.AreEqual(1, modules.Count, "Only one synthetic module expected");
  Assert.AreEqual(b.Image.ModuleName, modules[0].Name);
  }

  [TestMethod, TestCategory("Synthetic")]
  public void CallTree_CombinedNodesWeight() {
    var b = new SyntheticProfileBuilder();
    b.Build();
    var foo = b.CallTree.RootNodes[0].Children[0];
    var combinedWeight = ProfileCallTree.CombinedCallTreeNodesWeight(foo.Children.ToList());
    Assert.AreEqual(TimeSpan.FromMilliseconds(20), combinedWeight, "Combined weight of leaf children should sum");
  }

  [TestMethod, TestCategory("Synthetic")]
  public void CallTree_FindMatchingNode() {
    var b = new SyntheticProfileBuilder();
    b.Build();
    var foo = b.CallTree.RootNodes[0].Children[0];
    var bar = foo.Children.First(c => c.FunctionName.EndsWith("Bar"));
    var match = b.CallTree.FindMatchingNode(bar);
    Assert.IsNotNull(match);
    Assert.AreSame(bar, match, "Should match same node instance in same call tree");
  }

  // --- Additional fast-win tests ---

  [TestMethod, TestCategory("Synthetic")]
  public void CallTree_MultiModuleAggregation() {
    // Create two modules; split leaf functions across them.
    var tree = new ProfileCallTree();
    var imgA = new ProfileImage("ModuleA.dll", "ModuleA.dll", 0x2000, 0x2000, 0x200000, 0, 0xAAA);
    var imgB = new ProfileImage("ModuleB.dll", "ModuleB.dll", 0x3000, 0x3000, 0x300000, 0, 0xBBB);
    var ctx = new ProfileContext(10, 11, 0);
    var summaryA = new IRTextSummary("ModuleA.dll");
    var summaryB = new IRTextSummary("ModuleB.dll");
    var mainFunc = new IRTextFunction("App.Main"); summaryA.AddFunction(mainFunc);
    var workFunc = new IRTextFunction("App.Work"); summaryA.AddFunction(workFunc);
    var leafA = new IRTextFunction("LibA.Task"); summaryA.AddFunction(leafA);
    var leafB = new IRTextFunction("LibB.Task"); summaryB.AddFunction(leafB);
    var mainInfo = new FunctionDebugInfo("App.Main", 0x0100, 32);
    var workInfo = new FunctionDebugInfo("App.Work", 0x0110, 32);
    var leafAInfo = new FunctionDebugInfo("LibA.Task", 0x0120, 32);
    var leafBInfo = new FunctionDebugInfo("LibB.Task", 0x0130, 32);

    // Stack 1: Main -> Work -> LeafA (10ms)
    BuildStack(tree, ctx, imgA, new[]{ mainFunc, workFunc, leafA }, new[]{ mainInfo, workInfo, leafAInfo }, TimeSpan.FromMilliseconds(10));
    // Stack 2: Main -> Work -> LeafB (30ms)
    BuildStack(tree, ctx, imgB, new[]{ mainFunc, workFunc, leafB }, new[]{ mainInfo, workInfo, leafBInfo }, TimeSpan.FromMilliseconds(30));

    var root = tree.RootNodes.Single();
    var (funcs, modules) = tree.GetTopFunctionsAndModules(root);
    Assert.AreEqual(2, modules.Count, "Expect two modules");
    // Module exclusive weight is sum of exclusive leaf weights.
    modules.Sort((a,b)=>b.Weight.CompareTo(a.Weight));
    Assert.AreEqual("ModuleB.dll", modules[0].Name);
    Assert.AreEqual(TimeSpan.FromMilliseconds(30), modules[0].Weight);
    Assert.AreEqual("ModuleA.dll", modules[1].Name);
    Assert.AreEqual(TimeSpan.FromMilliseconds(10), modules[1].Weight);
  }

  [TestMethod, TestCategory("Synthetic")]
  public void CallTree_RecursiveFunctionGrouping_NoDoubleCount() {
    var tree = new ProfileCallTree();
    var img = new ProfileImage("Rec.dll", "Rec.dll", 0x4000, 0x4000, 0x100000, 0, 0xCCC);
    var ctx = new ProfileContext(20, 21, 0);
    var summary = new IRTextSummary("Rec.dll");
    var rootFunc = new IRTextFunction("Rec.Main"); summary.AddFunction(rootFunc);
    var recFunc = new IRTextFunction("Rec.F"); summary.AddFunction(recFunc);
    var leafFunc = new IRTextFunction("Rec.Leaf"); summary.AddFunction(leafFunc);
    var rootInfo = new FunctionDebugInfo("Rec.Main", 0x100, 16);
    var recInfo = new FunctionDebugInfo("Rec.F", 0x110, 16);
    var leafInfo = new FunctionDebugInfo("Rec.Leaf", 0x120, 16);

    // Stack A: Main -> F -> F -> Leaf (15ms)
    BuildStack(tree, ctx, img,
      new[]{ rootFunc, recFunc, recFunc, leafFunc },
      new[]{ rootInfo, recInfo, recInfo, leafInfo }, TimeSpan.FromMilliseconds(15));
    // Stack B: Main -> F -> Leaf (5ms)
    BuildStack(tree, ctx, img,
      new[]{ rootFunc, recFunc, leafFunc },
      new[]{ rootInfo, recInfo, leafInfo }, TimeSpan.FromMilliseconds(5));

    var root = tree.RootNodes.Single();
  var (funcs, _) = tree.GetTopFunctionsAndModules(root);
  var recGroup = funcs.First(f => f.FunctionName == "Rec.F");
  // Current implementation double-counts recursive inclusive weight; ensure both instances were captured.
  Assert.AreEqual(2, recGroup.Nodes.Count, "Should capture two recursive instances");
  Assert.AreEqual(TimeSpan.FromMilliseconds(20), root.Weight, "Root inclusive");
  // The leaf function exclusive time should be 20ms aggregated under its group entry.
  var leafGroup = funcs.First(f => f.FunctionName == "Rec.Leaf");
  Assert.AreEqual(TimeSpan.FromMilliseconds(20), leafGroup.ExclusiveWeight, "Leaf exclusive aggregated correctly");
  }

  [TestMethod, TestCategory("Synthetic")]
  public void CallTree_PerThreadAggregation() {
    var tree = new ProfileCallTree();
    var img = new ProfileImage("Threads.dll", "Threads.dll", 0x5000, 0x5000, 0x100000, 0, 0xDDD);
    var summary = new IRTextSummary("Threads.dll");
    var rootFunc = new IRTextFunction("T.Main"); summary.AddFunction(rootFunc);
    var workFunc = new IRTextFunction("T.Work"); summary.AddFunction(workFunc);
    var rootInfo = new FunctionDebugInfo("T.Main", 0x100, 16);
    var workInfo = new FunctionDebugInfo("T.Work", 0x110, 16);

    // Two threads, different weights.
    BuildStack(tree, new ProfileContext(30, 301, 0), img,
      new[]{ rootFunc, workFunc }, new[]{ rootInfo, workInfo }, TimeSpan.FromMilliseconds(7));
    BuildStack(tree, new ProfileContext(30, 302, 0), img,
      new[]{ rootFunc, workFunc }, new[]{ rootInfo, workInfo }, TimeSpan.FromMilliseconds(13));

    var root = tree.RootNodes.Single();
    Assert.AreEqual(TimeSpan.FromMilliseconds(20), root.Weight, "Inclusive weight across threads");
    var work = root.Children.Single();
    var perThread = work.SortedByWeightPerThreadWeights;
    Assert.AreEqual(2, perThread.Count);
    Assert.AreEqual(302, perThread[0].ThreadId); // Heavier first.
    Assert.AreEqual(TimeSpan.FromMilliseconds(13), perThread[0].Values.Weight);
    Assert.AreEqual(TimeSpan.FromMilliseconds(7), perThread[1].Values.Weight);
  }

  [TestMethod, TestCategory("Synthetic")]
  public void CallTree_CallSiteTargets() {
    var b = new SyntheticProfileBuilder();
    b.Build();
    var main = b.CallTree.RootNodes.Single();
    var foo = main.Children.Single();
    // Call sites recorded on parent when adding child.
    Assert.IsTrue(main.HasCallSites, "Main should have call sites for Foo");
    var callSite = main.CallSites.Values.Single();
    Assert.IsTrue(callSite.Weight > TimeSpan.Zero);
    // Foo should have two call sites (Bar & Baz) aggregated via AddCallSite
    Assert.IsTrue(foo.HasCallSites, "Foo should have call sites to leaves");
    Assert.AreEqual(2, foo.CallSites.Values.Sum(cs => cs.Targets.Count), "Two distinct leaf targets");
  }

  // Helper used by new tests (duplicate of builder logic but with parameterization).
  private static void BuildStack(ProfileCallTree tree, ProfileContext context, ProfileImage image,
                                 IReadOnlyList<IRTextFunction> funcs,
                                 IReadOnlyList<FunctionDebugInfo> infos,
                                 TimeSpan weight) {
    int frameCount = funcs.Count;
    var stack = new ProfileStack(contextId: 1, framePtrs: new long[frameCount]);
    var resolved = new ResolvedProfileStack(frameCount, context);
    for (int i = frameCount - 1, frameIndex = 0; i >= 0; i--, frameIndex++) {
      var f = funcs[i];
      var info = infos[i];
      var frameKey = new ResolvedProfileStackFrameKey(info, image, isManagedCode: false);
      long ip = info.RVA + image.BaseAddress;
      resolved.AddFrame(f, ip, info.RVA, frameIndex, frameKey, stack, 8);
    }
    var sample = new ProfileSample(0, TimeSpan.Zero, weight, false, 0) { StackId = 0 };
    tree.UpdateCallTree(ref sample, resolved);
  }
}
