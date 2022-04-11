using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AutoUpdaterDotNET;
using Gee.External.Capstone;
using Gee.External.Capstone.Arm64;
using Gee.External.Capstone.X86;
using Google.Protobuf.WellKnownTypes;
using IntervalTree;
using IRExplorerCore;
using IRExplorerCore.ASM;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers;
using Microsoft.Win32.SafeHandles;
using Microsoft.Windows.EventTracing.Metadata;
using Microsoft.Windows.EventTracing.Symbols;

namespace IRExplorerUI.Utilities {
    public class Disassembler : IDisposable {
        private byte[] data_;
        private long dataStartRVA_;
        private long baseAddress_;
        private Machine architecture_;
        private IDebugInfoProvider debugInfo_;
        private Interop.DisassemblerHandle disasmHandle_;
        private IntervalTree<long, DebugFunctionInfo> functionRvaTree_;

        public static Disassembler CreateForBinary(string binaryFilePath, IDebugInfoProvider debugInfo) {
            using var peInfo = new PEBinaryInfoProvider(binaryFilePath);

            if (!peInfo.Initialize()) {
                return null;
            }

            var textSection = peInfo.TextSectionHeader;

            if (textSection.HasValue) {
                var binaryInfo = peInfo.BinaryFileInfo;
                return new Disassembler(binaryInfo.Architecture,
                                        peInfo.GetSectionData(textSection.Value), 
                                        textSection.Value.VirtualAddress,
                                        binaryInfo.ImageBase,
                                        debugInfo);
            }
            
            return null;
        }

        public Disassembler(Machine architecture, byte[] data, long dataStartRva = 0, long baseAddress = 0,
                            IDebugInfoProvider debugInfo = null) {
            architecture_ = architecture;
            data_ = data;
            dataStartRVA_ = dataStartRva;
            baseAddress_ = baseAddress;
            debugInfo_ = debugInfo;
            disasmHandle_ = Interop.Create(architecture);
        }


        public string DisassembleToText(DebugFunctionInfo funcInfo) {
            return DisassembleToText(funcInfo.StartRVA, funcInfo.Size);
        }

        public string DisassembleToText(long startRVA, long size) {
            var builder = new StringBuilder((int)(size / 4) + 1);

            //SetDisassemblerOption(Interop.DisassemblerOptionType.SetInstructionDetails, Interop.DisassemblerOptionValue.Enable);

            try {
                foreach (var instr in DisassembleInstructions(startRVA, size, baseAddress_ + startRVA)) {
                    var addressString = $"{instr.Address:X}: ";
                    builder.Append(addressString);
                    int startIndex = 0;

                    unsafe {
                        startIndex += AppendBytes(instr, startIndex, builder);
                        builder.Append("  ");

                        AppendMnemonic(instr, builder);
                        builder.Append("  ");

                        AppendOperands(instr, startRVA, size, builder);
                        builder.AppendLine();
                    }

                    // For longer instructions, append up to 6 bytes per line.
                    while (startIndex < instr.Size) {
                        builder.Append(' ', addressString.Length); // Align right.
                        startIndex += AppendBytes(instr, startIndex, builder);
                        builder.AppendLine();
                    }
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to disassemble code at RVA {startRVA}, size {size}");
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
            byte* letterPtr = instr.Operand;
            bool isArm = architecture_ == Machine.Arm || architecture_ == Machine.Arm64;
            int index = 0;

            bool isJump = false;
            bool sawBracket = false;
            bool lookupName = ShouldLookupAddressByName(instr, ref isJump);

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
                        if (!isJump || rva < startRVA || rva >= (startRVA + size)) {
                            replaced = TryAppendFunctionName(builder, rva);
                        }

                        if(!replaced) {
                            if (skippedSharp) builder.Append('#');
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
            return hexLength > 0 && 
                   hexValue >= baseAddress_ && 
                   hexValue < (baseAddress_ + data_.Length);
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
            if(functionRvaTree_ == null) {
                // Cache RVA -> function mapping, much faster to query.
                functionRvaTree_ = new IntervalTree<long, DebugFunctionInfo>();

                foreach (var func in debugInfo_.EnumerateFunctions(true)) {
                    if (func.Size > 0) {
                        functionRvaTree_.Add(func.StartRVA, func.EndRVA, func);
                    }
                }
            }

            var functs = functionRvaTree_.Query(rva);
            foreach (var func in functs) {
                return func;
            }

            var funcInfo =  debugInfo_.FindFunctionByRVA(rva);

            if (!funcInfo.IsUnknown) {
                Trace.WriteLine($"=> Found for RVA {rva}: {funcInfo.Name}");
            }

            return funcInfo;
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
                (index + 1) >= Interop.Instruction.OperandLength ||
                !(letterPtr[index + 1] == 'x' || letterPtr[index + 1] == 'X')) {
                value = 0;
                return 0;
            }

            int startIndex = index;
            index += 2;
            value = 0;

            while (index < Interop.Instruction.OperandLength && letterPtr[index] != 0) {
                char c = (char)letterPtr[index];

                if ((c >= '0' && c <= '9')) {
                    value = (value << 4) | (c - '0');
                    index++;
                }
                else if (c >= 'a' && c <= 'f') {
                    value = (value << 4) | (10 + (c - 'a'));
                    index++;
                }
                else if (c >= 'A' && c <= 'F') {
                    value = (value << 4) | (10 + (c - 'A'));
                    index++;
                }
                else break;
            }

            int length = index - startIndex;
            return length > 3 ? length : 0;
        }

        private unsafe int AppendBytes(Interop.Instruction instr, int startIndex, StringBuilder builder) {
            // Append at most 6 bytes per line.
            int count =  Math.Min(6, instr.Size - startIndex);
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

        private IEnumerable<Interop.Instruction> DisassembleInstructions(long startRVA, long size, long startAddress) {
            using var instrBuffer = Interop.AllocateInstruction(disasmHandle_);
            long offset = startRVA - dataStartRVA_;

            if (offset < 0 || (offset + size) > data_.Length) {
                Trace.WriteLine($"Invalid disassembler offset/size {offset}/{size} for image of length {data_.Length}");
                yield break;
            }

            var dataBuffer = GCHandle.Alloc(data_, GCHandleType.Pinned);
            var dataBufferPtr = dataBuffer.AddrOfPinnedObject();

            IntPtr dataIteratorPtr = (IntPtr)(dataBufferPtr.ToInt64() + offset);
            IntPtr dataEndPtr = (IntPtr)(dataBufferPtr.ToInt64() + offset + size);

            while (dataIteratorPtr.ToInt64() < dataEndPtr.ToInt64()) {
                IntPtr remainingLength = (IntPtr)(dataEndPtr.ToInt64() - dataIteratorPtr.ToInt64());

                if (Interop.Iterate(disasmHandle_, ref dataIteratorPtr, ref remainingLength, ref startAddress, instrBuffer)) {
                    IntPtr instrPtr = instrBuffer.DangerousGetHandle();
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

        ~Disassembler() {
            Dispose(false);
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
                    this.handle = pDisassembler;
                }

                protected override bool ReleaseHandle() {
                    var resultCode = CloseDisassembler(ref this.handle);
                    this.handle = IntPtr.Zero;
                    return resultCode == CapstoneResultCode.Ok;
                }
            }

            public class InstructionHandle : SafeHandleZeroOrMinusOneIsInvalid {
                public InstructionHandle(IntPtr pInstruction) : base(true) {
                    this.handle = pInstruction;
                }

                protected override bool ReleaseHandle() {
                    FreeInstructions(this.handle, (IntPtr)1);
                    this.handle = IntPtr.Zero;
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
