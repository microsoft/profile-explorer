// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// Coarse-grained classification of what kind of control transfer (if any) an instruction
/// performs. Derived deterministically from the mnemonic via <see cref="Disassembler.ClassifyBranch"/>
/// (already used/tested elsewhere for symbol-name substitution), refined here with the
/// unconditional-vs-conditional and return distinctions needed for CFG construction.
/// </summary>
[Flags]
public enum InstructionGroupFlags {
  None = 0,
  Call = 1 << 0,
  UnconditionalJump = 1 << 1,
  ConditionalBranch = 1 << 2,
  Return = 1 << 3,
}

/// <summary>
/// A single disassembled instruction enriched with Capstone's <c>CS_OPT_DETAIL</c> output: the
/// exact combined implicit + explicit set of registers read/written (via Capstone's
/// <c>cs_regs_access</c> API -- a real decode result, never a text-parsing guess), plus a
/// structured control-transfer classification.
/// <para>
/// This is the first tier of the semantic instruction model described in the reverse-engineering
/// enhancement plan. It deliberately does not yet include typed per-operand decoding (register vs.
/// immediate vs. memory operands with base/index/scale/displacement) or condition-flag
/// read/write sets -- both require hand-porting Capstone's architecture-specific <c>cs_detail</c>
/// union layout and are tracked as a follow-up increment, gated by their own native-truth tests,
/// rather than risked in this change.
/// </para>
/// </summary>
public sealed class SemanticInstruction {
  public long Address { get; init; }
  public long Rva { get; init; }
  public int Size { get; init; }
  public string Mnemonic { get; init; } = "";
  public string OperandText { get; init; } = "";

  /// <summary>
  /// The resolved module-relative RVA of a direct call/jump/branch target, when the operand is a
  /// literal PC-relative/absolute address (never a memory/register-indirect operand) -- the same
  /// resolution <see cref="Disassembler.DisassembleToStructuredList"/> uses for
  /// <see cref="DisassembledInstructionTarget"/>. Null for non-branch instructions and for
  /// indirect calls/jumps (e.g. "call [rax]") whose target cannot be determined statically. This
  /// is what <see cref="FunctionControlFlowGraph.Build"/> uses to resolve edges.
  /// </summary>
  public long? TargetRva { get; init; }

  /// <summary>Combined implicit + explicit registers read, by Capstone register name (e.g. "eax", "rbx").</summary>
  public IReadOnlyList<string> RegistersRead { get; init; } = Array.Empty<string>();

  /// <summary>Combined implicit + explicit registers written, by Capstone register name.</summary>
  public IReadOnlyList<string> RegistersWritten { get; init; } = Array.Empty<string>();

  public InstructionGroupFlags Groups { get; init; }
  public bool IsCall => Groups.HasFlag(InstructionGroupFlags.Call);
  public bool IsUnconditionalJump => Groups.HasFlag(InstructionGroupFlags.UnconditionalJump);
  public bool IsConditionalBranch => Groups.HasFlag(InstructionGroupFlags.ConditionalBranch);
  public bool IsReturn => Groups.HasFlag(InstructionGroupFlags.Return);

  /// <summary>
  /// True when Capstone's <c>CS_OPT_DETAIL</c> was enabled for this disassembler instance and
  /// <see cref="RegistersRead"/>/<see cref="RegistersWritten"/> are exact decode results. False
  /// means detail mode was off -- callers must not treat empty register lists in that case as
  /// "no registers accessed"; the fact is simply absent, not negative.
  /// </summary>
  public bool HasRegisterAccessDetail { get; init; }
}
