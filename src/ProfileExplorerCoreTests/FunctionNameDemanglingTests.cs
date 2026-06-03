// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Providers;

namespace ProfileExplorer.CoreTests;

/// <summary>
/// Tests for PDBDebugInfoProvider.DemangleFunctionName, which is used by the
/// MCP server's FindFunction to match caller-supplied human-readable names
/// (from Kusto) against the decorated MSVC names stored in the trace.
/// </summary>
[TestClass]
public class FunctionNameDemanglingTests {
  // ── Non-decorated names pass through unchanged ──────────────────────────

  [TestMethod]
  public void Demangle_PlainCName_ReturnsUnchanged() {
    Assert.AreEqual("NtWaitForSingleObject",
      PDBDebugInfoProvider.DemangleFunctionName("NtWaitForSingleObject"));
  }

  [TestMethod]
  public void Demangle_EmptyString_ReturnsEmpty() {
    Assert.AreEqual("",
      PDBDebugInfoProvider.DemangleFunctionName(""));
  }

  [TestMethod]
  public void Demangle_NullString_ReturnsNull() {
    // The method checks IsNullOrEmpty and returns the input.
    string? input = null;
    Assert.IsNull(PDBDebugInfoProvider.DemangleFunctionName(input!));
  }

  [TestMethod]
  public void Demangle_HexPlaceholder_ReturnsUnchanged() {
    // Hex addresses used when no symbols are available never start with '?'
    Assert.AreEqual("0x00007fff1234abcd",
      PDBDebugInfoProvider.DemangleFunctionName("0x00007fff1234abcd"));
  }

  // ── Decorated MSVC names → demangled ────────────────────────────────────

  [TestMethod]
  public void Demangle_MsvcDecoratedName_DefaultOptions_ReturnsFullSignature() {
    // Default options: full signature including return type, parameters, qualifiers.
    string result = PDBDebugInfoProvider.DemangleFunctionName(
      "?ProcessComposition@CComposition@@UEAAXXZ");
    // Must at least contain the class and method name.
    StringAssert.Contains(result, "CComposition");
    StringAssert.Contains(result, "ProcessComposition");
  }

  [TestMethod]
  public void Demangle_MsvcDecoratedName_OnlyName_ReturnsScopedNameOnly() {
    // OnlyName | NoReturnType | NoSpecialKeywords is the combination used by
    // FindFunction when building the DemangledFunctionLookup dictionary.
    var options = FunctionNameDemanglingOptions.OnlyName |
                  FunctionNameDemanglingOptions.NoReturnType |
                  FunctionNameDemanglingOptions.NoSpecialKeywords;

    string result = PDBDebugInfoProvider.DemangleFunctionName(
      "?ProcessComposition@CComposition@@UEAAXXZ", options);

    Assert.AreEqual("CComposition::ProcessComposition", result);
  }

  [TestMethod]
  public void Demangle_MsvcDecoratedName_OnlyName_StaticMethod() {
    var options = FunctionNameDemanglingOptions.OnlyName |
                  FunctionNameDemanglingOptions.NoReturnType |
                  FunctionNameDemanglingOptions.NoSpecialKeywords;

    string result = PDBDebugInfoProvider.DemangleFunctionName(
      "?Create@CFoo@@SAPAV1@XZ", options);

    StringAssert.Contains(result, "CFoo");
    StringAssert.Contains(result, "Create");
  }

  [TestMethod]
  public void Demangle_MsvcDecoratedName_OnlyName_GlobalFunction() {
    var options = FunctionNameDemanglingOptions.OnlyName |
                  FunctionNameDemanglingOptions.NoReturnType |
                  FunctionNameDemanglingOptions.NoSpecialKeywords;

    // Global (non-member) C++ function: ?MyFunc@@YAXXZ → MyFunc
    string result = PDBDebugInfoProvider.DemangleFunctionName(
      "?MyFunc@@YAXXZ", options);

    Assert.AreEqual("MyFunc", result);
  }

  // ── Round-trip: decorated → demangled matches what callers supply ────────

  [TestMethod]
  public void Demangle_FindFunctionOptions_MatchesKustoStyleName() {
    // Kusto stores human-readable names like "CComposition::ProcessComposition".
    // Verify that the same options used in FindFunction produce an exact match.
    var options = FunctionNameDemanglingOptions.OnlyName |
                  FunctionNameDemanglingOptions.NoReturnType |
                  FunctionNameDemanglingOptions.NoSpecialKeywords;

    string decorated = "?ProcessComposition@CComposition@@UEAAXXZ";
    string kustoName = "CComposition::ProcessComposition";

    string demangled = PDBDebugInfoProvider.DemangleFunctionName(decorated, options);
    Assert.AreEqual(kustoName, demangled,
      "Demangled name must exactly match the human-readable name Kusto provides.");
  }
}
