// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProfileExplorer.Core.Profile.Spgo;

public static class SptDumpParser {
  private static readonly string[] HeaderNames = {
    "Signature", "Version", "RawDataId", "TargetArch", "StrTableOffset", "CbStrTableUsed",
    "CbStrTableCapacity", "ProgIdsOffset", "ProgIdsUsed", "ProgIdsCapacity"
  };
  private static readonly Regex ProgramPattern = new(
    @"^Index\[(\d+)\] (.+) Guid\{([0-9a-fA-F-]+)\} Age\[(\d+)\]$", RegexOptions.CultureInvariant);
  private static readonly Regex StringPattern = new(
    @"^Offset\[(0x[0-9a-fA-F]+)\]: (.+)$", RegexOptions.CultureInvariant);
  private static readonly Regex SelectorPattern = new(
    @"^SPT_OP_BINARY_ID\s+(\d+)\s+(.+)\s+(\d+) bytes$", RegexOptions.CultureInvariant);

  public static async Task<SptProfile> ParseAsync(TextReader reader, CancellationToken token = default) {
    var header = new Dictionary<string, uint>();
    var strings = new Dictionary<uint, string>();
    var programs = new Dictionary<int, SptProgram>();
    var segments = new List<SptSegment>();
    var records = new List<SptRecord>();
    int section = 0;
    int programIndex = -1;
    uint segmentLength = 0;
    ulong segmentBytes = 0;
    ulong? repeat = null;
    int lineNumber = 0;

    InvalidDataException Error(string message) => new($"SPTDump output, line {lineNumber}: {message}");

    void FinishSegment() {
      if (repeat.HasValue) {
        throw Error("Repetition count has no following sample record.");
      }
      if (programIndex >= 0) {
        if (segmentBytes != segmentLength) {
          throw Error("Incomplete or inconsistent binary event segment.");
        }
        segments.Add(new SptSegment(programIndex, segmentLength, records.AsReadOnly()));
      }
      records = new List<SptRecord>();
      segmentBytes = 0;
    }

    try {
      while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } rawLine) {
        lineNumber++;
        token.ThrowIfCancellationRequested();
        string line = rawLine.Trim();
        if (line.Length == 0) {
          continue;
        }

        int nextSection = line switch {
          "*** File Header ***" => 1,
          "*** Strings ***" => 2,
          "*** Program IDs ***" => 3,
          "*** Events ***" => 4,
          _ => 0
        };
        if (nextSection != 0) {
          if (nextSection != section + 1) {
            throw Error("Unexpected or repeated section header.");
          }
          section = nextSection;
          continue;
        }

        if (section == 1) {
          string[] fields = line.Split(':', 2, StringSplitOptions.TrimEntries);
          if (fields.Length != 2 || !HeaderNames.Contains(fields[0]) ||
              !header.TryAdd(fields[0], Hex(fields[1]))) {
            throw Error("Invalid or unsupported header field.");
          }
        }
        else if (section == 2) {
          var match = StringPattern.Match(line);
          if (!match.Success || !strings.TryAdd(Hex(match.Groups[1].Value), match.Groups[2].Value)) {
            throw Error("Invalid or duplicate string-table entry.");
          }
        }
        else if (section == 3) {
          var match = ProgramPattern.Match(line);
          if (!match.Success) {
            throw Error("Invalid program identity.");
          }
          int index = checked((int)Number(match.Groups[1].Value));
          var program = new SptProgram(index, match.Groups[2].Value,
                                       Guid.Parse(match.Groups[3].Value),
                                       checked((uint)Number(match.Groups[4].Value)));
          if (!programs.TryAdd(index, program)) {
            throw Error("Duplicate program index.");
          }
        }
        else if (section == 4) {
          if (line.StartsWith("SPT_OP_BINARY_ID ", StringComparison.Ordinal)) {
            FinishSegment();
            var match = SelectorPattern.Match(line);
            if (!match.Success) {
              throw Error("Invalid binary selector.");
            }
            programIndex = checked((int)Number(match.Groups[1].Value));
            segmentLength = checked((uint)Number(match.Groups[3].Value));
            if (!programs.TryGetValue(programIndex, out var program) ||
                program.Name != match.Groups[2].Value.TrimEnd()) {
              throw Error("Binary selector does not reference a known program.");
            }
            continue;
          }
          if (programIndex < 0) {
            throw Error("Sample record precedes its binary selector.");
          }
          string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
          if (fields[0] == "SPT_OP_REPEAT") {
            if (fields.Length != 2 || repeat.HasValue) {
              throw Error("Invalid or consecutive repetition records.");
            }
            repeat = Number(fields[1]);
            segmentBytes = checked(segmentBytes + 10);
            continue;
          }

          var kind = fields[0] switch {
            "SPT_OP_UNHALT_CYCLE" => SptEventKind.UnhaltedCycles,
            "SPT_OP_RETIRE_INSTR" => SptEventKind.RetiredInstructions,
            "SPT_OP_RETIRE_BR_INSTR" => SptEventKind.RetiredBranches,
            "SPT_OP_L1_DCACHE_MISS" => SptEventKind.L1DataCacheMisses,
            "SPT_OP_L1_ICACHE_MISS" => SptEventKind.L1InstructionCacheMisses,
            "SPT_OP_ETW_INSTR" => SptEventKind.InstructionPointer,
            "SPT_OP_ETW_CALLSTACK" => SptEventKind.CallStack,
            "SPT_OP_LBR" => SptEventKind.LastBranchRecord,
            _ => throw Error($"Unsupported event type: {fields[0]}.")
          };
          if (fields.Length < 2) {
            throw Error("Missing sample count.");
          }
          int count = checked((byte)Number(fields[1]));
          if (fields.Length != count + 2) {
            throw Error("Sample count does not match the record's payload.");
          }
          var addresses = new List<uint>(count * 2);
          for (int i = 2; i < fields.Length; i++) {
            string field = fields[i];
            if (i < fields.Length - 1) {
              if (!field.EndsWith(',')) {
                throw Error("Missing separator between sample entries.");
              }
              field = field[..^1];
            }
            if (kind == SptEventKind.LastBranchRecord) {
              if (!field.StartsWith('[') || !field.EndsWith(']')) {
                throw Error("Invalid LBR pair.");
              }
              string[] pair = field[1..^1].Split(',');
              if (pair.Length != 2) {
                throw Error("Invalid LBR pair.");
              }
              addresses.Add(Hex(pair[0]));
              addresses.Add(Hex(pair[1]));
            }
            else {
              addresses.Add(Hex(field));
            }
          }
          ulong weight = checked(1 + repeat.GetValueOrDefault());
          records.Add(new SptRecord(kind, weight, addresses.AsReadOnly()));
          segmentBytes = checked(segmentBytes + 2 + (ulong)addresses.Count * 4);
          repeat = null;
        }
        else {
          throw Error("Expected an SPT file header.");
        }
      }
      FinishSegment();

      if (section != 4 || HeaderNames.Any(name => !header.ContainsKey(name))) {
        throw Error("Missing sections or header fields.");
      }
      if (header["Signature"] != 0x5350543A || header["Version"] != 1) {
        throw Error("Expected SPT signature and version 1.");
      }
      if (header["ProgIdsUsed"] != programs.Count || header["ProgIdsUsed"] > header["ProgIdsCapacity"] ||
          header["CbStrTableUsed"] > header["CbStrTableCapacity"] ||
          header["StrTableOffset"] < 32 ||
          header["ProgIdsOffset"] < (ulong)header["StrTableOffset"] + header["CbStrTableCapacity"] ||
          programs.Keys.Any(index => index < 0 || index >= programs.Count) ||
          strings.Any(pair => (ulong)pair.Key + (ulong)Encoding.UTF8.GetByteCount(pair.Value) + 1 >
                              header["CbStrTableUsed"]) ||
          programs.Values.Any(program => !strings.ContainsValue(program.Name))) {
        throw Error("Inconsistent header or program/string tables.");
      }
      if (segments.Count == 0 || segments.All(segment => segment.Records.Count == 0)) {
        throw Error("The SPT contains no sample records.");
      }

      // Check weighted totals without expanding repetition records.
      foreach (var group in segments.SelectMany(segment => segment.Records).GroupBy(record => record.Kind)) {
        ulong total = 0;
        foreach (var record in group) {
          total = checked(total + checked(record.Weight * (ulong)record.EntryCount));
        }
      }
      return new SptProfile(header.AsReadOnly(), programs.Values.OrderBy(program => program.Index).ToList().AsReadOnly(),
                            segments.AsReadOnly());
    }
    catch (Exception ex) when (ex is FormatException or OverflowException) {
      throw Error($"Invalid numeric value or overflow: {ex.Message}");
    }
  }

  private static uint Hex(string text) {
    if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
      throw new FormatException($"Expected hexadecimal value, got '{text}'.");
    }
    return uint.Parse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
  }

  private static ulong Number(string text) =>
    ulong.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
}
