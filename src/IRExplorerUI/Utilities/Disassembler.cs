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
using IRExplorerUI.Compilers;
using Microsoft.Win32.SafeHandles;
using Microsoft.Windows.EventTracing.Metadata;

namespace IRExplorerUI.Utilities {
    public class Disassembler : IDisposable {
        class Interop {
            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct Instruction {
                public int Id;
                public long Address;
                public short Size;
                public fixed byte Bytes[16];
                public fixed byte Mnemonic[32];
                public fixed byte Operand[160];
                public IntPtr Details;

                public unsafe byte[] BytesArray {
                    get {
                        fixed (byte* pinned = Bytes) {
                            var bytes = new byte[16];
                            Marshal.Copy((IntPtr)pinned, bytes, 0, Size);
                            return bytes;
                        }
                    }
                }

                public unsafe string MnemonicString {
                    get {
                        fixed (byte* pinned = Mnemonic) {
                            return Marshal.PtrToStringAnsi((IntPtr)pinned);
                        }
                    }
                }

                public unsafe string OperandString {
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

        private byte[] data_;
        private long dataStartRVA_;
        private long baseAddress_;
        private Machine architecture_;
        private Interop.DisassemblerHandle disasmHandle_;

        public static Disassembler CreateForBinary(string binaryFilePath) {
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
                                        binaryInfo.ImageBase);
            }
            
            return null;
        }

        public Disassembler(Machine architecture, byte[] data, long dataStartRva = 0, long baseAddress = 0) {
            architecture_ = architecture;
            data_ = data;
            dataStartRVA_ = dataStartRva;
            baseAddress_ = baseAddress;
            disasmHandle_ = Interop.Create(architecture);
        }


        public string DisassembleToText(DebugFunctionInfo funcInfo) {
            return DisassembleToText(funcInfo.StartRVA, funcInfo.Size);
        }

       
        public unsafe string DisassembleToText(long startRVA, long size) {
            var builder = new StringBuilder();

#if false
            // 2.61s
            var disasm = CapstoneDisassembler.CreateArm64Disassembler(Arm64DisassembleMode.Arm);
            disasm.DisassembleMode
            //SetDisassemblerOption(Interop.NativeDisassemblerOptionType.SetSyntax, Interop.NativeDisassemblerOptionValue.UseIntelSyntax);

            long offset = startRVA - dataStartRVA_;
            var data = new byte[size];

            for (long i = 0; i < size; i++) {
                data[i] = data_[i + offset];
            }

            long t = 0;

            for (int k = 0; k < 1000 *10; k++) {

                foreach (var instr in disasm.Iterate(data, offset)) {
                    var address = instr.Address;
                    var mnemonic = instr.Mnemonic;
                    var operand = instr.Operand;
                    //builder.AppendFormat("{0:X}: \t {1} \t {2}\n", address, mnemonic, operand);
                    t += address + mnemonic.Length + operand.Length;
                }
            }

            builder.AppendFormat("{0}", t);
#else
            //SetDisassemblerOption(Interop.DisassemblerOptionType.SetInstructionDetails, Interop.DisassemblerOptionValue.Enable);
            long t = 0;

            for (int k = 0; k < 1000 *10; k++) {

                foreach (var instr in DisassembleInstructions(startRVA, size, baseAddress_ + startRVA)) {
                    var address = instr.Address;
                    var s = instr.MnemonicString;
                    var o = instr.OperandString;
                    builder.AppendFormat("{0:X}: \t {1} \t {2}\n", address, s, o);
                    t += address + s.Length + o.Length;
                }
            }

            builder.AppendFormat("{0}", t);
#endif

            return builder.ToString();
        }

        private void SetDisassemblerOption(Interop.DisassemblerOptionType option, Interop.DisassemblerOptionValue value) {
            Interop.SetDisassemblerOption(disasmHandle_, option, (IntPtr)value);
        }

        private IEnumerable<Interop.Instruction> DisassembleInstructions(long startRVA, long size, long startAddress) {
            using var instrBuffer = Interop.AllocateInstruction(disasmHandle_);
            long offset = startRVA - dataStartRVA_;

            if (offset < 0 || (offset + size) > data_.Length) {
                Trace.WriteLine($"Invalid disassembler offset/size {offset}/{size} for image of length {data_.Length}");
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
    }
}
