// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Symbols;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// Locates binary files on disk or downloads them from a symbol server using TraceEvent's
/// <see cref="SymbolReader"/>. This is the Profile Explorer-side (TraceEvent-coupled) counterpart
/// to the TraceEvent-free <see cref="PEBinaryInfoProvider"/> reader that lives in the library.
/// </summary>
public static class BinaryFileLocator {
  private static ConcurrentDictionary<BinaryFileDescriptor, BinaryFileSearchResult> resolvedBinariesCache_ = new();

  /// <summary>
  /// Clears the static resolved binaries cache.
  /// Call between trace loads to ensure a clean resolution state.
  /// </summary>
  public static void ClearResolvedCache() {
    resolvedBinariesCache_.Clear();
    PEBinaryInfoProvider.ClearVersionInfoCache();
  }

  public static async Task<BinaryFileSearchResult> LocateBinaryFileAsync(BinaryFileDescriptor binaryFile,
                                                                         SymbolFileSourceSettings settings) {
    // Check if the binary was requested before.
    if (resolvedBinariesCache_.TryGetValue(binaryFile, out var searchResult)) {
      return searchResult;
    }

    return await Task.Run(() => {
      return LocateBinaryFile(binaryFile, settings);
    }).ConfigureAwait(false);
  }

  public static BinaryFileSearchResult LocateBinaryFile(BinaryFileDescriptor binaryFile,
                                                        SymbolFileSourceSettings settings) {
    var sw = Stopwatch.StartNew();
    
    // Check if the binary was requested before.
    if (resolvedBinariesCache_.TryGetValue(binaryFile, out var searchResult)) {
      DiagnosticLogger.LogDebug($"[BinarySearch] Cache hit for {binaryFile.ImageName}");
      return searchResult;
    }

    DiagnosticLogger.LogInfo($"[BinarySearch] Starting binary search for {binaryFile.ImageName} (Size: {binaryFile.ImageSize}, Timestamp: {binaryFile.TimeStamp})");

    // Check if this binary was previously rejected (failed lookup in a prior session).
    if (settings.IsRejectedBinaryFile(binaryFile)) {
      DiagnosticLogger.LogInfo($"[BinarySearch] SKIPPED - previously rejected: {binaryFile.ImageName}");
      searchResult = BinaryFileSearchResult.Failure(binaryFile, "Previously rejected");
      resolvedBinariesCache_.TryAdd(binaryFile, searchResult);
      return searchResult;
    }

    // Resolve NT kernel paths like \SystemRoot\system32\ntoskrnl.exe
    // to actual file system paths like C:\Windows\system32\ntoskrnl.exe.
    string resolvedPath = ResolveNtKernelPath(binaryFile.ImagePath);
    if (resolvedPath != binaryFile.ImagePath) {
      DiagnosticLogger.LogInfo($"[BinarySearch] Resolved NT path for {binaryFile.ImageName}: {binaryFile.ImagePath} -> {resolvedPath}");
      binaryFile.ImagePath = resolvedPath;
    }

    // Quick check if trace was recorded on local machine.
    string approximateMatchPath = null;
    long correctedImageSize = 0;
    string result = FindExactLocalBinaryFile(binaryFile, out approximateMatchPath, out correctedImageSize);

    if (result != null) {
      DiagnosticLogger.LogInfo($"[BinarySearch] Found exact local binary for {binaryFile.ImageName} at {result} ({sw.ElapsedMilliseconds}ms)");
      binaryFile = PEBinaryInfoProvider.GetBinaryFileInfo(result);
      searchResult = BinaryFileSearchResult.Success(binaryFile, result, "");
      resolvedBinariesCache_.TryAdd(binaryFile, searchResult);
      return searchResult;
    }

    // Correct ImageSize if the file exists locally with matching timestamp but
    // different size. The symbol server indexes binaries by TimeDateStamp+SizeOfImage
    // from the PE header, but the ETW kernel event may report a larger mapped view size
    // (the kernel mapping can include extra slack pages). Using the wrong size causes
    // 404s on the symbol server even when the binary IS indexed there.
    if (correctedImageSize > 0 && correctedImageSize != binaryFile.ImageSize) {
      DiagnosticLogger.LogInfo(
        $"[BinarySearch] Correcting ImageSize for {binaryFile.ImageName} from ETW value {binaryFile.ImageSize} " +
        $"to PE SizeOfImage {correctedImageSize} for symbol server lookup");
      binaryFile.ImageSize = correctedImageSize;
    }

    using var logWriter = new StringWriter();

    try {
      // Try to use symbol server to download binary.
      // Add local binary path directly instead of using WithSymbolPaths (which clones
      // settings again and resets runtime state like timeout overrides).
      if (File.Exists(binaryFile.ImagePath)) {
        settings.InsertSymbolPath(binaryFile.ImagePath);
      }

      string userSearchPath = PDBDebugInfoProvider.ConstructSymbolSearchPath(settings);

      //? TODO: Making a new instance clears the "dead servers",
      //? have a way to share the list between multiple instances.
      using var symbolReader =
        new SymbolReader(logWriter, userSearchPath, PDBDebugInfoProvider.CreateAuthHandler(settings));
      symbolReader.SecurityCheck += s => true; // Allow symbols from "unsafe" locations.

      // Set symbol server timeout from settings (default 10 seconds).
      // Use EffectiveTimeoutSeconds which is reduced if bellwether test marked server as degraded.
      int timeoutSeconds = settings.EffectiveTimeoutSeconds > 0 ? settings.EffectiveTimeoutSeconds : 10;
      symbolReader.ServerTimeout = TimeSpan.FromSeconds(timeoutSeconds);
      DiagnosticLogger.LogInfo($"[BinarySearch] ServerTimeout={timeoutSeconds}s, RejectPreviouslyFailedFiles={settings.RejectPreviouslyFailedFiles}, " +
                               $"RejectedBinaries={settings.RejectedBinaryFiles?.Count ?? 0} for {binaryFile.ImageName}");

      //? TODO: Workaround for cases where the ETL file doesn't have a timestamp
      //? and SymbolReader would reject the bin even on the same machine...
      //? Better way to handle this is to have SymReader accept a func to check if PDB is valid to use
      if (binaryFile.TimeStamp == 0 && File.Exists(binaryFile.ImagePath)) {
        var binInfo = PEBinaryInfoProvider.GetBinaryFileInfo(binaryFile.ImagePath);
        binaryFile.TimeStamp = binInfo.TimeStamp;
      }

      //Trace.WriteLine($"Start download of {Utils.TryGetFileName(binaryFile.ImageName)}");
      // Try the on-disk/ETW-reported name first, then the module's own embedded OriginalFileName
      // (if different) as a genuine alternate identity key -- not a guess, exactly like PDBs are
      // routinely indexed under a name that differs from "<image>.pdb" (see
      // BinaryFileDescriptor.OriginalFileName). This fixes lookups for binaries whose symbol-server
      // key uses their internal name instead of the on-disk name (e.g. ntoskrnl.exe is indexed as
      // "ntkrnlmp.exe" -- confirmed via a direct symweb probe: the on-disk name 404s at every
      // candidate size, while the exact same TimeStamp+ImageSize under "ntkrnlmp.exe" succeeds).
      string[] candidateNames = string.IsNullOrEmpty(binaryFile.OriginalFileName) ||
                                 string.Equals(binaryFile.OriginalFileName, binaryFile.ImageName, StringComparison.OrdinalIgnoreCase)
        ? [binaryFile.ImageName]
        : [binaryFile.ImageName, binaryFile.OriginalFileName];

      foreach (string candidateName in candidateNames) {
        result = symbolReader.FindExecutableFilePath(candidateName, binaryFile.TimeStamp, (int)binaryFile.ImageSize);

        if (result != null) {
          if (candidateName != binaryFile.ImageName) {
            DiagnosticLogger.LogInfo(
              $"[BinarySearch] Found {binaryFile.ImageName} on symbol server under its OriginalFileName " +
              $"'{candidateName}' instead of the on-disk name");
          }

          break;
        }
      }

      if (result == null) {
        // Finally, try an approximate manual search.
        DiagnosticLogger.LogDebug($"[BinarySearch] Symbol server search failed, trying approximate local search for {binaryFile.ImageName}");
        result = FindMatchingLocalBinaryFile(binaryFile, settings, ref approximateMatchPath);
      }

      // No local copy at all (e.g. a third-party driver we've never seen on this machine), so
      // FindExactLocalBinaryFile never had a chance to correct ImageSize from a PE header. Retry
      // the exact same server lookup with nearby candidate sizes: the kernel/DataLayer-reported
      // ImageSize is the mapped-view size, which can exceed the real PE SizeOfImage that symweb
      // indexes by, by a few slack pages -- probe downward in small alignment steps until we get
      // a hit, exactly mirroring the local-file correction above but against the server itself.
      // Tries every candidate name (on-disk + OriginalFileName) since a module could need BOTH a
      // corrected name AND a corrected size.
      // DisableSizeProbingFallback forces exact-match-only resolution (no probing at all) -- use
      // it to verify a computed identity is correct on its own merits, rather than letting the
      // probing fallback silently paper over a wrong ImageSize.
      if (result == null && settings.AllowApproximateBinaryMatch && !settings.DisableSizeProbingFallback) {
        foreach (string candidateName in candidateNames) {
          result = TryFindExecutableWithSizeProbing(symbolReader, binaryFile, candidateName, out long probedSize);

          if (result != null) {
            DiagnosticLogger.LogInfo(
              $"[BinarySearch] Found {binaryFile.ImageName} on symbol server after size-probing " +
              $"(name='{candidateName}', reported ImageSize {binaryFile.ImageSize} -> corrected {probedSize})");
            binaryFile.ImageSize = probedSize;
            break;
          }
        }
      }
    }
    catch (Exception ex) {
      DiagnosticLogger.LogError($"[BinarySearch] Exception during binary search for {binaryFile.ImageName}: {ex.Message}", ex);
      Trace.TraceError($"Failed FindExecutableFilePath: {ex.Message}");
    }

    var searchDuration = sw.Elapsed;
    string searchLog = logWriter.ToString();
    
#if DEBUG
    Trace.WriteLine($">> TraceEvent FindExecutableFilePath for {binaryFile.ImageName}");
    Trace.WriteLine(searchLog);
    Trace.WriteLine("<< TraceEvent");
#endif

    if (!string.IsNullOrEmpty(result) && File.Exists(result)) {
      DiagnosticLogger.LogInfo($"[BinarySearch] Found binary for {binaryFile.ImageName} at {result} ({searchDuration.TotalMilliseconds:F0}ms)");
      // Read the binary info from the local file to fill in all fields.
      binaryFile = PEBinaryInfoProvider.GetBinaryFileInfo(result);
      searchResult = BinaryFileSearchResult.Success(binaryFile, result, searchLog);
    }
    else if (settings.AllowApproximateBinaryMatch &&
             !string.IsNullOrEmpty(approximateMatchPath) &&
             File.Exists(approximateMatchPath)) {
      // Fallback: use a binary with matching timestamp but different size.
      // This handles cases where the kernel-reported ImageSize in the ETW trace
      // differs from the PE header SizeOfImage, or the DLL was serviced.
      long traceImageSize = binaryFile.ImageSize;
      var originalDescriptor = binaryFile;
      DiagnosticLogger.LogWarning(
        $"[BinarySearch] Using APPROXIMATE match for {binaryFile.ImageName} " +
        $"at {approximateMatchPath} ({searchDuration.TotalMilliseconds:F0}ms) - " +
        $"timestamp matches but image size differs");
      binaryFile = PEBinaryInfoProvider.GetBinaryFileInfo(approximateMatchPath);
      string details = $"Approximate match: timestamp matches but image size differs " +
        $"(trace: {traceImageSize}, on-disk: {binaryFile.ImageSize}). Disassembly may not be fully accurate.\n{searchLog}";
      searchResult = BinaryFileSearchResult.ApproximateSuccess(binaryFile, approximateMatchPath, details);
      // Cache under the original trace descriptor so subsequent lookups for the
      // same trace module hit the cache instead of repeating the search.
      resolvedBinariesCache_.TryAdd(originalDescriptor, searchResult);
    }
    else {
      DiagnosticLogger.LogWarning($"[BinarySearch] Failed to find binary for {binaryFile.ImageName} ({searchDuration.TotalMilliseconds:F0}ms)");
      searchResult = BinaryFileSearchResult.Failure(binaryFile, searchLog);

      // Record failed lookup to avoid retrying in future sessions,
      // but skip transient failures (timeout, auth, server errors).
      var reason = settings.ClassifySearchFailure(searchLog);
      settings.RejectBinaryFile(binaryFile, reason, searchLog);
    }

    resolvedBinariesCache_.TryAdd(binaryFile, searchResult);
    return searchResult;
  }

  // PE section alignment is almost always 0x1000 (4KB); the kernel-reported mapped-view size
  // rounds up to a small number of these pages beyond the real SizeOfImage. Bounded to 16 steps
  // (64KB of slack) -- generous relative to observed real-world slack of a few pages -- so a
  // genuinely-missing binary still fails fast instead of hammering the server.
  private const int SizeProbeStepBytes = 0x1000;
  private const int SizeProbeMaxSteps = 16;

  /// <summary>
  /// Retries <see cref="SymbolReader.FindExecutableFilePath"/> against nearby candidate
  /// ImageSize values, stepping down from <paramref name="binaryFile"/>'s reported size in
  /// <see cref="SizeProbeStepBytes"/> increments. Used when the exact TimeStamp+ImageSize lookup
  /// 404s and there's no local copy of the binary to read the corrected PE SizeOfImage from
  /// directly (see <see cref="FindExactLocalBinaryFile"/> for that local-file counterpart).
  /// <paramref name="candidateName"/> lets the caller probe under either the on-disk ImageName or
  /// the module's OriginalFileName (see <see cref="BinaryFileDescriptor.OriginalFileName"/>).
  /// </summary>
  private static string TryFindExecutableWithSizeProbing(SymbolReader symbolReader,
                                                          BinaryFileDescriptor binaryFile,
                                                          string candidateName,
                                                          out long correctedSize) {
    correctedSize = 0;
    long baseSize = binaryFile.ImageSize;

    for (int step = 1; step <= SizeProbeMaxSteps; step++) {
      long candidateSize = baseSize - (long)step * SizeProbeStepBytes;

      if (candidateSize <= 0) {
        break;
      }

      string candidateResult;

      try {
        candidateResult = symbolReader.FindExecutableFilePath(candidateName, binaryFile.TimeStamp, (int)candidateSize);
      }
      catch (Exception ex) {
        DiagnosticLogger.LogDebug($"[BinarySearch] Size probe failed for {candidateName} at size {candidateSize}: {ex.Message}");
        continue;
      }

      if (candidateResult != null) {
        correctedSize = candidateSize;
        return candidateResult;
      }
    }

    return null;
  }

  private static string ResolveNtKernelPath(string path) {
    if (string.IsNullOrEmpty(path)) {
      return path;
    }

    if (path.StartsWith(@"\SystemRoot", StringComparison.OrdinalIgnoreCase)) {
      string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
      return systemRoot + path.Substring(@"\SystemRoot".Length);
    }

    return path;
  }

  private static string FindExactLocalBinaryFile(BinaryFileDescriptor binaryFile,
                                                  out string approximateMatchPath,
                                                  out long correctedImageSize) {
    approximateMatchPath = null;
    correctedImageSize = 0;

    if (File.Exists(binaryFile.ImagePath)) {
      var fileInfo = PEBinaryInfoProvider.GetBinaryFileInfo(binaryFile.ImagePath);

      if (fileInfo != null && fileInfo.TimeStamp == binaryFile.TimeStamp) {
        if (fileInfo.ImageSize == binaryFile.ImageSize) {
          return binaryFile.ImagePath;
        }

        // Timestamp matches but size differs. This commonly happens because the
        // kernel-reported ImageSize in ETW events is the mapped view size, which
        // can exceed the PE header's SizeOfImage by a few pages of slack.
        // Record the local file as an approximate match and save the PE SizeOfImage
        // so we can correct the symbol server lookup key.
        approximateMatchPath = binaryFile.ImagePath;
        correctedImageSize = fileInfo.ImageSize;
        DiagnosticLogger.LogInfo(
          $"[BinarySearch] Local binary for {binaryFile.ImageName} at {binaryFile.ImagePath}: " +
          $"timestamp matches ({fileInfo.TimeStamp}), size differs " +
          $"(PE SizeOfImage: {fileInfo.ImageSize}, ETW ImageSize: {binaryFile.ImageSize})");
      }
    }

    return null;
  }

  private static string FindMatchingLocalBinaryFile(BinaryFileDescriptor binaryFile,
                                                    SymbolFileSourceSettings settings,
                                                    ref string approximateMatchPath) {
    // Manually search in the provided directories.
    // This helps in cases where the original fine name doesn't match
    // the one on disk, like it seems to happen sometimes with the SPEC runner.
    string winPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    string sysPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string sysx86Path = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);

    // Don't search in the system dirs though, it's pointless
    // and takes a long time checking thousands of binaries.
    bool PathIsSubPath(string subPath, string basePath) {
      string rel = Path.GetRelativePath(basePath, subPath);
      return !rel.StartsWith('.') && !Path.IsPathRooted(rel);
    }

    foreach (string path in settings.SymbolPaths) {
      if (PathIsSubPath(path, winPath) ||
          PathIsSubPath(path, sysPath) ||
          PathIsSubPath(path, sysx86Path)) {
        continue;
      }

      try {
        string searchPath = Utils.TryGetDirectoryName(path);

        foreach (string file in Directory.EnumerateFiles(searchPath, "*.*", SearchOption.AllDirectories)) {
          if (!Utils.IsBinaryFile(file)) {
            continue;
          }

          var fileInfo = PEBinaryInfoProvider.GetBinaryFileInfo(file);

          if (fileInfo != null && fileInfo.TimeStamp == binaryFile.TimeStamp) {
            if (fileInfo.ImageSize == binaryFile.ImageSize) {
              return file;
            }

            // Track first approximate match (timestamp matches, size differs).
            if (approximateMatchPath == null) {
              approximateMatchPath = file;
              DiagnosticLogger.LogInfo(
                $"[BinarySearch] Approximate match for {binaryFile.ImageName} in search paths: " +
                $"{file} (on-disk: {fileInfo.ImageSize}, trace: {binaryFile.ImageSize})");
            }
          }
        }
      }
      catch (Exception ex) {
        Trace.TraceError($"Exception searching for binary {binaryFile.ImageName} in {path}: {ex.Message}");
      }
    }

    return null;
  }
}
