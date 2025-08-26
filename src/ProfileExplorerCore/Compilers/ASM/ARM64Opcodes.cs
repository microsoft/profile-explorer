// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProfileExplorerCore.Collections;
using ProfileExplorerCore.IR;

namespace ProfileExplorerCore.Compilers.ASM;

public enum ARM64Opcode {
  RET,
  BL,
  BLR,
  B,
  BR,
  BEQ,
  BNE,
  BCS,
  BHS,
  BCC,
  BLO,
  BMI,
  BPL,
  BVS,
  BVC,
  BHI,
  BLS,
  BGE,
  BLT,
  BGT,
  BLE,
  BAL,
  TBZ,
  TBNZ,
  CBZ,
  CBNZ,
  NOP
}

public struct ARM64OpcodeInfo {
  public ARM64Opcode Opcode { get; set; }
  public InstructionKind Kind { get; set; }

  public ARM64OpcodeInfo(ARM64Opcode opcode, InstructionKind kind) {
    Opcode = opcode;
    Kind = kind;
  }
}

public static class ARM64Opcodes {
  private static readonly Dictionary<string, ARM64OpcodeInfo> opcodes_ =
    new() {
      {"B", new ARM64OpcodeInfo(ARM64Opcode.B, InstructionKind.Goto)},
      {"BR", new ARM64OpcodeInfo(ARM64Opcode.BR, InstructionKind.Goto)},
      {"RET", new ARM64OpcodeInfo(ARM64Opcode.RET, InstructionKind.Return)},
      {"BEQ", new ARM64OpcodeInfo(ARM64Opcode.BEQ, InstructionKind.Branch)},
      {"BNE", new ARM64OpcodeInfo(ARM64Opcode.BNE, InstructionKind.Branch)},
      {"BCS", new ARM64OpcodeInfo(ARM64Opcode.BCS, InstructionKind.Branch)},
      {"BHS", new ARM64OpcodeInfo(ARM64Opcode.BHS, InstructionKind.Branch)},
      {"BCC", new ARM64OpcodeInfo(ARM64Opcode.BCC, InstructionKind.Branch)},
      {"BLO", new ARM64OpcodeInfo(ARM64Opcode.BLO, InstructionKind.Branch)},
      {"BMI", new ARM64OpcodeInfo(ARM64Opcode.BMI, InstructionKind.Branch)},
      {"BPL", new ARM64OpcodeInfo(ARM64Opcode.BPL, InstructionKind.Branch)},
      {"BVS", new ARM64OpcodeInfo(ARM64Opcode.BVS, InstructionKind.Branch)},
      {"BVC", new ARM64OpcodeInfo(ARM64Opcode.BVC, InstructionKind.Branch)},
      {"BHI", new ARM64OpcodeInfo(ARM64Opcode.BHI, InstructionKind.Branch)},
      {"BLS", new ARM64OpcodeInfo(ARM64Opcode.BLS, InstructionKind.Branch)},
      {"BGE", new ARM64OpcodeInfo(ARM64Opcode.BGE, InstructionKind.Branch)},
      {"BLT", new ARM64OpcodeInfo(ARM64Opcode.BLT, InstructionKind.Branch)},
      {"BGT", new ARM64OpcodeInfo(ARM64Opcode.BGT, InstructionKind.Branch)},
      {"BLE", new ARM64OpcodeInfo(ARM64Opcode.BLE, InstructionKind.Branch)},
      {"BAL", new ARM64OpcodeInfo(ARM64Opcode.BAL, InstructionKind.Branch)},
      {"TBZ", new ARM64OpcodeInfo(ARM64Opcode.TBZ, InstructionKind.Branch)},
      {"TBNZ", new ARM64OpcodeInfo(ARM64Opcode.TBNZ, InstructionKind.Branch)},
      {"CBZ", new ARM64OpcodeInfo(ARM64Opcode.CBZ, InstructionKind.Branch)},
      {"CBNZ", new ARM64OpcodeInfo(ARM64Opcode.CBNZ, InstructionKind.Branch)},
      {"BL", new ARM64OpcodeInfo(ARM64Opcode.BL, InstructionKind.Call)},
      {"BLR", new ARM64OpcodeInfo(ARM64Opcode.BLR, InstructionKind.Call)},
      {"NOP", new ARM64OpcodeInfo(ARM64Opcode.NOP, InstructionKind.Other)}
    };
  private static readonly StringTrie<ARM64OpcodeInfo> opcodesTrie_ = new(opcodes_);

  public static bool GetOpcodeInfo(string value, out ARM64OpcodeInfo info) {
    return opcodesTrie_.TryGetValue(value, out info, true);
  }

  //? TODO: Needs a TryGetValueUpper that does the value.ToUpper() on each letter
  public static bool GetOpcodeInfo(ReadOnlyMemory<char> value, out ARM64OpcodeInfo info) {
    return opcodesTrie_.TryGetValue(value, out info, true);
  }

  public static bool IsOpcode(string value) {
    return GetOpcodeInfo(value, out _);
  }

  public static bool IsOpcode(ReadOnlyMemory<char> value) {
    return opcodesTrie_.Contains(value);
  }
}