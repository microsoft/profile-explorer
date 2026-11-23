// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Security.Cryptography;

namespace ProfileExplorer.Core.Profile.Spgo;

public enum SptEventKind {
  UnhaltedCycles,
  RetiredInstructions,
  RetiredBranches,
  L1DataCacheMisses,
  L1InstructionCacheMisses,
  InstructionPointer,
  CallStack,
  LastBranchRecord
}

public sealed record SptProgram(int Index, string Name, Guid Id, uint Age);

// LBR addresses retain the pair order printed by SPTDump; no branch interpretation is performed here.
public sealed record SptRecord(SptEventKind Kind, ulong Weight, IReadOnlyList<uint> Addresses) {
  public int EntryCount => Kind == SptEventKind.LastBranchRecord ? Addresses.Count / 2 : Addresses.Count;
}

public sealed record SptSegment(int ProgramIndex, uint ByteLength, IReadOnlyList<SptRecord> Records);

public sealed record SptProfile(IReadOnlyDictionary<string, uint> Header,
                                IReadOnlyList<SptProgram> Programs,
                                IReadOnlyList<SptSegment> Segments) {
  public long RecordCount => Segments.Sum(segment => (long)segment.Records.Count);
}

public sealed record SptInputFile(string Path, string Hash) {
  public static FileStream OpenRead(string path) =>
    new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

  public static async Task<SptInputFile> CaptureAsync(FileStream stream, CancellationToken token) {
    stream.Position = 0;
    byte[] hash = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
    return new SptInputFile(stream.Name, Convert.ToHexString(hash));
  }

  public async Task VerifyUnchangedAsync(CancellationToken token) {
    using var stream = OpenRead(Path);
    var current = await CaptureAsync(stream, token).ConfigureAwait(false);
    if (Hash != current.Hash) {
      throw new IOException($"The file changed after validation: {Path}. Retry validation before attaching.");
    }
  }
}

public sealed record PreparedSptProfile(SptProfile Profile, IReadOnlyList<int> MatchingProgramIndices,
                                        string BinaryPath, Guid BinaryId, uint BinaryAge,
                                        string ToolPath, string ToolVersion,
                                        IReadOnlyList<SptInputFile> Inputs) {
  public async Task VerifyUnchangedAsync(CancellationToken token) {
    foreach (var input in Inputs) {
      await input.VerifyUnchangedAsync(token).ConfigureAwait(false);
    }
  }
}
