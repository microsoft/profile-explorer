// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling.Tests.Unit;

[TestClass]
[TestCategory("Unit")]
public class SymbolServerClientTests {
  private sealed class LegacyLocator : ISymbolFileLocator {
    public List<string> RequestedNames { get; } = new();
    public CancellationToken LastToken { get; private set; }
    public Task<string?> FindSymbolFileAsync(string name, Guid id, int age, CancellationToken ct = default) =>
      Task.FromResult<string?>(null);
    public Task<string?> FindBinaryFileAsync(string name, int stamp, long size, CancellationToken ct = default) {
      RequestedNames.Add(name);
      LastToken = ct;
      return Task.FromResult(name == "original.dll" ? @"C:\cache\original.dll" : null);
    }
  }

  [TestMethod]
  public async Task FindBinaryFileAsync_LegacyContractAndAlternateName_PreserveCancellationToken() {
    var locator = new LegacyLocator();
    ISymbolFileLocator contract = locator;
    using var cts = new CancellationTokenSource();
    Assert.IsNull(await contract.FindBinaryFileAsync("reported.dll", 1, 2, cts.Token));
    Assert.AreEqual(cts.Token, locator.LastToken);
    Assert.AreEqual(@"C:\cache\original.dll",
      await contract.FindBinaryFileAsync("reported.dll", 1, 2, "original.dll", cts.Token));
    CollectionAssert.AreEqual(new[] { "reported.dll", "reported.dll", "original.dll" }, locator.RequestedNames);
    Assert.AreEqual(cts.Token, locator.LastToken);

    using var client = new SymbolServerClient(new ProfilerOptions { SymbolPaths = Array.Empty<string>() });
    Assert.IsNull(await client.FindBinaryFileAsync("not-found.dll", 1, 2, cts.Token));
  }

  [TestMethod]
  public void ParseSymbolPath_SrvCacheServer() {
    var options = new ProfilerOptions {
      SymbolPaths = ["srv*C:\\Symbols*https://symbolserver.example.com"]
    };

    using var client = new SymbolServerClient(options);

    // Should have at least a local + remote server.
    Assert.IsTrue(client.Servers.Count >= 2);
    Assert.IsTrue(client.Servers.Any(s => !s.IsRemote)); // Local cache
    Assert.IsTrue(client.Servers.Any(s => s.IsRemote && s.Url.Contains("symbolserver"))); // Remote
  }

  [TestMethod]
  public void ParseSymbolPath_MultipleServers() {
    var options = new ProfilerOptions {
      SymbolPaths = [
        "srv*C:\\cache1*https://server1.com",
        "srv*C:\\cache2*https://server2.com"
      ]
    };

    using var client = new SymbolServerClient(options);

    Assert.IsTrue(client.Servers.Any(s => s.IsRemote && s.Url.Contains("server1")));
    Assert.IsTrue(client.Servers.Any(s => s.IsRemote && s.Url.Contains("server2")));
  }

  [TestMethod]
  public void ParseSymbolPath_LocalPath() {
    var options = new ProfilerOptions {
      SymbolPaths = ["C:\\LocalSymbols"]
    };

    using var client = new SymbolServerClient(options);

    Assert.IsTrue(client.Servers.Any(s => !s.IsRemote && s.Url.Contains("LocalSymbols")));
  }

  [TestMethod]
  public void ParseSymbolPath_SymwebRequiresAuth() {
    var options = new ProfilerOptions {
      SymbolPaths = ["srv*https://symweb.example.com"]
    };

    using var client = new SymbolServerClient(options);

    var remoteServer = client.Servers.First(s => s.IsRemote);
    Assert.IsTrue(remoteServer.RequiresAuth);
  }
}
