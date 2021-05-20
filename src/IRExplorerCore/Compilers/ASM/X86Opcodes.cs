// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.ASM {
    public enum x86Opcode {
        RET,
        JMP,
        JO,
        JNO,
        JS,
        JNS,
        JE,
        JZ,
        JNE,
        JNZ,
        JB,
        JNAE,
        JC,
        JNB,
        JAE,
        JNC,
        JBE,
        JNA,
        JA,
        JNBE,
        JL,
        JNGE,
        JGE,
        JNL,
        JLE,
        JNG,
        JG,
        JNLE,
        JP,
        JPE,
        JNP,
        JPO,
        JCXZ,
        JECXZ,
        CALL,
    }

    public struct x86OpcodeInfo {
        public x86Opcode Opcode { get; set; }
        public InstructionKind Kind { get; set; }

        public x86OpcodeInfo(x86Opcode opcode, InstructionKind kind) {
            Opcode = opcode;
            Kind = kind;
        }
    }

    public static class x86Opcodes {
        private static readonly Dictionary<string, x86OpcodeInfo> opcodes_ =
            new Dictionary<string, x86OpcodeInfo> {
                {"JMP", new x86OpcodeInfo(x86Opcode.JMP, InstructionKind.Goto)},
                {"RET", new x86OpcodeInfo(x86Opcode.RET, InstructionKind.Return)},
                {"JO", new x86OpcodeInfo(x86Opcode.JO, InstructionKind.Branch)},
                {"JNO", new x86OpcodeInfo(x86Opcode.JNO, InstructionKind.Branch)},
                {"JS", new x86OpcodeInfo(x86Opcode.JS, InstructionKind.Branch)},
                {"JNS", new x86OpcodeInfo(x86Opcode.JNS, InstructionKind.Branch)},
                {"JE", new x86OpcodeInfo(x86Opcode.JE, InstructionKind.Branch)},
                {"JZ", new x86OpcodeInfo(x86Opcode.JZ, InstructionKind.Branch)},
                {"JNE", new x86OpcodeInfo(x86Opcode.JNE, InstructionKind.Branch)},
                {"JNZ", new x86OpcodeInfo(x86Opcode.JNZ, InstructionKind.Branch)},
                {"JB", new x86OpcodeInfo(x86Opcode.JB, InstructionKind.Branch)},
                {"JNAE", new x86OpcodeInfo(x86Opcode.JNAE, InstructionKind.Branch)},
                {"JC", new x86OpcodeInfo(x86Opcode.JC, InstructionKind.Branch)},
                {"JNB", new x86OpcodeInfo(x86Opcode.JNB, InstructionKind.Branch)},
                {"JAE", new x86OpcodeInfo(x86Opcode.JAE, InstructionKind.Branch)},
                {"JNC", new x86OpcodeInfo(x86Opcode.JNC, InstructionKind.Branch)},
                {"JBE", new x86OpcodeInfo(x86Opcode.JBE, InstructionKind.Branch)},
                {"JNA", new x86OpcodeInfo(x86Opcode.JNA, InstructionKind.Branch)},
                {"JA", new x86OpcodeInfo(x86Opcode.JA, InstructionKind.Branch)},
                {"JNBE", new x86OpcodeInfo(x86Opcode.JNBE, InstructionKind.Branch)},
                {"JL", new x86OpcodeInfo(x86Opcode.JL, InstructionKind.Branch)},
                {"JNGE", new x86OpcodeInfo(x86Opcode.JNGE, InstructionKind.Branch)},
                {"JGE", new x86OpcodeInfo(x86Opcode.JGE, InstructionKind.Branch)},
                {"JNL", new x86OpcodeInfo(x86Opcode.JNL, InstructionKind.Branch)},
                {"JLE", new x86OpcodeInfo(x86Opcode.JLE, InstructionKind.Branch)},
                {"JNG", new x86OpcodeInfo(x86Opcode.JNG, InstructionKind.Branch)},
                {"JG", new x86OpcodeInfo(x86Opcode.JG, InstructionKind.Branch)},
                {"JNLE", new x86OpcodeInfo(x86Opcode.JNLE, InstructionKind.Branch)},
                {"JP", new x86OpcodeInfo(x86Opcode.JP, InstructionKind.Branch)},
                {"JPE", new x86OpcodeInfo(x86Opcode.JPE, InstructionKind.Branch)},
                {"JNP", new x86OpcodeInfo(x86Opcode.JNP, InstructionKind.Branch)},
                {"JPO", new x86OpcodeInfo(x86Opcode.JPO, InstructionKind.Branch)},
                {"JCXZ", new x86OpcodeInfo(x86Opcode.JCXZ, InstructionKind.Branch)},
                {"JECXZ", new x86OpcodeInfo(x86Opcode.JECXZ, InstructionKind.Branch)},
                {"CALL", new x86OpcodeInfo(x86Opcode.CALL, InstructionKind.Call)},
            };

        private static readonly StringTrie<x86OpcodeInfo> opcodesTrie_ = new StringTrie<x86OpcodeInfo>(opcodes_);

        public static bool GetOpcodeInfo(string value, out x86OpcodeInfo info) {
            return opcodes_.TryGetValue(value, out info);
        }

        //? TODO: Needs a TryGetValueUpper that does the value.ToUpper() on each letter
        public static bool GetOpcodeInfo(ReadOnlyMemory<char> value, out x86OpcodeInfo info) {
            return opcodesTrie_.TryGetValue(value, out info);
        }

        public static bool IsOpcode(string value) {
            return GetOpcodeInfo(value, out _);
        }

        public static bool IsOpcode(ReadOnlyMemory<char> value) {
            return opcodesTrie_.Contains(value);
        }
    }
}
