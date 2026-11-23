// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Core.Profile.Spgo;

public sealed record SptIpSamples(ulong Count, IReadOnlyDictionary<SptEventKind, ulong> Sources);
public sealed record SptBranchSamples(ulong Count, IReadOnlyDictionary<uint, ulong> Destinations);

public sealed class SptSampleIndex {
  private SptSampleIndex(long imageBase, long imageSize,
                         IReadOnlyDictionary<uint, SptIpSamples> ipSamples,
                         IReadOnlyDictionary<uint, SptBranchSamples> branches,
                         bool hasIpSamples, bool hasBranches) {
    ImageBase = imageBase;
    ImageSize = imageSize;
    IpSamples = ipSamples;
    Branches = branches;
    HasIpSamples = hasIpSamples;
    HasBranches = hasBranches;
    SortedIpRvas = Array.AsReadOnly(ipSamples.Keys.Order().ToArray());
    SortedBranchRvas = Array.AsReadOnly(branches.Keys.Order().ToArray());
    UnmappedIpCount = Sum(ipSamples.Where(pair => !IsImageRva(pair.Key)).Select(pair => pair.Value.Count));
    UnmappedBranchCount = Sum(branches.Where(pair => !IsImageRva(pair.Key)).Select(pair => pair.Value.Count));
  }

  public long ImageBase { get; }
  public long ImageSize { get; }
  public bool HasIpSamples { get; }
  public bool HasBranches { get; }
  public IReadOnlyDictionary<uint, SptIpSamples> IpSamples { get; }
  public IReadOnlyDictionary<uint, SptBranchSamples> Branches { get; }
  public IReadOnlyList<uint> SortedIpRvas { get; }
  public IReadOnlyList<uint> SortedBranchRvas { get; }
  public ulong UnmappedIpCount { get; }
  public ulong UnmappedBranchCount { get; }

  public bool IsImageRva(uint rva) => rva != 0 && rva < ImageSize;

  public static SptSampleIndex Create(SptProfile profile, IReadOnlyList<int> matchingProgramIndices,
                                      long imageBase, long imageSize, CancellationToken token = default) {
    var ip = new Dictionary<uint, Dictionary<SptEventKind, ulong>>();
    var branches = new Dictionary<uint, Dictionary<uint, ulong>>();
    var matchedPrograms = matchingProgramIndices.ToHashSet();
    bool hasIp = false, hasLbr = false;
    ulong ipTotal = 0, lbrTotal = 0;
    try {
      foreach (var segment in profile.Segments) {
        token.ThrowIfCancellationRequested();
        if (!matchedPrograms.Contains(segment.ProgramIndex)) {
          continue;
        }
        foreach (var record in segment.Records) {
          token.ThrowIfCancellationRequested();
          if (record.Kind == SptEventKind.CallStack) {
            continue;
          }
          if (record.Kind == SptEventKind.LastBranchRecord) {
            hasLbr = true;
            for (int i = 0; i < record.Addresses.Count; i += 2) {
              // SPTDump prints [destination, source], not [source, destination].
              Accumulate(branches, record.Addresses[i + 1], record.Addresses[i], record.Weight);
              lbrTotal = checked(lbrTotal + record.Weight);
            }
          }
          else {
            hasIp = true;
            foreach (uint rva in record.Addresses) {
              Accumulate(ip, rva, record.Kind, record.Weight);
              ipTotal = checked(ipTotal + record.Weight);
            }
          }
        }
      }
      token.ThrowIfCancellationRequested();
      return new SptSampleIndex(imageBase, imageSize,
        ip.ToDictionary(pair => pair.Key,
          pair => new SptIpSamples(Sum(pair.Value.Values), pair.Value.AsReadOnly())).AsReadOnly(),
        branches.ToDictionary(pair => pair.Key,
          pair => new SptBranchSamples(Sum(pair.Value.Values), pair.Value.AsReadOnly())).AsReadOnly(),
        hasIp, hasLbr);
    }
    catch (OverflowException ex) {
      throw new InvalidDataException("SPT observation totals exceed the supported unsigned 64-bit count.", ex);
    }
  }

  private static void Accumulate<TKey>(Dictionary<uint, Dictionary<TKey, ulong>> index, uint rva,
                                        TKey key, ulong weight) where TKey : notnull {
    if (!index.TryGetValue(rva, out var values)) {
      values = new Dictionary<TKey, ulong>();
      index.Add(rva, values);
    }
    values[key] = checked(values.GetValueOrDefault(key) + weight);
  }

  private static ulong Sum(IEnumerable<ulong> values) {
    ulong sum = 0;
    foreach (ulong value in values) {
      sum = checked(sum + value);
    }
    return sum;
  }
}
