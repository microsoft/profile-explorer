// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.ASM;

public enum ARMOpcode {
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

public struct ARMOpcodeInfo {
  public ARMOpcode Opcode { get; set; }
  public InstructionKind Kind { get; set; }

  public ARMOpcodeInfo(ARMOpcode opcode, InstructionKind kind) {
    Opcode = opcode;
    Kind = kind;
  }
}

public static class ARMOpcodes {
  private static readonly Dictionary<string, ARMOpcodeInfo> opcodes_ =
    new Dictionary<string, ARMOpcodeInfo> {
      {"B", new ARMOpcodeInfo(ARMOpcode.B, InstructionKind.Goto)},
      {"BR", new ARMOpcodeInfo(ARMOpcode.BR, InstructionKind.Goto)},
      {"RET", new ARMOpcodeInfo(ARMOpcode.RET, InstructionKind.Return)},
      {"BEQ", new ARMOpcodeInfo(ARMOpcode.BEQ, InstructionKind.Branch)},
      {"BNE", new ARMOpcodeInfo(ARMOpcode.BNE, InstructionKind.Branch)},
      {"BCS", new ARMOpcodeInfo(ARMOpcode.BCS, InstructionKind.Branch)},
      {"BHS", new ARMOpcodeInfo(ARMOpcode.BHS, InstructionKind.Branch)},
      {"BCC", new ARMOpcodeInfo(ARMOpcode.BCC, InstructionKind.Branch)},
      {"BLO", new ARMOpcodeInfo(ARMOpcode.BLO, InstructionKind.Branch)},
      {"BMI", new ARMOpcodeInfo(ARMOpcode.BMI, InstructionKind.Branch)},
      {"BPL", new ARMOpcodeInfo(ARMOpcode.BPL, InstructionKind.Branch)},
      {"BVS", new ARMOpcodeInfo(ARMOpcode.BVS, InstructionKind.Branch)},
      {"BVC", new ARMOpcodeInfo(ARMOpcode.BVC, InstructionKind.Branch)},
      {"BHI", new ARMOpcodeInfo(ARMOpcode.BHI, InstructionKind.Branch)},
      {"BLS", new ARMOpcodeInfo(ARMOpcode.BLS, InstructionKind.Branch)},
      {"BGE", new ARMOpcodeInfo(ARMOpcode.BGE, InstructionKind.Branch)},
      {"BLT", new ARMOpcodeInfo(ARMOpcode.BLT, InstructionKind.Branch)},
      {"BGT", new ARMOpcodeInfo(ARMOpcode.BGT, InstructionKind.Branch)},
      {"BLE", new ARMOpcodeInfo(ARMOpcode.BLE, InstructionKind.Branch)},
      {"BAL", new ARMOpcodeInfo(ARMOpcode.BAL, InstructionKind.Branch)},
      {"TBZ", new ARMOpcodeInfo(ARMOpcode.TBZ, InstructionKind.Branch)},
      {"TBNZ", new ARMOpcodeInfo(ARMOpcode.TBNZ, InstructionKind.Branch)},
      {"CBZ", new ARMOpcodeInfo(ARMOpcode.CBZ, InstructionKind.Branch)},
      {"CBNZ", new ARMOpcodeInfo(ARMOpcode.CBNZ, InstructionKind.Branch)},
      {"BL", new ARMOpcodeInfo(ARMOpcode.BL, InstructionKind.Call)},
      {"BLR", new ARMOpcodeInfo(ARMOpcode.BLR, InstructionKind.Call)},
      {"NOP", new ARMOpcodeInfo(ARMOpcode.NOP, InstructionKind.Other)}
    };

  private static readonly StringTrie<ARMOpcodeInfo> opcodesTrie_ = new StringTrie<ARMOpcodeInfo>(opcodes_);

  public static bool GetOpcodeInfo(string value, out ARMOpcodeInfo info) {
    return opcodesTrie_.TryGetValue(value, out info, true);
  }

  //? TODO: Needs a TryGetValueUpper that does the value.ToUpper() on each letter
  public static bool GetOpcodeInfo(ReadOnlyMemory<char> value, out ARMOpcodeInfo info) {
    return opcodesTrie_.TryGetValue(value, out info, true);
  }

  public static bool IsOpcode(string value) {
    return GetOpcodeInfo(value, out _);
  }

  public static bool IsOpcode(ReadOnlyMemory<char> value) {
    return opcodesTrie_.Contains(value);
  }
}
