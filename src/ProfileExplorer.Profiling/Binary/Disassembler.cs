// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using ProfileExplorer.Core.Providers;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// A resolved call/jump/branch target for a single instruction, as produced by
/// <see cref="Disassembler.DisassembleToStructuredList"/>. <see cref="Rva"/>/<see cref="Address"/>
/// are the target's module RVA / absolute address; <see cref="SymbolName"/> is populated only
/// when the disassembler's debug-info provider (or symbol name resolver) can resolve it — a null
/// name still carries a structured target address (e.g. an unresolved indirect/import target).
/// </summary>
public record DisassembledInstructionTarget(long Rva, long Address, string? SymbolName, bool IsCall, bool IsJump);

/// <summary>
/// A single disassembled instruction with resolved operand text. <see cref="Mnemonic"/>,
/// <see cref="OperandText"/> and <see cref="Target"/> are only populated by
/// <see cref="Disassembler.DisassembleToStructuredList"/>; <see cref="Disassembler.DisassembleToList"/>
/// leaves them null/default for backward compatibility.
/// </summary>
public record DisassembledInstruction(long Address, long Rva, string Text, int Size,
                                      string? Mnemonic = null, string? OperandText = null,
                                      DisassembledInstructionTarget? Target = null);

public class Disassembler : IDisposable {
  public delegate string SymbolNameResolverDelegate(long address);
  private PEBinaryInfoProvider peInfo_;
  private List<(ReadOnlyMemory<byte> Data, long StartRVA)> codeSectionData_;
  private long baseAddress_;
  private Machine architecture_;
  private ISymbolDebugInfo debugInfo_;
  private Interop.DisassemblerHandle disasmHandle_;
  private bool checkValidCallAddress_;
  private SymbolNameResolverDelegate symbolNameResolver_;
  private FunctionNameFormatter funcNameFormatter_;
  private object sectionLock_;
  private Dictionary<long, string> iatSymbolCache_;

  private Disassembler(Machine architecture,
                       PEBinaryInfoProvider peInfo,
                       long baseAddress = 0,
                       ISymbolDebugInfo debugInfo = null,
                       FunctionNameFormatter funcNameFormatter = null,
                       SymbolNameResolverDelegate symbolNameResolver = null) {
    peInfo_ = peInfo;
    architecture_ = architecture;
    baseAddress_ = baseAddress;
    debugInfo_ = debugInfo;
    funcNameFormatter_ = funcNameFormatter;
    symbolNameResolver_ = symbolNameResolver;
    sectionLock_ = new object();
    Initialize(true);
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  public static Disassembler CreateForBinary(string binaryFilePath, ISymbolDebugInfo debugInfo,
                                             FunctionNameFormatter funcNameFormatter) {
    var peInfo = new PEBinaryInfoProvider(binaryFilePath);

    if (!peInfo.Initialize()) {
      return null;
    }

    var binaryInfo = peInfo.BinaryFileInfo;
    return new Disassembler(binaryInfo.Architecture, peInfo,
                            binaryInfo.ImageBase, debugInfo, funcNameFormatter);
  }

  public static Disassembler CreateForMachine(ISymbolDebugInfo debugInfo,
                                              FunctionNameFormatter funcNameFormatter) {
    return new Disassembler(debugInfo.Architecture.Value, null, 0, debugInfo, funcNameFormatter);
  }

  public void UseSymbolNameResolver(SymbolNameResolverDelegate symbolNameResolver) {
    symbolNameResolver_ = symbolNameResolver;
    checkValidCallAddress_ = false;
    // IAT-slot name resolution is cached per-instance; invalidate the cache so
    // disabling/swapping the resolver takes effect immediately for subsequent
    // disassembly calls.
    iatSymbolCache_?.Clear();
  }

  public string DisassembleToText(FunctionDebugInfo funcInfo) {
    return DisassembleToText(funcInfo.StartRVA, funcInfo.Size);
  }

  public string DisassembleToText(byte[] data, long startRVA) {
    codeSectionData_ = new List<(ReadOnlyMemory<byte> Data, long StartRVA)> {(data.AsMemory(), startRVA)};
    string result = DisassembleToText(startRVA, data.Length);
    codeSectionData_ = null;
    return result;
  }

  public string DisassembleToText(long startRVA, long size) {
    if (startRVA == 0 || size == 0) {
      return "";
    }

    var builder = new StringBuilder((int)(size / 4) + 1);

    try {
      DisassembleInstructions(startRVA, size, startRVA + baseAddress_, (instr) => {
        string addressString = $"{instr.Address:X}:    ";
        builder.Append(addressString);
        int startIndex = 0;
        bool appendBytes = false; //? TODO: Add UI option

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
      });
    }
    catch (Exception ex) {
#if DEBUG
      Trace.TraceError($"Failed to disassemble code at RVA {startRVA}, size {size}: {ex.Message}");
#endif
      return "";
    }

    return builder.ToString();
  }

  /// <summary>
  /// Disassemble a function into a list of individual instructions, with operand text
  /// (call/jump targets resolved to symbol names when debug info is available).
  /// </summary>
  public List<DisassembledInstruction> DisassembleToList(FunctionDebugInfo funcInfo) {
    return DisassembleToList(funcInfo.StartRVA, funcInfo.Size);
  }

  public List<DisassembledInstruction> DisassembleToList(long startRVA, long size) {
    var list = new List<DisassembledInstruction>();

    if (startRVA == 0 || size == 0) {
      return list;
    }

    try {
      DisassembleInstructions(startRVA, size, startRVA + baseAddress_, (instr) => {
        var sb = new StringBuilder();
        AppendMnemonic(instr, sb);
        sb.Append("  ");
        AppendOperands(instr, startRVA, size, sb);
        list.Add(new DisassembledInstruction(instr.Address, instr.Address - baseAddress_,
                                             sb.ToString(), instr.Size));
      });
    }
    catch (Exception ex) {
#if DEBUG
      Trace.TraceError($"Failed to disassemble code list at RVA {startRVA}, size {size}: {ex.Message}");
#endif
    }

    return list;
  }

  /// <summary>
  /// Disassemble a function into a list of structured instructions: mnemonic and operand text are
  /// kept separate (in addition to the combined <see cref="DisassembledInstruction.Text"/>), and
  /// each call/jump/branch instruction carries a resolved <see cref="DisassembledInstructionTarget"/>
  /// when a target address is present in the operand (memory/register-indirect operands, e.g.
  /// "call [rax+0x8]", are not resolved to a target — only direct/PC-relative branches are).
  /// Bounded to [startRVA, startRVA + size), same as <see cref="DisassembleToList"/>: never scans
  /// beyond the requested range.
  /// </summary>
  public List<DisassembledInstruction> DisassembleToStructuredList(long startRVA, long size) {
    var list = new List<DisassembledInstruction>();

    if (startRVA == 0 || size == 0) {
      return list;
    }

    try {
      DisassembleInstructions(startRVA, size, startRVA + baseAddress_, (instr) => {
        string mnemonic = instr.MnemonicString;
        var sb = new StringBuilder();
        AppendOperands(instr, startRVA, size, sb);
        string operandText = sb.ToString();
        string text = $"{mnemonic}  {operandText}";

        DisassembledInstructionTarget? target = null;

        if (TryGetBranchTarget(instr, out long targetRva, out bool isCall, out bool isJump)) {
          string? symbolName = ResolveFunctionName(targetRva);
          target = new DisassembledInstructionTarget(targetRva, targetRva + baseAddress_, symbolName, isCall, isJump);
        }

        list.Add(new DisassembledInstruction(instr.Address, instr.Address - baseAddress_,
                                             text, instr.Size, mnemonic, operandText, target));
      });
    }
    catch (Exception ex) {
#if DEBUG
      Trace.TraceError($"Failed to disassemble structured list at RVA {startRVA}, size {size}: {ex.Message}");
#endif
    }

    return list;
  }

  private void Initialize(bool checkValidCallAddress) {
    checkValidCallAddress_ = checkValidCallAddress;
    disasmHandle_ = Interop.Create(architecture_);
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
          // For x64, try to resolve RIP-relative [rip + 0xN] / [rip - 0xN]
          // memory operands, which are typically IAT slots for indirect
          // calls/jumps through imported functions. The PDB has public
          // symbols (e.g., _imp_FunctionName) at these slot addresses.
          if (architecture_ == Machine.Amd64 && !sawBracket &&
              TryResolveRipRelativeOperand(instr, letterPtr, index, builder,
                                           out int consumedLength)) {
            index += consumedLength;
            continue;
          }

          sawBracket = true; // Reject lookups for call ptr [rax + 0xN] and similar.
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
    if (hexLength == 0) {
      return false;
    }

    if (!checkValidCallAddress_) {
      return true;
    }

    long rva = hexValue - baseAddress_;
    return !FindCodeSection(rva).Data.IsEmpty;
  }

  private bool TryAppendFunctionName(StringBuilder builder, long rva) {
    string name = ResolveFunctionName(rva);

    if (name != null) {
      builder.Append(name);
      return true;
    }

    return false;
  }

  /// <summary>
  /// Resolve the function/symbol name at <paramref name="rva"/>, using the symbol-name resolver
  /// callback when one was supplied (with no fallback to <see cref="debugInfo_"/> — matches the
  /// original inline resolution order), otherwise falling back to <see cref="debugInfo_"/> lookup.
  /// Returns null when nothing resolves.
  /// </summary>
  private string? ResolveFunctionName(long rva) {
    if (symbolNameResolver_ != null) {
      string name = symbolNameResolver_(rva);
      return string.IsNullOrEmpty(name) ? null : name;
    }

    if (debugInfo_ != null) {
      var func = FindFunctionByRva(rva);

      if (func != null) {
        return funcNameFormatter_ != null ? funcNameFormatter_(func.Name) : func.Name;
      }
    }

    return null;
  }

  private FunctionDebugInfo FindFunctionByRva(long rva) {
    if (debugInfo_ != null) {
      return debugInfo_.FindFunctionByRVA(rva);
    }

    return null;
  }

  // Attempt to detect and resolve an x64 RIP-relative memory operand of the
  // form "[rip + 0xN]" or "[rip - 0xN]" (and the rare "[rip]"). On success,
  // appends "[symbol]" to the builder and returns true with consumedLength
  // set to the number of characters that should be skipped in the operand
  // text (including the leading '[' and trailing ']'). On failure leaves the
  // builder unchanged and returns false.
  private unsafe bool TryResolveRipRelativeOperand(Interop.Instruction instr, byte* letterPtr,
                                                   int startIdx, StringBuilder builder,
                                                   out int consumedLength) {
    consumedLength = 0;

    if (letterPtr[startIdx] != (byte)'[') {
      return false;
    }

    int idx = startIdx + 1;
    SkipOperandWhitespace(letterPtr, ref idx);

    // Match "rip".
    if (idx + 2 >= Interop.Instruction.OperandLength ||
        letterPtr[idx] != (byte)'r' ||
        letterPtr[idx + 1] != (byte)'i' ||
        letterPtr[idx + 2] != (byte)'p') {
      return false;
    }

    idx += 3;
    SkipOperandWhitespace(letterPtr, ref idx);

    long signedOffset = 0;

    if (idx < Interop.Instruction.OperandLength && letterPtr[idx] == (byte)']') {
      // Bare [rip] with no displacement.
      idx++;
    }
    else {
      // Expect + or -, then a hex number, then ].
      int sign;

      if (idx >= Interop.Instruction.OperandLength) {
        return false;
      }

      if (letterPtr[idx] == (byte)'+') {
        sign = 1;
      }
      else if (letterPtr[idx] == (byte)'-') {
        sign = -1;
      }
      else {
        return false;
      }

      idx++;
      SkipOperandWhitespace(letterPtr, ref idx);

      int hexLen = FindHexNumber(letterPtr, idx, out long hexValue);

      if (hexLen <= 0) {
        return false;
      }

      idx += hexLen;
      signedOffset = sign * hexValue;
      SkipOperandWhitespace(letterPtr, ref idx);

      if (idx >= Interop.Instruction.OperandLength || letterPtr[idx] != (byte)']') {
        return false;
      }

      idx++;
    }

    // Resolve the target. For RIP-relative addressing the effective address is
    // (next-instruction address) + displacement, i.e. instr.Address + instr.Size + disp.
    long iatSlotAddr = instr.Address + instr.Size + signedOffset;
    long iatSlotRva = iatSlotAddr - baseAddress_;

    string symbolName = TryResolveIatSlotSymbol(iatSlotRva);

    if (string.IsNullOrEmpty(symbolName)) {
      return false;
    }

    builder.Append('[');

    if (funcNameFormatter_ != null) {
      builder.Append(funcNameFormatter_(symbolName));
    }
    else {
      builder.Append(symbolName);
    }

    builder.Append(']');
    consumedLength = idx - startIdx;
    return true;
  }

  // Look up a symbol at an exact RVA, intended for IAT slot resolution.
  // Mirrors TryAppendFunctionName's resolver semantics: if symbolNameResolver_
  // is set, only consult it (do not fall through to debugInfo_). For the
  // debugInfo_ path, require an exact RVA match so we don't accidentally
  // report a nearby function/symbol as if it were the IAT entry.
  private string TryResolveIatSlotSymbol(long rva) {
    iatSymbolCache_ ??= new Dictionary<long, string>();

    if (iatSymbolCache_.TryGetValue(rva, out string cached)) {
      return cached;
    }

    string name = null;

    if (symbolNameResolver_ != null) {
      name = symbolNameResolver_(rva);
    }
    else if (debugInfo_ != null) {
      var funcInfo = FindFunctionByRva(rva);

      if (funcInfo != null && funcInfo.StartRVA == rva) {
        name = funcInfo.Name;
      }
    }

    iatSymbolCache_[rva] = name;
    return name;
  }

  private static unsafe void SkipOperandWhitespace(byte* letterPtr, ref int idx) {
    while (idx < Interop.Instruction.OperandLength && letterPtr[idx] == (byte)' ') {
      idx++;
    }
  }

  private bool ShouldLookupAddressByName(Interop.Instruction instr, ref bool isJump) {
    if (debugInfo_ == null && symbolNameResolver_ == null) {
      return false;
    }

    var (isCall, isJumpBranch) = ClassifyBranch(architecture_, instr.MnemonicString);
    isJump = isJumpBranch;
    return isCall || isJumpBranch;
  }

  /// <summary>
  /// Classify a mnemonic as a call and/or (unconditional) jump/branch instruction, independent of
  /// whether symbol-name resolution is available. Shared by <see cref="ShouldLookupAddressByName"/>
  /// (operand text symbol substitution) and <see cref="TryGetBranchTarget"/> (structured target
  /// extraction) so both stay in sync.
  /// </summary>
  internal static (bool IsCall, bool IsJump) ClassifyBranch(Machine architecture, string mnemonic) {
    switch (architecture) {
      case Machine.I386:
      case Machine.Amd64: {
        // Matches x86Opcodes Call/Goto classification: CALL, SYSCALL, JMP.
        bool isJump = mnemonic.Equals("jmp", StringComparison.OrdinalIgnoreCase);
        bool isCall = mnemonic.Equals("call", StringComparison.OrdinalIgnoreCase) ||
                      mnemonic.Equals("syscall", StringComparison.OrdinalIgnoreCase);
        return (isCall, isJump);
      }
      case Machine.Arm:
      case Machine.Arm64: {
        // Matches ARM64Opcodes Call/Goto classification: B, BR (Goto), BL, BLR (Call).
        bool isJump = mnemonic.Equals("b", StringComparison.OrdinalIgnoreCase) ||
                      mnemonic.Equals("br", StringComparison.OrdinalIgnoreCase);
        bool isCall = mnemonic.Equals("bl", StringComparison.OrdinalIgnoreCase) ||
                      mnemonic.Equals("blr", StringComparison.OrdinalIgnoreCase);
        return (isCall, isJump);
      }
      default:
        return (false, false);
    }
  }

  /// <summary>
  /// Extract the first direct call/jump/branch target address from an instruction's operand text,
  /// without building display text or resolving symbol names (unlike <see cref="AppendOperands"/>,
  /// which does both). Memory/register-indirect operands (e.g. "call [rax+0x8]", "call [rip+0xN]")
  /// are not resolved to a target — only direct/PC-relative branches with a literal hex address
  /// operand are. Returns false for non-branch instructions and for branches whose target can't be
  /// determined from the operand text alone.
  /// </summary>
  private unsafe bool TryGetBranchTarget(Interop.Instruction instr, out long targetRva, out bool isCall, out bool isJump) {
    targetRva = 0;
    (isCall, isJump) = ClassifyBranch(architecture_, instr.MnemonicString);

    if (!isCall && !isJump) {
      return false;
    }

    bool isArm = architecture_ == Machine.Arm || architecture_ == Machine.Arm64;
    bool sawBracket = false;
    byte* letterPtr = instr.Operand;
    int index = 0;

    while (index < Interop.Instruction.OperandLength && letterPtr[index] != 0) {
      char letter = (char)letterPtr[index];
      int hexLength = 0;
      long hexValue = 0;

      if (letter == '[') {
        sawBracket = true; // Memory/register-indirect operand -> not a resolvable direct target.
      }
      else if (letter == '#' && isArm && !sawBracket) {
        hexLength = FindHexNumber(letterPtr, index + 1, out hexValue); // Skip over #.
      }
      else if (letter == '0' && !sawBracket) {
        hexLength = FindHexNumber(letterPtr, index, out hexValue);
      }

      if (IsValidCallAddress(hexLength, hexValue)) {
        targetRva = hexValue - baseAddress_;
        return true;
      }

      index++;
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

  private (ReadOnlyMemory<byte> Data, long StartRVA) FindCodeSection(long rva) {
    // Load code section on-demand to avoid wasting time and memory
    // for binaries that are not dot disassembled.
    if (codeSectionData_ == null) {
      lock (sectionLock_) {
        if (codeSectionData_ == null) {
          var codeSections = peInfo_.CodeSectionHeaders;
          codeSectionData_ = new List<(ReadOnlyMemory<byte> Data, long StartRVA)>();

          foreach (var section in codeSections) {
            codeSectionData_.Add((peInfo_.GetSectionData(section), section.VirtualAddress));
          }
        }
      }
    }

    foreach (var section in codeSectionData_) {
      if (rva >= section.StartRVA && rva < section.StartRVA + section.Data.Length) {
        return section;
      }
    }

    return (ReadOnlyMemory<byte>.Empty, 0);
  }

  private unsafe void DisassembleInstructions(long startRVA, long size, long startAddress,
                                              Action<Interop.Instruction> action) {
    var codeSection = FindCodeSection(startRVA);

    if (codeSection.Data.IsEmpty) {
      Trace.WriteLine($"Invalid disassembler RVA/size {startRVA}/{size}");
      return;
    }

    // Allocate a buffer for storing the instruction,
    // gets reused during iteration.
    using var instrBuffer = Interop.AllocateInstruction(disasmHandle_);
    long offset = startRVA - codeSection.StartRVA;
    using var dataBuffer = codeSection.Data.Pin();
    IntPtr dataBufferPtr = (IntPtr)dataBuffer.Pointer;

    // Disassemble the entire range of the code data buffer.
    IntPtr dataIteratorPtr = (IntPtr)(dataBufferPtr.ToInt64() + offset);
    IntPtr dataEndPtr = (IntPtr)(dataBufferPtr.ToInt64() + offset + size);

    while (dataIteratorPtr.ToInt64() < dataEndPtr.ToInt64()) {
      IntPtr remainingLength = (IntPtr)(dataEndPtr.ToInt64() - dataIteratorPtr.ToInt64());

      // Handles one instruction at a time.
      // dataIteratorPtr is being incremented by the native API.
      if (Interop.Iterate(disasmHandle_, ref dataIteratorPtr, ref remainingLength, ref startAddress, instrBuffer)) {
        IntPtr instrPtr = instrBuffer.DangerousGetHandle();

        if (instrPtr == IntPtr.Zero) {
          return;
        }

        var instruction = (Interop.Instruction)Marshal.PtrToStructure(instrPtr, typeof(Interop.Instruction));
        action(instruction);
      }
      else {
        break;
      }
    }
  }

  private void Dispose(bool disposing) {
    disasmHandle_?.Dispose();
    disasmHandle_ = null;
    peInfo_?.Dispose();
    peInfo_ = null;

    if (disposing) {
      GC.SuppressFinalize(this);
    }
  }

  private static class Interop {
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

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_close")]
    public static extern CapstoneResultCode CloseDisassembler(ref IntPtr pDissembler);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_open")]
    public static extern CapstoneResultCode CreateDisassembler(Architecture architecture,
                                                               DisassembleMode disassembleMode,
                                                               ref IntPtr pDisassembler);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_malloc")]
    public static extern IntPtr CreateInstruction(DisassemblerHandle hDisassembler);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_disasm")]
    public static extern IntPtr Disassemble(DisassemblerHandle hDisassembler, IntPtr pCode, IntPtr codeSize,
                                            long startingAddress, IntPtr count, ref IntPtr pInstructions);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_free")]
    public static extern void FreeInstructions(IntPtr pInstructions, IntPtr count);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_regs_access")]
    public static extern CapstoneResultCode GetAccessedRegisters(DisassemblerHandle hDisassembler,
                                                                 InstructionHandle hInstruction, short[] readRegisters,
                                                                 ref byte readRegistersCount, short[] writtenRegisters,
                                                                 ref byte writtenRegistersCount);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_group_name")]
    public static extern IntPtr GetInstructionGroupName(DisassemblerHandle hDisassembler, int instructionGroupId);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_errno")]
    public static extern CapstoneResultCode GetLastErrorCode(DisassemblerHandle hDisassembler);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_reg_name")]
    public static extern IntPtr GetRegisterName(DisassemblerHandle hDisassembler, int registerId);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_version")]
    public static extern int GetVersion(ref int majorVersion, ref int minorVersion);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_disasm_iter")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Iterate(DisassemblerHandle hDisassembler, ref IntPtr pCode, ref IntPtr codeSize,
                                      ref long address, InstructionHandle hInstruction);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("kernel32.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi,
               EntryPoint = "LoadLibraryA", SetLastError = true)]
    public static extern IntPtr LoadLibrary(string libraryFilePath);

    [SuppressUnmanagedCodeSecurity]
    [DllImport("capstone.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "cs_option")]
    public static extern CapstoneResultCode SetDisassemblerOption(DisassemblerHandle hDisassembler,
                                                                  DisassemblerOptionType optionType,
                                                                  IntPtr optionValue);

    public static DisassemblerHandle Create(Architecture architecture, DisassembleMode mode) {
      IntPtr disasmPtr = IntPtr.Zero;
      var resultCode = CreateDisassembler(architecture, mode, ref disasmPtr);

      if (resultCode == CapstoneResultCode.Ok) {
        var handle = new DisassemblerHandle(disasmPtr);
        //SetDisassemblerOption(handle, DisassemblerOptionType.SetSkipData, (IntPtr)DisassemblerOptionValue.Enable);
        return handle;
      }

      Trace.WriteLine($"Failed to create Capstone disassembler: {resultCode}");
      return null;
    }

    public static DisassemblerHandle Create(Machine architecture) {
      return architecture switch {
        Machine.I386  => Create(Architecture.X86, DisassembleMode.Bit32),
        Machine.Amd64 => Create(Architecture.X86, DisassembleMode.Bit64),
        Machine.Arm   => Create(Architecture.Arm, DisassembleMode.Arm),
        Machine.Arm64 => Create(Architecture.Arm64, DisassembleMode.Arm),
        _             => throw new NotSupportedException("Unsupported architecture!")
      };
    }

    public static InstructionHandle AllocateInstruction(DisassemblerHandle handle) {
      return new InstructionHandle(CreateInstruction(handle));
    }

    // Must be kept in sync with the definition of cs_insn from Capstone.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    public unsafe struct Instruction {
      public const int MnemonicLength = 32;
      public const int OperandLength = 160;
      [FieldOffset(0)]
      public int Id;
      [FieldOffset(8)]
      public long AliasId;
      [FieldOffset(16)]
      public long Address;
      [FieldOffset(24)]
      public short Size;
      [FieldOffset(26)]
      public fixed byte Bytes[24];
      [FieldOffset(50)]
      public fixed byte Mnemonic[32];
      [FieldOffset(82)]
      public fixed byte Operand[160];
      [FieldOffset(242)]
      public bool IsAlias;
      [FieldOffset(243)]
      public bool UsesAliasDetails;
      [FieldOffset(248)]
      public IntPtr Details;

      public byte[] BytesArray {
        get {
          fixed (byte* pinned = Bytes) {
            byte[] bytes = new byte[Size];
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
        FreeInstructions(handle, 1);
        handle = IntPtr.Zero;
        return true;
      }
    }
  }
}