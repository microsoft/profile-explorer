// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.ETW;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorerCoreTests;

[TestClass]
[DoNotParallelize]
public class BinaryIdentityReviewTests {
  private string directory_;
  private string FixturePath => Path.Combine(AppContext.BaseDirectory, "TestData", "Binaries",
    "SignatureTestLib", "testlib.dll");

  [TestInitialize]
  public void Initialize() {
    directory_ = Path.Combine(Path.GetTempPath(), $"ProfileExplorer-identity-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory_);
    BinaryFileLocator.ClearResolvedCache();
  }

  [TestCleanup]
  public void Cleanup() {
    BinaryFileLocator.ClearResolvedCache();
    Directory.Delete(directory_, true);
  }

  private SymbolFileSourceSettings LocalSettings() => new() {
    SymbolPaths = new List<string> { directory_ + Path.DirectorySeparatorChar },
    CustomSymbolCacheDirectory = directory_,
    UseEnvironmentVarSymbolPaths = false,
    ManagedIdentityEnabled = false,
    DisableSizeProbingFallback = true
  };

  private long FunctionRva() {
    using var pe = new PEBinaryInfoProvider(FixturePath);
    Assert.IsTrue(pe.Initialize());
    return RuntimeFunctionTable.ParseAmd64(pe.GetExceptionDirectoryData().Span).First().StartRva;
  }

  [TestMethod]
  public void AlternateNameLookup_DisassemblesAndCachesUnderOriginalRequestWithoutMutation() {
    var actual = PEBinaryInfoProvider.GetBinaryFileInfo(FixturePath);
    string alternateName = "server-name.dll";
    string storeEntry = Path.Combine(directory_, alternateName, $"{actual.TimeStamp:X8}{actual.ImageSize:x}");
    Directory.CreateDirectory(storeEntry);
    File.Copy(FixturePath, Path.Combine(storeEntry, alternateName));
    var requested = actual.Clone();
    requested.ImageName = "trace-name.dll";
    requested.ImagePath = FixturePath;
    requested.OriginalFileName = alternateName;
    requested.ImageSize += 0x1000;
    var original = requested.Clone();
    int hash = requested.GetHashCode();
    var settings = LocalSettings();
    settings.SymbolPaths = new List<string> { $"srv*{directory_}*{directory_}" };

    var located = BinaryFileLocator.LocateBinaryFile(requested, settings);
    Assert.IsTrue(located.Found, located.Details);
    Assert.AreEqual(alternateName, located.BinaryFile.ImageName);
    Assert.AreEqual(actual.ImageSize, located.BinaryFile.ImageSize);
    Assert.IsFalse(located.IsApproximateMatch);
    Assert.AreEqual(hash, requested.GetHashCode());
    Assert.AreEqual(original, requested);
    settings.RejectedBinaryFiles.Add(original);
    Assert.AreSame(located, BinaryFileLocator.LocateBinaryFile(original.Clone(), settings),
      "The original ETW size/name must hit the cache before any server probes or rejection checks.");

    var result = NativeAddressDisassemblyService.ResolveByIdentity(original.Clone(), FunctionRva(),
      NativeAddressForm.ModuleRva, FrameAddressKind.InstructionPointer, settings);
    Assert.IsTrue(result.Success, result.ErrorMessage);
  }

  [TestMethod]
  public async System.Threading.Tasks.Task MissingAlternateName_DoesNotBlockLaterLookupOrPersistentRejection() {
    var actual = PEBinaryInfoProvider.GetBinaryFileInfo(FixturePath);
    string alternateName = "alternate.dll";
    string entry = Path.Combine(directory_, alternateName, $"{actual.TimeStamp:X8}{actual.ImageSize:x}");
    Directory.CreateDirectory(entry);
    File.Copy(FixturePath, Path.Combine(entry, alternateName));
    var requested = actual.Clone();
    requested.ImageName = "unknown.dll";
    requested.ImagePath = Path.Combine(directory_, requested.ImageName);
    var settings = LocalSettings();
    settings.SymbolPaths = new List<string> { $"srv*{directory_}*{directory_}" };
    settings.AllowApproximateBinaryMatch = false;

    // Suppress the directory-wide fallback so only the exact symbol-store keys are searched.
    settings.RejectedBinaryFiles.Add(requested.Clone());
    Assert.IsFalse(BinaryFileLocator.LocateBinaryFile(requested, settings).Found);
    requested.OriginalFileName = alternateName;
    Assert.AreEqual(actual, PEBinaryInfoProvider.GetBinaryFileInfo(FixturePath));
    var result = await BinaryFileLocator.LocateBinaryFileAsync(requested, settings);
    Assert.IsTrue(result.Found, result.Details);
    Assert.AreEqual(alternateName, result.BinaryFile.ImageName);
  }

  [TestMethod]
  public void StrictLookup_DoesNotReusePermissiveCachedResult() {
    var actual = PEBinaryInfoProvider.GetBinaryFileInfo(FixturePath);
    var requested = actual.Clone();
    requested.ImageName = "not-local.dll";
    requested.ImagePath = Path.Combine(directory_, requested.ImageName);
    requested.ImageSize += 0x1000;
    string entry = Path.Combine(directory_, requested.ImageName, $"{actual.TimeStamp:X8}{actual.ImageSize:x}");
    Directory.CreateDirectory(entry);
    File.Copy(FixturePath, Path.Combine(entry, requested.ImageName));
    var permissive = LocalSettings();
    permissive.SymbolPaths = new List<string> { $"srv*{directory_}*{directory_}" };
    permissive.DisableSizeProbingFallback = false;
    var probed = BinaryFileLocator.LocateBinaryFile(requested, permissive);
    Assert.IsTrue(probed.Found, probed.Details);
    Assert.AreEqual(actual.ImageSize, probed.BinaryFile.ImageSize);

    var strict = LocalSettings();
    strict.SymbolPaths = new List<string> { $"srv*{directory_}*{directory_}" };
    var result = BinaryFileLocator.LocateBinaryFile(requested, strict);
    Assert.IsFalse(result.Found, "Strict resolution must not return a size-probed cached result.");
  }

  [TestMethod]
  public void RestrictedNegativeLookup_DoesNotPoisonLaterSizeProbing() {
    var actual = PEBinaryInfoProvider.GetBinaryFileInfo(FixturePath);
    var requested = actual.Clone();
    requested.ImageName = "later-probed.dll";
    requested.ImagePath = Path.Combine(directory_, requested.ImageName);
    requested.ImageSize += 0x1000;
    string entry = Path.Combine(directory_, requested.ImageName, $"{actual.TimeStamp:X8}{actual.ImageSize:x}");
    Directory.CreateDirectory(entry);
    File.Copy(FixturePath, Path.Combine(entry, requested.ImageName));
    var settings = LocalSettings();
    settings.SymbolPaths = new List<string> { $"srv*{directory_}*{directory_}" };
    Assert.IsFalse(BinaryFileLocator.LocateBinaryFile(requested, settings).Found);
    Assert.IsFalse(settings.IsRejectedBinaryFile(requested));

    settings.DisableSizeProbingFallback = false;
    var result = BinaryFileLocator.LocateBinaryFile(requested, settings);
    Assert.IsTrue(result.Found, result.Details);
    Assert.AreEqual(actual.ImageSize, result.BinaryFile.ImageSize);
  }

  private static MethodInfo ProbeMethod() => typeof(BinaryFileLocator).GetMethod(
    "TryFindExecutableWithSizeProbing", BindingFlags.NonPublic | BindingFlags.Static);

  [TestMethod]
  public void SizeProbing_ExpiredBudgetPerformsNoLookup() {
    using var log = new StringWriter();
    var image = new BinaryFileDescriptor { ImageName = "missing.dll", ImageSize = 0x10000 };
    object[] args = { null, image, new[] { "missing.dll", "alternate.dll" },
      TimeSpan.Zero, TimeSpan.FromSeconds(1), LocalSettings(), log, 0L };
    Assert.IsNull(ProbeMethod().Invoke(null, args));
    Assert.AreEqual(0L, args[7]);
    StringAssert.Contains(log.ToString(), "timeout budget exhausted");
  }

  [TestMethod]
  public async Task SizeProbing_SlowServerStopsBeforeFurtherSizesOrAlternateNames() {
    using var stop = new CancellationTokenSource();
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var requests = new ConcurrentBag<string>();
    var clients = new List<Task>();
    var server = Task.Run(async () => {
      try {
        while (!stop.IsCancellationRequested) {
          var client = await listener.AcceptTcpClientAsync(stop.Token);
          clients.Add(Task.Run(async () => {
            using (client)
            using (var reader = new StreamReader(client.GetStream())) {
              try {
                string firstLine = await reader.ReadLineAsync(stop.Token);
                requests.Add(firstLine);
                await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token);
              }
              catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            }
          }));
        }
      }
      catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    });

    try {
      using var log = new StringWriter();
      using var reader = new SymbolReader(log, $"srv*{directory_}*http://127.0.0.1:{port}");
      reader.SecurityCheck += _ => true;
      var image = new BinaryFileDescriptor { ImageName = "missing.dll", TimeStamp = 1, ImageSize = 0x10000 };
      object[] args = { reader, image, new[] { "missing.dll", "alternate.dll" },
        TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), LocalSettings(), log, 0L };
      Assert.IsNull(ProbeMethod().Invoke(null, args));
      Assert.IsTrue(requests.Count > 0, log.ToString());
      Assert.IsTrue(requests.All(r => r.Contains("/missing.dll/1f000/", StringComparison.OrdinalIgnoreCase)),
        "A stalled probe must not trigger requests for later sizes or the alternate name: " + string.Join("; ", requests));
      StringAssert.Contains(log.ToString(), "stopped after a timeout");
    }
    finally {
      stop.Cancel();
      await server;
      listener.Stop();
      await Task.WhenAll(clients);
    }
  }

  [TestMethod]
  public void ApproximateMatch_IsRejectedByIdentityDisassembly() {
    var actual = PEBinaryInfoProvider.GetBinaryFileInfo(FixturePath);
    File.Copy(FixturePath, Path.Combine(directory_, "candidate.dll"));
    var requested = actual.Clone();
    requested.ImageName = "missing.dll";
    requested.ImagePath = Path.Combine(directory_, "missing.dll");
    requested.ImageSize += 0x1000;
    var settings = LocalSettings();
    settings.AllowApproximateBinaryMatch = true;

    var located = BinaryFileLocator.LocateBinaryFile(requested, settings);
    Assert.IsTrue(located.Found, located.Details);
    Assert.IsTrue(located.IsApproximateMatch, located.Details);
    var result = NativeAddressDisassemblyService.ResolveByIdentity(requested, FunctionRva(),
      NativeAddressForm.ModuleRva, FrameAddressKind.InstructionPointer, settings);
    Assert.IsFalse(result.Success);
    Assert.AreEqual(NativeAddressDisassemblyFailure.ImageIdentityMismatch, result.FailureReason);
    Assert.AreEqual(0, result.Instructions.Count);
  }

  [TestMethod]
  public void TraceImageDescriptor_PreservesOriginalFileName() {
    var image = new ProfileImage {
      FilePath = @"C:\Windows\system32\ntoskrnl.exe", OriginalFileName = "ntkrnlmp.exe",
      TimeStamp = 123, Size = 0x2000
    };
    using var raw = new RawProfileData("test.etl");
    var provider = new ETWProfileDataProvider();
    var method = typeof(ETWProfileDataProvider).GetMethod("FromProfileImage",
      BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.IsNotNull(method);
    var descriptor = (BinaryFileDescriptor)method.Invoke(provider, new object[] { image, raw, 0 });
    Assert.AreEqual("ntkrnlmp.exe", descriptor.OriginalFileName);
    Assert.AreEqual(image.TimeStamp, descriptor.TimeStamp);
    Assert.AreEqual(image.Size, descriptor.ImageSize);
  }
}
