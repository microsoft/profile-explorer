// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics;
using System.Runtime.InteropServices;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Core.Profile.Spgo;

public static class SptProfileLoader {
  public static async Task<PreparedSptProfile> LoadAsync(string profilePath, string binaryPath,
                                                        string? pdbPath, string toolPath,
                                                        IProgress<string>? progress = null,
                                                        CancellationToken token = default) {
    // Keep inputs read-locked while the external tool and identity readers inspect them.
    using var profileFile = SptInputFile.OpenRead(profilePath);
    using var binaryFile = SptInputFile.OpenRead(binaryPath);
    using var toolFile = SptInputFile.OpenRead(toolPath);
    progress?.Report("Checking binary identity...");
    var identity = PEBinaryInfoProvider.GetSymbolFileInfo(binaryFile.Name);
    if (identity is null || identity.Id == Guid.Empty || identity.Age <= 0) {
      throw new InvalidDataException("The selected binary has no usable CodeView GUID/age. " +
                                     "An available binary with GUID/age is required.");
    }
    if (!string.IsNullOrWhiteSpace(pdbPath)) {
      token.ThrowIfCancellationRequested();
      progress?.Report("Checking PDB identity...");
    }
    using var pdbFile = OpenAndValidatePdb(pdbPath, identity);

    token.ThrowIfCancellationRequested();
    progress?.Report("Reading SPT with SPTDump...");
    var profile = await SptDumpTool.ReadAsync(toolFile.Name, profileFile.Name, token).ConfigureAwait(false);
    long expectedLength = checked((long)profile.Header["ProgIdsOffset"] +
                                  profile.Header["ProgIdsCapacity"] * 24L +
                                  profile.Segments.Sum(segment => 8L + segment.ByteLength));
    if (profileFile.Length != expectedLength) {
      throw new InvalidDataException("SPTDump output does not account for the entire SPT file.");
    }

    progress?.Report("Matching SPT program identities...");
    var matches = profile.Programs.Where(program => program.Id == identity.Id && program.Age == (uint)identity.Age)
      .Select(program => program.Index).ToList();
    if (matches.Count == 0) {
      string found = string.Join("; ", profile.Programs.Take(8)
        .Select(program => $"{program.Name}: {program.Id:B}, age {program.Age}"));
      throw new InvalidDataException($"No SPT program matches binary GUID {identity.Id:B}, age {identity.Age}.\n" +
                                     $"SPT identities: {found}");
    }
    if (!profile.Segments.Any(segment => matches.Contains(segment.ProgramIndex) &&
                                        segment.Records.Any(record => record.EntryCount > 0))) {
      throw new InvalidDataException("The SPT identity matches, but that program has no sample records.");
    }

    progress?.Report("Fingerprinting validated inputs...");
    var inputs = new List<SptInputFile>();
    foreach (var file in new[] {profileFile, binaryFile, toolFile, pdbFile}) {
      if (file != null) {
        inputs.Add(await SptInputFile.CaptureAsync(file, token).ConfigureAwait(false));
      }
    }
    return new PreparedSptProfile(profile, matches.AsReadOnly(), binaryFile.Name, identity.Id,
                                   (uint)identity.Age, toolFile.Name,
                                   FileVersionInfo.GetVersionInfo(toolFile.Name).FileVersion ?? "Unknown",
                                   inputs.AsReadOnly());
  }

  private static FileStream? OpenAndValidatePdb(string? path, SymbolFileDescriptor identity) {
    if (string.IsNullOrWhiteSpace(path)) {
      return null;
    }
    FileStream? file = null;
    try {
      file = SptInputFile.OpenRead(path);
      using var pdb = new PdbSymbolProvider();
      if (!pdb.LoadDebugInfo(file.Name, enumerateImmediately: false)) {
        throw new InvalidDataException($"Cannot read the PDB identity: {file.Name}. " +
                                       PdbSymbolProvider.DiaRegistrationError);
      }
      if (!pdb.MatchesIdentity(identity)) {
        throw new InvalidDataException($"The PDB does not match binary GUID {identity.Id:B}, age {identity.Age}.");
      }
      return file;
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or COMException) {
      file?.Dispose();
      throw new SptPdbValidationException(ex.Message, ex);
    }
  }
}

public sealed class SptPdbValidationException : IOException {
  public SptPdbValidationException(string message, Exception innerException) : base(message, innerException) { }
}
