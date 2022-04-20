﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using IntervalTree;
using IRExplorerCore;
using IRExplorerCore.ASM;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers;
using Microsoft.Win32.SafeHandles;

namespace IRExplorerUI.Compilers.ASM {
    public class DisassemblerOptions {
        public bool IncludeBytes { get; set; }

    }

    public class Disassembler : IDisposable {
        private List<(byte[] Data, long StartRVA)> codeSectionData_;
        private long baseAddress_;
        private Machine architecture_;
        private IDebugInfoProvider debugInfo_;
        private Interop.DisassemblerHandle disasmHandle_;
        private IntervalTree<long, DebugFunctionInfo> functionRvaTree_;
        private bool hasExternalSyms_;

        public static Disassembler CreateForBinary(string binaryFilePath, IDebugInfoProvider debugInfo) {
            using var peInfo = new PEBinaryInfoProvider(binaryFilePath);

            if (!peInfo.Initialize()) {
                return null;
            }

            var codeSections = peInfo.CodeSectionHeaders;
            var codeSectionData = new List<(byte[] Data, long StartRVA)>();

            foreach (var section in codeSections) {
                codeSectionData.Add((peInfo.GetSectionData(section), section.VirtualAddress));
            }

            var binaryInfo = peInfo.BinaryFileInfo;
            return new Disassembler(binaryInfo.Architecture, codeSectionData,
                                    binaryInfo.ImageBase,
                                    debugInfo);
        }

        private Disassembler(Machine architecture, List<(byte[] Data, long StartRVA)> codeSectionData, long baseAddress = 0,
            IDebugInfoProvider debugInfo = null) {
            codeSectionData_ = codeSectionData;
            architecture_ = architecture;
            baseAddress_ = baseAddress;
            debugInfo_ = debugInfo;
            Initialize();
        }

        public Disassembler(Machine architecture, byte[] data, long dataStartRva = 0, long baseAddress = 0,
                            IDebugInfoProvider debugInfo = null) {
            architecture_ = architecture;
            baseAddress_ = baseAddress;
            debugInfo_ = debugInfo;
            codeSectionData_ = new List<(byte[] Data, long StartRVA)>();
            codeSectionData_.Add((data, dataStartRva));
            Initialize();
        }

        private void Initialize() {
            disasmHandle_ = Interop.Create(architecture_);
            BuildFunctionRvaTree(false);
        }

        public string DisassembleToText(DebugFunctionInfo funcInfo) {
            return DisassembleToText(funcInfo.StartRVA, funcInfo.Size);
        }

        public string DisassembleToText(long startRVA, long size) {
            if (startRVA == 0 || size == 0) {
                return "";
            }

            var builder = new StringBuilder((int)(size / 4) + 1);

            try {
                foreach (var instr in DisassembleInstructions(startRVA, size, baseAddress_ + startRVA)) {
                    var addressString = $"{instr.Address:X}:    ";
                    builder.Append(addressString);
                    int startIndex = 0;
                    bool appendBytes = false; //? TODO: Use option
                    //? TODO: Also adjust column of editor line numbers based on this

                    if (appendBytes) {
                        startIndex += AppendBytes(instr, startIndex, builder);
                        builder.Append("  ");
                    }

                    AppendMnemonic(instr, builder);
                    builder.Append("  ");

                    AppendOperands(instr, startRVA, size, builder);
                    builder.AppendLine();

                    if (appendBytes) {
                        // For longer instructions, append up to 6 bytes per line.
                        while (startIndex < instr.Size) {
                            builder.Append(' ', addressString.Length); // Align right.
                            startIndex += AppendBytes(instr, startIndex, builder);
                            builder.AppendLine();
                        }
                    }
                }
            }
            catch (Exception ex) {
#if DEBUG
                Trace.TraceError($"Failed to disassemble code at RVA {startRVA}, size {size}");
#endif
                return "";
            }

            return builder.ToString();
        }

        private unsafe void AppendMnemonic(Interop.Instruction instr, StringBuilder builder) {
            byte* letterPtr = instr.Mnemonic;
            int index = 0;

            while (index < Interop.Instruction.MnemonicLength && letterPtr[index] != 0) {
                builder.Append((char)letterPtr[index]);
                index++;
            }
        }

        private unsafe void AppendOperands(Interop.Instruction instr, long startRVA, long size, StringBuilder builder) {
            bool isArm = architecture_ == Machine.Arm || architecture_ == Machine.Arm64;
            bool isJump = false;
            bool sawBracket = false;
            bool lookupName = ShouldLookupAddressByName(instr, ref isJump);

            byte* letterPtr = instr.Operand;
            int index = 0;

            while (index < Interop.Instruction.OperandLength && letterPtr[index] != 0) {
                char letter = (char)letterPtr[index];

                if (lookupName) {
                    // Try to replace a call target address by the function name.
                    int hexLength = 0;
                    bool skippedSharp = false;
                    long hexValue = 0;

                    if (letter == '[') {
                        sawBracket = true; // Reject lookups for call ptr [rip + 0xABCD] and similar.
                    }
                    else if (letter == '#' && isArm && !sawBracket) {
                        hexLength = FindHexNumber(letterPtr, index + 1, out hexValue); // Skip over #
                        skippedSharp = true;
                    }
                    else if (letter == '0' && !sawBracket) {
                        hexLength = FindHexNumber(letterPtr, index, out hexValue);
                    }

                    if (IsValidCallAddress(hexLength, hexValue)) {
                        long rva = hexValue - baseAddress_;
                        bool replaced = false;

                        // For jumps, use the name only if it's to another function.
                        if (!isJump || rva < startRVA || rva >= startRVA + size) {
                            replaced = TryAppendFunctionName(builder, rva);
                        }

                        if (!replaced) {
                            if (skippedSharp)
                                builder.Append('#');
                            builder.Append($"0x{hexValue:X}");
                        }

                        index += hexLength + (skippedSharp ? 1 : 0);
                        continue;
                    }
                }

                builder.Append(letter);
                index++;
            }
        }

        private bool IsValidCallAddress(int hexLength, long hexValue) {
            long rva = hexValue - baseAddress_;
            return FindCodeSection(rva).Data != null;
        }

        private bool TryAppendFunctionName(StringBuilder builder, long rva) {
            var func = FindFunctionByRva(rva);

            if (!func.IsUnknown) {
                builder.Append(func.Name);
                return true;
            }

            return false;
        }

        private DebugFunctionInfo FindFunctionByRva(long rva) {
            // Rebuild RVA tree with all symbol info when symbol name lookup is done.
            if (!hasExternalSyms_) {
                BuildFunctionRvaTree(true);
            }

            var functs = functionRvaTree_.Query(rva);
            foreach (var func in functs) {
                return func;
            }

            return debugInfo_.FindFunctionByRVA(rva);
        }

        private void BuildFunctionRvaTree(bool includeExternalSyms) {
            // Cache RVA -> function mapping, much faster to query.
            functionRvaTree_ = new IntervalTree<long, DebugFunctionInfo>();
            hasExternalSyms_ = includeExternalSyms;

            if (debugInfo_ == null) {
                return;
            }

            foreach (var func in debugInfo_.EnumerateFunctions(includeExternalSyms)) {
                if (func.Size > 0) {
                    functionRvaTree_.Add(func.StartRVA, func.EndRVA, func);
                }
            }

            // Force a query to ensure the tree is built on this thread
            // and all future queries are read-only.
            functionRvaTree_.Query(0);
        }

        private bool ShouldLookupAddressByName(Interop.Instruction instr, ref bool isJump) {
            if (debugInfo_ == null) {
                return false;
            }

            switch (architecture_) {
                case Machine.I386:
                case Machine.Amd64: {
                    if (x86Opcodes.GetOpcodeInfo(instr.MnemonicString, out var info)) {
                        isJump = info.Kind == InstructionKind.Goto;
                        return info.Kind == InstructionKind.Call || isJump;
                    }

                    return false;
                }
                case Machine.Arm:
                case Machine.Arm64: {
                    if (ARMOpcodes.GetOpcodeInfo(instr.MnemonicString, out var info)) {
                        isJump = info.Kind == InstructionKind.Goto;
                        return info.Kind == InstructionKind.Call || isJump;
                    }

                    return false;
                }
            }

            return false;
        }

        private unsafe int FindHexNumber(byte* letterPtr, int index, out long value) {
            // Expect star with 0x and skip.
            if (letterPtr[index] != '0' ||
                index + 1 >= Interop.Instruction.OperandLength ||
                !(letterPtr[index + 1] == 'x' || letterPtr[index + 1] == 'X')) {
                value = 0;
                return 0;
            }

            int startIndex = index;
            index += 2;
            value = 0;

            while (index < Interop.Instruction.OperandLength && letterPtr[index] != 0) {
                char c = (char)letterPtr[index];

                if (c >= '0' && c <= '9') {
                    value = value << 4 | c - '0';
                    index++;
                }
                else if (c >= 'a' && c <= 'f') {
                    value = value << 4 | 10 + (c - 'a');
                    index++;
                }
                else if (c >= 'A' && c <= 'F') {
                    value = value << 4 | 10 + (c - 'A');
                    index++;
                }
                else
                    break;
            }

            int length = index - startIndex;
            return length > 3 ? length : 0;
        }

        private unsafe int AppendBytes(Interop.Instruction instr, int startIndex, StringBuilder builder) {
            // Append at most 6 bytes per line.
            int count = Math.Min(6, instr.Size - startIndex);
            byte* bytes = instr.Bytes;

            switch (count) {
                case 0: {
                    return 0;
                }
                case 1: {
                    builder.Append($"{bytes[0]:X02}               ");
                    break;
                }
                case 2: {
                    builder.Append($"{bytes[0]:X02} {bytes[1]:X02}            ");
                    break;
                }
                case 3: {
                    builder.Append($"{bytes[0]:X02} {bytes[1]:X02} {bytes[2]:X02}         ");
                    break;
                }
                case 4: {
                    builder.Append($"{bytes[0]:X02} {bytes[1]:X02} {bytes[2]:X02} {bytes[3]:X02}      ");
                    break;
                }
                case 5: {
                    builder.Append($"{bytes[0]:X02} {bytes[1]:X02} {bytes[2]:X02} {bytes[3]:X02} {bytes[4]:X02}   ");
                    break;
                }
                case 6: {
                    builder.Append($"{bytes[0]:X02} {bytes[1]:X02} {bytes[2]:X02} {bytes[3]:X02} {bytes[4]:X02} {bytes[5]:X02}");
                    break;
                }
            }

            return count;
        }

        private void SetDisassemblerOption(Interop.DisassemblerOptionType option, Interop.DisassemblerOptionValue value) {
            Interop.SetDisassemblerOption(disasmHandle_, option, (IntPtr)value);
        }

        private (byte[] Data, long StartRVA) FindCodeSection(long rva) {
            foreach (var section in codeSectionData_) {
                if (rva >= section.StartRVA && rva < section.StartRVA + section.Data.Length) {
                    return section;
                }
            }

            return (null, 0);
        }

        private IEnumerable<Interop.Instruction> DisassembleInstructions(long startRVA, long size, long startAddress) {
            var codeSection = FindCodeSection(startRVA);

            if (codeSection.Data == null) {
                Trace.WriteLine($"Invalid disassembler RVA/size {startRVA}/{size}");
                yield break;
            }

            // Allocate a buffer for storing the instruction,
            // gets reused during iteration.
            using var instrBuffer = Interop.AllocateInstruction(disasmHandle_);
            long offset = startRVA - codeSection.StartRVA;
            var dataBuffer = GCHandle.Alloc(codeSection.Data, GCHandleType.Pinned);
            var dataBufferPtr = dataBuffer.AddrOfPinnedObject();

            // Disassemble the entire range of the code data buffer.
            var dataIteratorPtr = (IntPtr)(dataBufferPtr.ToInt64() + offset);
            var dataEndPtr = (IntPtr)(dataBufferPtr.ToInt64() + offset + size);

            while (dataIteratorPtr.ToInt64() < dataEndPtr.ToInt64()) {
                var remainingLength = (IntPtr)(dataEndPtr.ToInt64() - dataIteratorPtr.ToInt64());

                // Handles one instruction at a time.
                // dataIteratorPtr is being incremented by the native API.
                if (Interop.Iterate(disasmHandle_, ref dataIteratorPtr, ref remainingLength, ref startAddress, instrBuffer)) {
                    var instrPtr = instrBuffer.DangerousGetHandle();
                    var instruction = (Interop.Instruction)Marshal.PtrToStructure(instrPtr, typeof(Interop.Instruction));
                    yield return instruction;
                }
                else {
                    yield break;
                }
            }

            dataBuffer.Free();
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing) {
            disasmHandle_?.Dispose();
            disasmHandle_ = null;

            if (disposing) {
                GC.SuppressFinalize(this);
            }
        }

        static class Interop {
            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct Instruction {
                public const int MnemonicLength = 32;
                public const int OperandLength = 160;

                public int Id;
                public long Address;
                public short Size;
                public fixed byte Bytes[16];
                public fixed byte Mnemonic[32];
                public fixed byte Operand[160];
                public IntPtr Details;

                public byte[] BytesArray {
                    get {
                        fixed (byte* pinned = Bytes) {
                            var bytes = new byte[Size];
                            Marshal.Copy((IntPtr)pinned, bytes, 0, Size);
                            return bytes;
                        }
                    }
                }

                public string MnemonicString {
                    get {
                        fixed (byte* pinned = Mnemonic) {
                            return Marshal.PtrToStringAnsi((IntPtr)pinned);
                        }
                    }
                }

                public string OperandString {
                    get {
                        fixed (byte* pinned = Operand) {
                            return Marshal.PtrToStringAnsi((IntPtr)pinned);
                        }
                    }
                }
            }

            public enum CapstoneResultCode {
                Ok = 0,
                OutOfMemory,
                UnsupportedDisassembleArchitecture,
                InvalidHandle1,
                InvalidHandle2,
                UnsupportedDisassembleMode,
                InvalidOption,
                UnsupportedInstructionDetail,
                UninitializedMemoryManagement,
                UnsupportedVersion,
                UnSupportedDietModeOperation,
                UnsupportedSkipDataModeOperation,
                UnSupportedX86AttSyntax,
                UnSupportedX86IntelSyntax,
                UnSupportedX86MasmSyntax
            }

            [Flags]
            public enum DisassembleMode {
                LittleEndian = 0,
                Arm = 0,
                Bit16 = 1 << 1,
                Bit32 = 1 << 2,
                Bit64 = 1 << 3,
                ArmThumb = 1 << 4,

                ArmCortexM = 1 << 5,

                ArmV8 = 1 << 6,

                MipsMicro = 1 << 4,

                Mips3 = 1 << 5,

                Mips32R6 = 1 << 6,

                Mips2 = 1 << 7,

                SparcV9 = 1 << 4,

                PowerPcQuadProcessingExtensions = 1 << 4,

                M68K000 = 1 << 1,

                M68K010 = 1 << 2,

                M68K020 = 1 << 3,

                M68K030 = 1 << 4,

                M68K040 = 1 << 5,

                M68K060 = 1 << 6,

                BigEndian = 1 << 31,

                Mips32 = Bit32,
                Mips64 = Bit64,
                M680X6301 = 1 << 1,
                M680X6309 = 1 << 2,
                M680X6800 = 1 << 3,
                M680X6801 = 1 << 4,
                M680X6805 = 1 << 5,
                M680X6808 = 1 << 6,
                M680X6809 = 1 << 7,
                M680X6811 = 1 << 8,
                M680XCpu12 = 1 << 9,
                M680XHcS08 = 1 << 10
            }

            public class DisassemblerHandle : SafeHandleMinusOneIsInvalid {
                public DisassemblerHandle(IntPtr pDisassembler) : base(true) {
                    handle = pDisassembler;
                }

                protected override bool ReleaseHandle() {
                    var resultCode = CloseDisassembler(ref handle);
                    handle = IntPtr.Zero;
                    return resultCode == CapstoneResultCode.Ok;
                }
            }

            public class InstructionHandle : SafeHandleZeroOrMinusOneIsInvalid {
                public InstructionHandle(IntPtr pInstruction) : base(true) {
                    handle = pInstruction;
                }

                protected override bool ReleaseHandle() {
                    FreeInstructions(handle, (IntPtr)1);
                    handle = IntPtr.Zero;
                    return true;
                }
            }
            public enum DisassemblerOptionType {
                None = 0,
                SetSyntax,
                SetInstructionDetails,
                SetDisassembleMode,
                SetMemory,
                SetSkipData,
                SetSkipDataConfig,
                SetMnemonic,
                SetUnsigned
            }

            public enum DisassemblerOptionValue {
                Disable = 0,
                Enable = 3,
                UseDefaultSyntax = 0,
                UseIntelSyntax,
                UseAttSyntax,
                CS_OPT_SYNTAX_NOREGNAME,
                UseMasmSyntax
            }

            public enum Architecture {
                Arm,
                Arm64,
                Mips,
                X86,
                PowerPc,
                Sparc,
                SystemZ,
                XCore,
                M68K,
                Tms320C64X,
                M680X,
                Evm
            }

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_close")]
            public static extern CapstoneResultCode CloseDisassembler(ref IntPtr pDissembler);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_open")]
            public static extern CapstoneResultCode CreateDisassembler(Architecture architecture, DisassembleMode disassembleMode, ref IntPtr pDisassembler);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_malloc")]
            public static extern IntPtr CreateInstruction(DisassemblerHandle hDisassembler);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_disasm")]
            public static extern IntPtr Disassemble(DisassemblerHandle hDisassembler, IntPtr pCode, IntPtr codeSize, long startingAddress, IntPtr count, ref IntPtr pInstructions);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_free")]
            public static extern void FreeInstructions(IntPtr pInstructions, IntPtr count);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_regs_access")]
            public static extern CapstoneResultCode GetAccessedRegisters(DisassemblerHandle hDisassembler, InstructionHandle hInstruction, short[] readRegisters, ref byte readRegistersCount, short[] writtenRegisters, ref byte writtenRegistersCount);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_group_name")]
            public static extern IntPtr GetInstructionGroupName(DisassemblerHandle hDisassembler, int instructionGroupId);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_errno")]
            public static extern CapstoneResultCode GetLastErrorCode(DisassemblerHandle hDisassembler);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_reg_name")]
            public static extern IntPtr GetRegisterName(DisassemblerHandle hDisassembler, int registerId);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_version")]
            public static extern int GetVersion(ref int majorVersion, ref int minorVersion);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_disasm_iter")]
            [return: MarshalAs(UnmanagedType.I1)]
            public static extern bool Iterate(DisassemblerHandle hDisassembler, ref IntPtr pCode, ref IntPtr codeSize, ref long address, InstructionHandle hInstruction);

            [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi, EntryPoint = "LoadLibraryA", SetLastError = true)]
            public static extern IntPtr LoadLibrary(string libraryFilePath);

            [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_option")]
            public static extern CapstoneResultCode SetDisassemblerOption(DisassemblerHandle hDisassembler, DisassemblerOptionType optionType, IntPtr optionValue);

            public static DisassemblerHandle Create(Architecture architecture, DisassembleMode mode) {
                var disasmPtr = IntPtr.Zero;
                var resultCode = CreateDisassembler(architecture, mode, ref disasmPtr);

                if (resultCode == CapstoneResultCode.Ok) {
                    return new DisassemblerHandle(disasmPtr);
                }

                Trace.WriteLine($"Failed to create Capstone disassembler: {resultCode}");
                return null;
            }

            public static DisassemblerHandle Create(Machine architecture) {
                return architecture switch {
                    Machine.I386 => Create(Architecture.X86, DisassembleMode.Bit32),
                    Machine.Amd64 => Create(Architecture.X86, DisassembleMode.Bit64),
                    Machine.Arm => Create(Architecture.Arm, DisassembleMode.Arm),
                    Machine.Arm64 => Create(Architecture.Arm64, DisassembleMode.Arm),
                    _ => throw new NotSupportedException("Unsupported architecture!")
                };
            }

            public static InstructionHandle AllocateInstruction(DisassemblerHandle handle) {
                return new InstructionHandle(CreateInstruction(handle));
            }
        }

    }
}