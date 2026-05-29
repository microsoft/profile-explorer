// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Reflection;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling.Profiling;

/// <summary>
/// IP skid correction constants per processor architecture.
/// CPU sampling IPs may point past the instruction that was actually executing
/// due to instruction pipeline effects. This corrects by walking backward.
/// </summary>
public static class InstructionOffsetConfig {
  /// <summary>
  /// Get the IP skid correction parameters for the given architecture.
  /// </summary>
  public static SkidCorrectionParams GetSkidCorrection(ProcessorArchitecture architecture) {
    return architecture switch {
      ProcessorArchitecture.Arm => new SkidCorrectionParams(4, 4, 1), // ARM: fixed 4-byte instructions
      _ => new SkidCorrectionParams(1, 16, 1) // x86/x64: variable-length 1-16 bytes
    };
  }

  /// <summary>
  /// Try to find the actual instruction at or before the given offset,
  /// compensating for IP skid on variable-length instruction architectures.
  /// </summary>
  /// <param name="offset">The sampled instruction offset (relative to function start).</param>
  /// <param name="knownOffsets">Set of known instruction offsets from disassembly.</param>
  /// <param name="architecture">Target processor architecture.</param>
  /// <returns>The corrected offset, or the original if no correction is possible.</returns>
  public static long CorrectSkid(long offset, IReadOnlySet<long> knownOffsets, ProcessorArchitecture architecture) {
    if (knownOffsets.Contains(offset)) {
      return offset; // Exact match, no correction needed.
    }

    var skid = GetSkidCorrection(architecture);

    // Walk backward to find the nearest known instruction.
    for (int multiplier = skid.InitialMultiplier;
         multiplier * skid.AdjustIncrement <= skid.MaxAdjust;
         multiplier++) {
      long adjusted = offset - multiplier * skid.AdjustIncrement;
      if (adjusted < 0) break;

      if (knownOffsets.Contains(adjusted)) {
        return adjusted;
      }
    }

    return offset; // No known instruction found — return original.
  }
}

/// <summary>
/// Parameters for IP skid correction.
/// </summary>
/// <param name="AdjustIncrement">Bytes to step backward per attempt (1 for x86, 4 for ARM).</param>
/// <param name="MaxAdjust">Maximum total bytes to walk backward.</param>
/// <param name="InitialMultiplier">Starting multiplier (typically 1).</param>
public readonly record struct SkidCorrectionParams(int AdjustIncrement, int MaxAdjust, int InitialMultiplier);
