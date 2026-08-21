// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// On-demand disassembly-by-identity — the single entry point callers (e.g. FUN AI's pool-analysis
/// pipeline and its tests) use to disassemble a stack address without pre-downloading the binary.
///
/// Given a binary identity (image name + timestamp + size) and symbol settings, it downloads the
/// exact image via <see cref="BinaryFileLocator"/> (authenticated symbol/binary server, cached
/// locally under the settings' symbol path) and then runs
/// <see cref="NativeAddressDisassembler.Resolve"/>. This lives in ProfileExplorerCore because it
/// couples the TraceEvent-based <see cref="BinaryFileLocator"/> with the TraceEvent-free
/// <see cref="NativeAddressDisassembler"/> (Core references Profiling, not the reverse).
///
/// No trace or process is loaded: the caller has already extracted (module identity + RVA) from the
/// trace, so the only work here is fetching one binary and decoding the RVA.
/// </summary>
public static class NativeAddressDisassemblyService {
  /// <summary>
  /// Locates/downloads the exact binary for <paramref name="identity"/> and disassembles the
  /// function containing <paramref name="address"/>. Boundaries come from a supplied
  /// <paramref name="symbolDebugInfo"/> PDB when available, else the PE exception directory
  /// (.pdata) — so symbol-less third-party drivers still disassemble.
  /// </summary>
  public static NativeAddressDisassemblyResult ResolveByIdentity(
      BinaryFileDescriptor identity,
      long address,
      NativeAddressForm addressForm,
      FrameAddressKind frameKind,
      SymbolFileSourceSettings symbolSettings,
      ISymbolDebugInfo? symbolDebugInfo = null) {
    ArgumentNullException.ThrowIfNull(identity);
    ArgumentNullException.ThrowIfNull(symbolSettings);

    BinaryFileSearchResult located = BinaryFileLocator.LocateBinaryFile(identity, symbolSettings);
    if (located is not {Found: true} || string.IsNullOrEmpty(located.FilePath) || !File.Exists(located.FilePath)) {
      return new NativeAddressDisassemblyResult {
        Success = false,
        FailureReason = NativeAddressDisassemblyFailure.BinaryPathInvalid,
        ModuleName = identity.ImageName,
        ErrorMessage = $"On-demand binary download failed for {identity.ImageName} " +
                       $"(TimeStamp=0x{identity.TimeStamp:X}, Size={identity.ImageSize}): " +
                       $"{located?.Details ?? "not found on symbol/binary server"}"
      };
    }

    return NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest {
      BinaryPath = located.FilePath,
      ExpectedImageIdentity = identity,
      Address = address,
      AddressForm = addressForm,
      FrameKind = frameKind,
      SymbolDebugInfo = symbolDebugInfo
    });
  }
}
