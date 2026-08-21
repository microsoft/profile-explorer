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
  private bool enableSemanticDetail_;

  private Disassembler(Machine architecture,
                       PEBinaryInfoProvider peInfo,
                       long baseAddress = 0,
                       ISymbolDebugInfo debugInfo = null,
                       FunctionNameFormatter funcNameFormatter = null,
                       SymbolNameResolverDelegate symbolNameResolver = null,
                       bool enableSemanticDetail = false) {
    peInfo_ = peInfo;
    architecture_ = architecture;
    baseAddress_ = baseAddress;
    debugInfo_ = debugInfo;
    funcNameFormatter_ = funcNameFormatter;
    symbolNameResolver_ = symbolNameResolver;
    enableSemanticDetail_ = enableSemanticDetail;
    sectionLock_ = new object();
    Initialize(true);
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  /// <param name="enableSemanticDetail">
  /// When true, enables Capstone's <c>CS_OPT_DETAIL</c> mode so <see cref="DisassembleToSemanticList"/>
  /// can return exact register read/write facts. Defaults to false so all existing callers keep
  /// their current behavior and performance profile unchanged; detail mode has a per-instruction
  /// decode cost that only semantic-analysis callers should pay.
  /// </param>
  public static Disassembler CreateForBinary(string binaryFilePath, ISymbolDebugInfo debugInfo,
                                             FunctionNameFormatter funcNameFormatter,
                                             bool enableSemanticDetail = false) {
    var peInfo = new PEBinaryInfoProvider(binaryFilePath);

    if (!peInfo.Initialize()) {
      return null;
    }

    var binaryInfo = peInfo.BinaryFileInfo;
    return new Disassembler(binaryInfo.Architecture, peInfo,
                            binaryInfo.ImageBase, debugInfo, funcNameFormatter,
                            enableSemanticDetail: enableSemanticDetail);
  }

  /// <param name="enableSemanticDetail">See <see cref="CreateForBinary"/>.</param>
  public static Disassembler CreateForMachine(ISymbolDebugInfo debugInfo,
                                              FunctionNameFormatter funcNameFormatter,
                                              bool enableSemanticDetail = false) {
    return new Disassembler(debugInfo.Architecture.Value, null, 0, debugInfo, funcNameFormatter,
                            enableSemanticDetail: enableSemanticDetail);
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

  /// <summary>
  /// Disassemble a function into <see cref="SemanticInstruction"/> records: exact combined
  /// implicit + explicit register read/write sets (via Capstone's <c>cs_regs_access</c>) and a
  /// structured control-transfer classification, alongside the same mnemonic/operand text produced
  /// by <see cref="DisassembleToStructuredList"/>. Register access facts are only exact when this
  /// disassembler was created with <c>enableSemanticDetail: true</c> (see <see cref="CreateForBinary"/>);
  /// otherwise <see cref="SemanticInstruction.HasRegisterAccessDetail"/> is false and the register
  /// lists are empty rather than fabricated. Bounded to [startRVA, startRVA + size), same as the
  /// other Disassemble* APIs -- never scans beyond the requested range.
  /// </summary>
  public List<SemanticInstruction> DisassembleToSemanticList(long startRVA, long size) {
    var list = new List<SemanticInstruction>();

    if (startRVA == 0 || size == 0) {
      return list;
    }

    try {
      DisassembleSemanticInstructions(startRVA, size, startRVA + baseAddress_, (instr, instrHandle) => {
        string mnemonic = instr.MnemonicString;
        var sb = new StringBuilder();
        AppendOperands(instr, startRVA, size, sb);
        string operandText = sb.ToString();

        var groups = ClassifyInstructionGroups(instr);
        var (registersRead, registersWritten, hasDetail) = TryGetRegisterAccess(instrHandle);
        long? targetRva = TryGetBranchTarget(instr, out long branchTargetRva, out _, out _)
          ? branchTargetRva
          : null;
        long? memoryReferenceRva = TryResolveRipRelativeMemoryRva(instr);

        list.Add(new SemanticInstruction {
          Address = instr.Address,
          Rva = instr.Address - baseAddress_,
          Size = instr.Size,
          Mnemonic = mnemonic,
          OperandText = operandText,
          TargetRva = targetRva,
          MemoryReferenceRva = memoryReferenceRva,
          Groups = groups,
          RegistersRead = registersRead,
          RegistersWritten = registersWritten,
          HasRegisterAccessDetail = hasDetail
        });
      });
    }
    catch (Exception ex) {
#if DEBUG
      Trace.TraceError($"Failed to disassemble semantic list at RVA {startRVA}, size {size}: {ex.Message}");
#endif
    }

    return list;
  }

  public List<SemanticInstruction> DisassembleToSemanticList(FunctionDebugInfo funcInfo) {
    return DisassembleToSemanticList(funcInfo.StartRVA, funcInfo.Size);
  }

  private InstructionGroupFlags ClassifyInstructionGroups(Interop.Instruction instr) {
    string mnemonic = instr.MnemonicString;
    var (isCall, isJumpOrBranch) = ClassifyBranch(architecture_, mnemonic);
    var groups = InstructionGroupFlags.None;

    if (isCall) {
      groups |= InstructionGroupFlags.Call;
    }

    if (mnemonic.Equals("ret", StringComparison.OrdinalIgnoreCase)) {
      groups |= InstructionGroupFlags.Return;
    }

    if (isJumpOrBranch) {
      // ClassifyBranch (shared with symbol-name substitution/structured target extraction
      // elsewhere) only ever returns IsJump=true for the *unconditional* x86 "jmp" and ARM64
      // "b"/"br" -- it was written for those two use sites, where the conditional-vs-unconditional
      // distinction didn't matter. This branch is therefore only reachable for those two cases.
      bool isUnconditional = architecture_ switch {
        Machine.I386 or Machine.Amd64 => mnemonic.Equals("jmp", StringComparison.OrdinalIgnoreCase),
        Machine.Arm or Machine.Arm64 => mnemonic.Equals("b", StringComparison.OrdinalIgnoreCase) ||
                                        mnemonic.Equals("br", StringComparison.OrdinalIgnoreCase),
        _ => false
      };

      groups |= isUnconditional ? InstructionGroupFlags.UnconditionalJump : InstructionGroupFlags.ConditionalBranch;
    }
    else if (IsConditionalBranchMnemonic(architecture_, mnemonic)) {
      // Conditional branches (x86 Jcc, ARM64 "b.<cond>"/cbz/cbnz/tbz/tbnz) are NOT recognized as
      // branches at all by ClassifyBranch above -- confirmed by decoding real conditional-jump
      // bytes through this exact path (see DisassemblerSemanticDetailTests). CFG construction
      // needs every conditional branch recognized, so detect the common families here rather than
      // widen ClassifyBranch's existing, separately relied-upon behavior in this change.
      groups |= InstructionGroupFlags.ConditionalBranch;
    }

    return groups;
  }

  /// <summary>
  /// Detects conditional-branch mnemonics that <see cref="ClassifyBranch"/> does not classify as
  /// jumps/branches at all (see <see cref="ClassifyInstructionGroups"/>). Deliberately conservative:
  /// covers the common x86 Jcc family and the ARM64 "b.&lt;cond&gt;"/cbz/cbnz/tbz/tbnz family, which
  /// covers everything Capstone's default syntax produces for conditional control transfer on these
  /// architectures.
  /// </summary>
  private static bool IsConditionalBranchMnemonic(Machine architecture, string mnemonic) {
    switch (architecture) {
      case Machine.I386:
      case Machine.Amd64:
        // Every x86 "j*" mnemonic other than the unconditional "jmp" is a conditional jump (Jcc);
        // this also covers jcxz/jecxz/jrcxz (branch if counter register is zero).
        return mnemonic.StartsWith("j", StringComparison.OrdinalIgnoreCase) &&
               !mnemonic.Equals("jmp", StringComparison.OrdinalIgnoreCase);
      case Machine.Arm:
      case Machine.Arm64:
        // Capstone's default AArch64 syntax renders conditional branches as "b.<cond>" (e.g.
        // "b.eq", "b.ne"); the compare/test-and-branch family is a separate encoding but is
        // equally conditional.
        return mnemonic.StartsWith("b.", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("cbz", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("cbnz", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("tbz", StringComparison.OrdinalIgnoreCase) ||
               mnemonic.Equals("tbnz", StringComparison.OrdinalIgnoreCase);
      default:
        return false;
    }
  }

  /// <summary>
  /// Combined implicit + explicit register read/write sets for one instruction, via Capstone's
  /// <c>cs_regs_access</c> -- a real decode API, not a text-parsing heuristic. Requires the
  /// disassembler to have been created with semantic detail enabled; returns
  /// (empty, empty, false) otherwise so callers never mistake "detail unavailable" for
  /// "no registers accessed".
  /// </summary>
  private (IReadOnlyList<string> Read, IReadOnlyList<string> Written, bool HasDetail)
      TryGetRegisterAccess(Interop.InstructionHandle instrHandle) {
    if (!enableSemanticDetail_) {
      return (Array.Empty<string>(), Array.Empty<string>(), false);
    }

    // Capstone's cstool reference implementation sizes these at 64; MAX_IMPL_R_REGS/MAX_IMPL_W_REGS
    // (20/47) plus explicit operand registers comfortably fit within that bound for every
    // architecture Profile Explorer disassembles.
    var readIds = new short[64];
    var writtenIds = new short[64];
    byte readCount = 0;
    byte writtenCount = 0;

    var result = Interop.GetAccessedRegisters(disasmHandle_, instrHandle, readIds, ref readCount,
                                              writtenIds, ref writtenCount);

    if (result != Interop.CapstoneResultCode.Ok) {
      return (Array.Empty<string>(), Array.Empty<string>(), false);
    }

    return (MapRegisterIds(readIds, readCount), MapRegisterIds(writtenIds, writtenCount), true);
  }

  private string[] MapRegisterIds(short[] ids, byte count) {
    if (count == 0) {
      return Array.Empty<string>();
    }

    var names = new string[count];

    for (int i = 0; i < count; i++) {
      names[i] = GetRegisterNameSafe(ids[i]);
    }

    return names;
  }

  private string GetRegisterNameSafe(int registerId) {
    if (registerId == 0) {
      // 0 is always the architecture's *_REG_INVALID sentinel; cs_regs_access shouldn't emit it,
      // but guard defensively rather than report a fabricated name.
      return $"<invalid-reg-0>";
    }

    IntPtr namePtr = Interop.GetRegisterName(disasmHandle_, registerId);
    return namePtr == IntPtr.Zero ? $"reg{registerId}" : Marshal.PtrToStringAnsi(namePtr) ?? $"reg{registerId}";
  }

  private void Initialize(bool checkValidCallAddress) {
    checkValidCallAddress_ = checkValidCallAddress;
    disasmHandle_ = Interop.Create(architecture_, enableSemanticDetail_);
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

  /// <summary>
  /// Detects an x64 RIP-relative memory operand ("[rip + 0xN]", "[rip - 0xN]", or bare "[rip]")
  /// anywhere in the instruction's *raw* Capstone operand text (<see cref="Interop.Instruction.OperandString"/>,
  /// never the locally-rendered/symbol-substituted <c>operandText</c> built by
  /// <see cref="AppendOperands"/>) and resolves its effective address to a module RVA. This is a
  /// lightweight, managed-string re-implementation of the same effective-address arithmetic
  /// <see cref="TryResolveRipRelativeOperand"/> uses for symbol-name substitution, kept
  /// independent so semantic instruction construction never depends on that method's unsafe
  /// pointer-scanning/text-building side effects. Using the raw operand text (rather than the
  /// rendered one) means this works correctly regardless of whether the disassembler was given a
  /// PDB/symbol resolver that would otherwise rewrite "[rip+N]" into "[SymbolName]" in the
  /// rendered text. x86/ARM64 do not use RIP-relative addressing for this pattern, so this only
  /// applies to <see cref="Machine.Amd64"/>.
  /// </summary>
  private long? TryResolveRipRelativeMemoryRva(Interop.Instruction instr) {
    if (architecture_ != Machine.Amd64) {
      return null;
    }

    string operandText = instr.OperandString;

    if (string.IsNullOrEmpty(operandText)) {
      return null;
    }

    int ripIndex = operandText.IndexOf("[rip", StringComparison.Ordinal);

    if (ripIndex < 0) {
      return null;
    }

    int idx = ripIndex + 4; // Past "[rip".

    while (idx < operandText.Length && operandText[idx] == ' ') {
      idx++;
    }

    long signedOffset;

    if (idx < operandText.Length && operandText[idx] == ']') {
      signedOffset = 0; // Bare "[rip]".
    }
    else if (idx < operandText.Length && (operandText[idx] == '+' || operandText[idx] == '-')) {
      int sign = operandText[idx] == '+' ? 1 : -1;
      idx++;

      while (idx < operandText.Length && operandText[idx] == ' ') {
        idx++;
      }

      if (idx + 1 >= operandText.Length || operandText[idx] != '0' ||
          (operandText[idx + 1] != 'x' && operandText[idx + 1] != 'X')) {
        return null; // Not the expected "0x..." hex displacement form -- refuse to guess.
      }

      idx += 2;
      int digitsStart = idx;

      while (idx < operandText.Length && Uri.IsHexDigit(operandText[idx])) {
        idx++;
      }

      if (idx == digitsStart) {
        return null; // "0x" with no digits -- malformed.
      }

      signedOffset = sign * Convert.ToInt64(operandText.Substring(digitsStart, idx - digitsStart), 16);
    }
    else {
      return null; // Not a recognized "[rip ...]" form.
    }

    // For RIP-relative addressing the effective address is (next-instruction address) + displacement.
    long effectiveAddress = instr.Address + instr.Size + signedOffset;
    return effectiveAddress - baseAddress_;
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
    string mnemonic = instr.MnemonicString;
    (isCall, isJump) = ClassifyBranch(architecture_, mnemonic);

    if (!isCall && !isJump && IsConditionalBranchMnemonic(architecture_, mnemonic)) {
      // ClassifyBranch doesn't recognize conditional branches (je/b.eq/cbz/tbz/...) as branches at
      // all (see ClassifyInstructionGroups, which found this gap via testing) -- their operand is
      // still a direct PC-relative target exactly like an unconditional jump, so widen here too.
      // This resolves a pre-existing gap: conditional-branch targets were previously unresolved in
      // DisassembleToStructuredList's DisassembledInstructionTarget as well, for every caller.
      isJump = true;
    }

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

  /// <summary>
  /// Mirrors <see cref="DisassembleInstructions"/> exactly, but additionally passes the reused
  /// native instruction handle to the callback so it can call Capstone APIs that need the raw
  /// <c>cs_insn*</c> (e.g. <see cref="Interop.GetAccessedRegisters"/>/<c>cs_regs_access</c>), which
  /// isn't available from the marshaled-out <see cref="Interop.Instruction"/> struct alone. Kept as
  /// a separate method rather than changing <see cref="DisassembleInstructions"/>'s callback
  /// signature, so the existing text/list/structured disassembly paths -- and their call sites --
  /// are completely unaffected by this addition.
  /// </summary>
  private unsafe void DisassembleSemanticInstructions(long startRVA, long size, long startAddress,
                                                      Action<Interop.Instruction, Interop.InstructionHandle> action) {
    var codeSection = FindCodeSection(startRVA);

    if (codeSection.Data.IsEmpty) {
      Trace.WriteLine($"Invalid disassembler RVA/size {startRVA}/{size}");
      return;
    }

    using var instrBuffer = Interop.AllocateInstruction(disasmHandle_);
    long offset = startRVA - codeSection.StartRVA;
    using var dataBuffer = codeSection.Data.Pin();
    IntPtr dataBufferPtr = (IntPtr)dataBuffer.Pointer;

    IntPtr dataIteratorPtr = (IntPtr)(dataBufferPtr.ToInt64() + offset);
    IntPtr dataEndPtr = (IntPtr)(dataBufferPtr.ToInt64() + offset + size);

    while (dataIteratorPtr.ToInt64() < dataEndPtr.ToInt64()) {
      IntPtr remainingLength = (IntPtr)(dataEndPtr.ToInt64() - dataIteratorPtr.ToInt64());

      if (Interop.Iterate(disasmHandle_, ref dataIteratorPtr, ref remainingLength, ref startAddress, instrBuffer)) {
        IntPtr instrPtr = instrBuffer.DangerousGetHandle();

        if (instrPtr == IntPtr.Zero) {
          return;
        }

        var instruction = (Interop.Instruction)Marshal.PtrToStructure(instrPtr, typeof(Interop.Instruction));
        action(instruction, instrBuffer);
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

    public static DisassemblerHandle Create(Architecture architecture, DisassembleMode mode,
                                            bool enableDetail = false) {
      IntPtr disasmPtr = IntPtr.Zero;
      var resultCode = CreateDisassembler(architecture, mode, ref disasmPtr);

      if (resultCode == CapstoneResultCode.Ok) {
        var handle = new DisassemblerHandle(disasmPtr);
        //SetDisassemblerOption(handle, DisassemblerOptionType.SetSkipData, (IntPtr)DisassemblerOptionValue.Enable);

        if (enableDetail) {
          // Must be set before the first cs_disasm/cs_disasm_iter call on this handle -- Capstone
          // ties detail-buffer allocation to option state at open time. Required by cs_regs_access
          // (see TryGetRegisterAccess), which is what DisassembleToSemanticList relies on for exact
          // (non-heuristic) register read/write facts.
          var detailResult = SetDisassemblerOption(handle, DisassemblerOptionType.SetInstructionDetails,
                                                   (IntPtr)DisassemblerOptionValue.Enable);

          if (detailResult != CapstoneResultCode.Ok) {
            Trace.WriteLine($"Failed to enable Capstone instruction detail: {detailResult}");
          }
        }

        return handle;
      }

      Trace.WriteLine($"Failed to create Capstone disassembler: {resultCode}");
      return null;
    }

    public static DisassemblerHandle Create(Machine architecture, bool enableDetail = false) {
      return architecture switch {
        Machine.I386  => Create(Architecture.X86, DisassembleMode.Bit32, enableDetail),
        Machine.Amd64 => Create(Architecture.X86, DisassembleMode.Bit64, enableDetail),
        Machine.Arm   => Create(Architecture.Arm, DisassembleMode.Arm, enableDetail),
        Machine.Arm64 => Create(Architecture.Arm64, DisassembleMode.Arm, enableDetail),
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