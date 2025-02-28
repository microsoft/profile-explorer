﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.IR.Tags;

namespace ProfileExplorer.UI;

public class FunctionCodeStatistics {
  public long Size { get; set; }
  public int Instructions { get; set; }
  public int Loads { get; set; }
  public int Stores { get; set; }
  public int Branches { get; set; }
  public int Calls { get; set; }
  public int Callers { get; set; }
  public int IndirectCalls { get; set; }
  public int Callees { get; set; }
  public int OpcodeHash { get; set; }

  public static FunctionCodeStatistics Compute(FunctionIR function, ICompilerIRInfo irInfo) {
    var stats = new FunctionCodeStatistics();
    var metadataTag = function.GetTag<AssemblyMetadataTag>();

    if (metadataTag != null) {
      stats.Size = metadataTag.FunctionSize;
    }

    foreach (var instr in function.AllInstructions) {
      stats.Instructions++;

      //? TODO: Add hash for branch targets, other diff registers, etc - configurable with options
      if (instr.Opcode != null) {
        stats.OpcodeHash = HashCode.Combine(stats.OpcodeHash, instr.Opcode.GetHashCode());
      }
      else if (!instr.OpcodeText.IsEmpty) {
        stats.OpcodeHash = HashCode.Combine(stats.OpcodeHash, instr.OpcodeText.ToString().GetHashCode());
      }

      if (instr.IsBranch || instr.IsGoto || instr.IsSwitch) {
        stats.Branches++;
      }

      if (irInfo.IsLoadInstruction(instr)) {
        stats.Loads++;
      }

      if (irInfo.IsStoreInstruction(instr)) {
        stats.Stores++;
      }

      if (irInfo.IsCallInstruction(instr)) {
        if (irInfo.GetCallTarget(instr) == null) {
          stats.IndirectCalls++;
        }

        stats.Calls++;
      }
    }

    return stats;
  }

  public bool ComputeDiff(FunctionCodeStatistics other) {
    Size = other.Size - Size;
    Instructions = other.Instructions - Instructions;
    Loads = other.Loads - Loads;
    Stores = other.Stores - Stores;
    Branches = other.Branches - Branches;
    Calls = other.Calls - Calls;
    Callers = other.Callers - Callers;
    IndirectCalls = other.IndirectCalls - IndirectCalls;
    Callees = other.Callees - Callees;
    return Size != 0 || Instructions != 0 ||
           Loads != 0 || Stores != 0 ||
           Branches != 0 || Calls != 0 ||
           Callers != 0 || IndirectCalls != 0 || Callees != 0;
  }

  public void Add(FunctionCodeStatistics other) {
    Size = other.Size + Size;
    Instructions = other.Instructions + Instructions;
    Loads = other.Loads + Loads;
    Stores = other.Stores + Stores;
    Branches = other.Branches + Branches;
    Calls = other.Calls + Calls;
    Callers = other.Callers + Callers;
    IndirectCalls = other.IndirectCalls + IndirectCalls;
    Callees = other.Callees + Callees;
    OpcodeHash = HashCode.Combine(OpcodeHash, other.OpcodeHash);
  }

  public override string ToString() {
    return $"Size: {Size}\n" +
           $"Instructions: {Instructions}\n" +
           $"Loads: {Loads}\n" +
           $"Stores: {Stores}\n" +
           $"Branches: {Branches}\n" +
           $"Calls: {Calls}\n" +
           $"Callers: {Callers}\n" +
           $"IndirectCalls: {IndirectCalls}\n" +
           $"Callees: {Callees}";
  }
}