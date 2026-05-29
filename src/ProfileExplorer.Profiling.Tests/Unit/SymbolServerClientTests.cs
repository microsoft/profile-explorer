// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling.Tests.Unit;

[TestClass]
[TestCategory("Unit")]
public class SymbolServerClientTests {
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
      SymbolPaths = ["srv*https://symbolserver.example.com"]
    };

    using var client = new SymbolServerClient(options);

    var remoteServer = client.Servers.First(s => s.IsRemote);
    Assert.IsTrue(remoteServer.RequiresAuth);
  }
}
