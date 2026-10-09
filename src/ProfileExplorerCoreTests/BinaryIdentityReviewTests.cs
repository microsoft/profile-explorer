// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
