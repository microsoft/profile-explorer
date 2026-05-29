// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Profiling;

namespace ProfileExplorer.Profiling.Tests.Unit;

[TestClass]
[TestCategory("Unit")]
public class ManagedMethodResolverTests {
  private class TestManagedMethod : IManagedMethodMapping {
    public int ProcessId { get; init; }
    public string MethodName { get; init; } = "";
    public long NativeStartAddress { get; init; }
    public int NativeSize { get; init; }
    public int MethodToken { get; init; }
    public string? ModuleName { get; init; }
    public Guid ManagedPdbGuid { get; init; }
    public int ManagedPdbAge { get; init; }
    public string? ManagedPdbName { get; init; }
    public IReadOnlyList<ILToNativeMapping>? ILMappings { get; init; }
  }

  [TestMethod]
  public void RegisterMethod_FindByIP() {
    var resolver = new ManagedMethodResolver();
    resolver.AddMethod(new TestManagedMethod {
      ProcessId = 1,
      MethodName = "System.String.Concat",
      NativeStartAddress = 0x7FF00000,
      NativeSize = 0x100,
      ModuleName = "System.Private.CoreLib"
    });

    var result = resolver.FindMethod(0x7FF00050);

    Assert.IsNotNull(result);
    Assert.AreEqual("System.String.Concat", result.MethodName);
  }

  [TestMethod]
  public void IPOutsideRange_ReturnsNull() {
    var resolver = new ManagedMethodResolver();
    resolver.AddMethod(new TestManagedMethod {
      ProcessId = 1,
      MethodName = "Foo",
      NativeStartAddress = 0x1000,
      NativeSize = 0x100
    });

    Assert.IsNull(resolver.FindMethod(0x2000));
    Assert.IsNull(resolver.FindMethod(0x0500));
  }

  [TestMethod]
  public void MultipleMethodsRegistered_FindCorrectOne() {
    var resolver = new ManagedMethodResolver();
    resolver.AddMethod(new TestManagedMethod {
      MethodName = "MethodA", NativeStartAddress = 0x1000, NativeSize = 0x100
    });
    resolver.AddMethod(new TestManagedMethod {
      MethodName = "MethodB", NativeStartAddress = 0x2000, NativeSize = 0x200
    });
    resolver.AddMethod(new TestManagedMethod {
      MethodName = "MethodC", NativeStartAddress = 0x3000, NativeSize = 0x50
    });

    Assert.AreEqual("MethodA", resolver.FindMethod(0x1050)?.MethodName);
    Assert.AreEqual("MethodB", resolver.FindMethod(0x2100)?.MethodName);
    Assert.AreEqual("MethodC", resolver.FindMethod(0x3020)?.MethodName);
  }

  [TestMethod]
  public void EmptyResolver_ReturnsNull() {
    var resolver = new ManagedMethodResolver();
    Assert.IsNull(resolver.FindMethod(0x1000));
  }

  [TestMethod]
  public void ExactStartAddress_FindsMethod() {
    var resolver = new ManagedMethodResolver();
    resolver.AddMethod(new TestManagedMethod {
      MethodName = "Foo", NativeStartAddress = 0x5000, NativeSize = 0x80
    });

    Assert.AreEqual("Foo", resolver.FindMethod(0x5000)?.MethodName);
  }
}
