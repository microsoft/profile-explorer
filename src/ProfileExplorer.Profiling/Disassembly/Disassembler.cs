// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Runtime.InteropServices;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling.Disassembly;

/// <summary>
/// x86/x64/ARM64 disassembler using the Capstone engine via P/Invoke.
/// </summary>
internal class Disassembler : IDisposable {
  private nint handle_;
  private bool isOpen_;
  private static bool? capstoneAvailable_;

  /// <summary>Whether capstone.dll is loadable on this system.</summary>
  public static bool CapstoneAvailable {
    get {
      capstoneAvailable_ ??= CheckCapstoneAvailable();
      return capstoneAvailable_.Value;
    }
  }

  private static bool CheckCapstoneAvailable() {
    try {
      // Try to call cs_open — if DLL is missing, this throws DllNotFoundException.
      int err = Interop.cs_open(Interop.CS_ARCH_X86, Interop.CS_MODE_64, out var testHandle);
      if (err == 0) Interop.cs_close(ref testHandle);
      return true;
    }
    catch (DllNotFoundException) { return false; }
    catch { return false; }
  }

  /// <summary>
  /// Disassemble a function from a binary file.
  /// </summary>
  /// <param name="binaryPath">Path to the PE binary file.</param>
  /// <param name="functionRva">RVA of the function start.</param>
  /// <param name="functionSize">Size of the function in bytes.</param>
  /// <param name="imageBase">Image base address from the PE header.</param>
  /// <param name="architecture">Target architecture (x86, x64, ARM64).</param>
  /// <param name="debugInfo">Optional debug info for resolving call targets.</param>
  /// <returns>List of disassembled instructions.</returns>
  public List<DisassembledInstruction> DisassembleFunction(
    string binaryPath,
    long functionRva,
    int functionSize,
    long imageBase,
    System.Reflection.ProcessorArchitecture architecture,
    IDebugInfoProvider? debugInfo = null) {
    var instructions = new List<DisassembledInstruction>();

    if (functionSize <= 0) return instructions;

    // Check if Capstone DLL is available.
    if (!CapstoneAvailable) return instructions;

    // Read the function bytes from the PE binary.
    byte[]? code = ReadFunctionBytes(binaryPath, functionRva, functionSize);
    if (code == null) return instructions;

    // Initialize Capstone.
    var (csArch, csMode) = GetCapstoneParams(architecture);
    if (!Open(csArch, csMode)) return instructions;

    try {
      long baseAddress = imageBase + functionRva;
      var codeHandle = GCHandle.Alloc(code, GCHandleType.Pinned);
      try {
        nint codePtr = codeHandle.AddrOfPinnedObject();
        nint codeSize = (nint)code.Length;
        long address = baseAddress;

        // Allocate instruction struct.
        nint insn = Interop.cs_malloc(handle_);
        if (insn == 0) return instructions;

        try {
          while (Interop.cs_disasm_iter(handle_, ref codePtr, ref codeSize, ref address, insn)) {
            string mnemonic = Marshal.PtrToStringAnsi(insn + Interop.InsnMnemonicOffset) ?? "";
            string opStr = Marshal.PtrToStringAnsi(insn + Interop.InsnOpStrOffset) ?? "";
            int size = Marshal.ReadInt16(insn + Interop.InsnSizeOffset);
            long instrAddress = Marshal.ReadInt64(insn + Interop.InsnAddressOffset);
            long instrRva = instrAddress - imageBase;

            string text = string.IsNullOrEmpty(opStr) ? mnemonic : $"{mnemonic}  {opStr}";

            // Resolve call/jump targets to function names.
            if (debugInfo != null && (mnemonic == "call" || mnemonic == "jmp") &&
                TryParseAddress(opStr, out long targetAddr)) {
              long targetRva = targetAddr - imageBase;
              var targetFunc = debugInfo.FindFunctionByRVA(targetRva);
              if (targetFunc != null) {
                text = $"{mnemonic}  {targetFunc.Name}";
              }
            }

            instructions.Add(new DisassembledInstruction(instrAddress, instrRva, text, size));
          }
        }
        finally {
          Interop.cs_free(insn, 1);
        }
      }
      finally {
        codeHandle.Free();
      }
    }
    finally {
      Close();
    }

    return instructions;
  }

  private static byte[]? ReadFunctionBytes(string binaryPath, long functionRva, int functionSize) {
    try {
      using var stream = File.OpenRead(binaryPath);
      using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);

      // Find the section containing the function RVA.
      foreach (var section in peReader.PEHeaders.SectionHeaders) {
        if (functionRva >= section.VirtualAddress &&
            functionRva < section.VirtualAddress + section.VirtualSize) {
          int offsetInSection = (int)(functionRva - section.VirtualAddress);
          int fileOffset = section.PointerToRawData + offsetInSection;

          int bytesToRead = Math.Min(functionSize, section.SizeOfRawData - offsetInSection);
          if (bytesToRead <= 0) return null;

          var data = new byte[bytesToRead];
          stream.Position = fileOffset;
          int read = stream.Read(data, 0, bytesToRead);
          return read == bytesToRead ? data : null;
        }
      }
    }
    catch {
      // Binary read failure.
    }

    return null;
  }

  private static (int arch, int mode) GetCapstoneParams(System.Reflection.ProcessorArchitecture architecture) {
    return architecture switch {
      System.Reflection.ProcessorArchitecture.X86 => (Interop.CS_ARCH_X86, Interop.CS_MODE_32),
      System.Reflection.ProcessorArchitecture.Arm => (Interop.CS_ARCH_ARM64, Interop.CS_MODE_ARM),
      _ => (Interop.CS_ARCH_X86, Interop.CS_MODE_64) // Default to x64
    };
  }

  private bool Open(int arch, int mode) {
    if (isOpen_) Close();
    int err = Interop.cs_open(arch, mode, out handle_);
    isOpen_ = err == 0;
    return isOpen_;
  }

  private void Close() {
    if (!isOpen_) return;
    Interop.cs_close(ref handle_);
    isOpen_ = false;
  }

  private static bool TryParseAddress(string operand, out long address) {
    operand = operand.Trim();
    if (operand.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
      return long.TryParse(operand[2..], System.Globalization.NumberStyles.HexNumber, null, out address);
    }

    return long.TryParse(operand, System.Globalization.NumberStyles.HexNumber, null, out address);
  }

  public void Dispose() {
    Close();
  }

  /// <summary>
  /// Capstone P/Invoke interop.
  /// </summary>
  private static class Interop {
    public const int CS_ARCH_X86 = 3;
    public const int CS_ARCH_ARM64 = 1;
    public const int CS_MODE_32 = 1 << 2;
    public const int CS_MODE_64 = 1 << 3;
    public const int CS_MODE_ARM = 0;

    // cs_insn struct layout for Capstone 5.x (must match native struct exactly).
    // struct cs_insn {
    //   uint32_t id;          // offset 0, size 4
    //   // 4 bytes padding
    //   uint64_t alias_id;    // offset 8, size 8  (NEW in v5)
    //   uint64_t address;     // offset 16, size 8
    //   uint16_t size;        // offset 24, size 2
    //   uint8_t bytes[24];    // offset 26, size 24
    //   char mnemonic[32];    // offset 50, size 32
    //   char op_str[160];     // offset 82, size 160
    //   ...
    // }
    public const int InsnAddressOffset = 16;
    public const int InsnSizeOffset = 24;
    public const int InsnMnemonicOffset = 50;
    public const int InsnOpStrOffset = 82;

    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int cs_open(int arch, int mode, out nint handle);

    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int cs_close(ref nint handle);

    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern nint cs_malloc(nint handle);

    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void cs_free(nint insn, nint count);

    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool cs_disasm_iter(nint handle, ref nint code, ref nint size, ref long address, nint insn);
  }
}
