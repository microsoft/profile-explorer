// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling.Tests.Helpers;

/// <summary>
/// Builds synthetic IProfileSample and IProfileImage instances for unit tests.
/// </summary>
public static class SyntheticSampleBuilder {
  /// <summary>
  /// Create uniform samples — all at the same IP with the same weight.
  /// </summary>
  public static IReadOnlyList<IProfileSample> CreateUniform(
    int count, string module, long baseIp, TimeSpan weight, int processId = 1, int threadId = 1) {
    var samples = new List<IProfileSample>(count);
    for (int i = 0; i < count; i++) {
      samples.Add(new SyntheticSample(baseIp, weight, processId, threadId, module, baseIp - 0x1000));
    }

    return samples;
  }

  /// <summary>
  /// Create a hotspot — most samples at one IP, the rest distributed.
  /// </summary>
  public static IReadOnlyList<IProfileSample> CreateHotspot(
    int total, string module, long hotIp, double hotPercent,
    long baseIp = 0x1000, int spread = 10,
    int processId = 1, int threadId = 1) {
    int hotCount = (int)(total * hotPercent / 100.0);
    int coldCount = total - hotCount;
    var weight = TimeSpan.FromMilliseconds(1);
    long moduleBase = baseIp - 0x1000;

    var samples = new List<IProfileSample>(total);

    for (int i = 0; i < hotCount; i++) {
      samples.Add(new SyntheticSample(hotIp, weight, processId, threadId, module, moduleBase));
    }

    for (int i = 0; i < coldCount; i++) {
      long ip = baseIp + (i % spread) * 4; // Distribute across instruction offsets
      samples.Add(new SyntheticSample(ip, weight, processId, threadId, module, moduleBase));
    }

    return samples;
  }

  /// <summary>
  /// Create samples across multiple functions.
  /// </summary>
  public static IReadOnlyList<IProfileSample> CreateMultiFunction(
    params (string module, long baseIp, int sampleCount)[] functions) {
    var samples = new List<IProfileSample>();
    var weight = TimeSpan.FromMilliseconds(1);

    foreach (var (module, baseIp, count) in functions) {
      long moduleBase = baseIp - 0x1000;
      for (int i = 0; i < count; i++) {
        samples.Add(new SyntheticSample(baseIp + (i % 5) * 4, weight, 1, 1, module, moduleBase));
      }
    }

    return samples;
  }

  /// <summary>
  /// Create samples with stack frames for call tree testing.
  /// </summary>
  public static IReadOnlyList<IProfileSample> CreateWithStacks(
    params (string module, long leafIp, long[] stackIps, int count)[] entries) {
    var samples = new List<IProfileSample>();
    var weight = TimeSpan.FromMilliseconds(1);

    foreach (var (module, leafIp, stackIps, count) in entries) {
      long moduleBase = leafIp - 0x1000;
      // Stack is leaf-first.
      var fullStack = new long[] { leafIp }.Concat(stackIps).ToList();

      for (int i = 0; i < count; i++) {
        samples.Add(new SyntheticSample(leafIp, weight, 1, 1, module, moduleBase, fullStack));
      }
    }

    return samples;
  }

  /// <summary>
  /// Create fake image definitions.
  /// </summary>
  public static IReadOnlyList<IProfileImage> CreateImages(
    params (string name, long baseAddr, int size)[] images) {
    return images.Select(img => new SyntheticImage(
      img.name, img.baseAddr, img.size, 0, Guid.Empty, 0, img.name + ".pdb", 1)).ToList();
  }

  /// <summary>
  /// Create fake image definitions with PDB identity.
  /// </summary>
  public static IReadOnlyList<IProfileImage> CreateImagesWithPdb(
    params (string name, long baseAddr, int size, Guid pdbGuid, int pdbAge)[] images) {
    return images.Select(img => new SyntheticImage(
      img.name, img.baseAddr, img.size, 0, img.pdbGuid, img.pdbAge, img.name + ".pdb", 1)).ToList();
  }
}

internal class SyntheticSample : IProfileSample {
  public SyntheticSample(long ip, TimeSpan weight, int pid, int tid, string? imageName,
                          long imageBase, IReadOnlyList<long>? stackFrames = null) {
    InstructionPointer = ip;
    Weight = weight;
    ProcessId = pid;
    ThreadId = tid;
    ImageName = imageName;
    ImageBaseAddress = imageBase;
    StackFrames = stackFrames;
  }

  public long InstructionPointer { get; }
  public TimeSpan Weight { get; }
  public int ProcessId { get; }
  public int ThreadId { get; }
  public string? ImageName { get; }
  public long ImageBaseAddress { get; }
  public IReadOnlyList<long>? StackFrames { get; }
}

internal class SyntheticImage : IProfileImage {
  public SyntheticImage(string name, long baseAddr, int size, int timeStamp,
                         Guid pdbGuid, int pdbAge, string pdbName, int processId) {
    ImageName = name;
    BaseAddress = baseAddr;
    Size = size;
    TimeDateStamp = timeStamp;
    PdbGuid = pdbGuid;
    PdbAge = pdbAge;
    PdbName = pdbName;
    ProcessId = processId;
  }

  public string ImageName { get; }
  public long BaseAddress { get; }
  public int Size { get; }
  public int TimeDateStamp { get; }
  public Guid PdbGuid { get; }
  public int PdbAge { get; }
  public string PdbName { get; }
  public int ProcessId { get; }
}
