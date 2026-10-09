// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// How a register's value at a program point relates to prior definitions.
/// </summary>
public enum ReachingDefinitionState {
  /// <summary>No definition reaches this point (e.g. an incoming parameter/caller-set register,
  /// or truly the start of the function) -- not the same as "unknown due to ambiguity".</summary>
  Undefined,
  /// <summary>Exactly one instruction's write reaches this point deterministically.</summary>
  Unique,
  /// <summary>Two or more distinct definitions reach this point via different control-flow paths
  /// (a CFG join with disagreeing predecessors), or a partial/possibly clobbered value cannot be
  /// attributed to one complete definition. Reported as ambiguous rather than guessed.</summary>
  Ambiguous
}

/// <summary>A single register's reaching-definition state at some program point.</summary>
public readonly record struct ReachingDefinition(ReachingDefinitionState State, int? DefiningInstructionIndex) {
  public static readonly ReachingDefinition Undefined = new(ReachingDefinitionState.Undefined, null);
  public static readonly ReachingDefinition Ambiguous = new(ReachingDefinitionState.Ambiguous, null);
  public static ReachingDefinition Unique(int instructionIndex) => new(ReachingDefinitionState.Unique, instructionIndex);
}

/// <summary>
/// Function-local, register-level reaching-definitions analysis over a
/// <see cref="FunctionControlFlowGraph"/> -- the first tier of data-flow tracking from the
/// reverse-engineering enhancement plan. Deliberately lighter than full SSA (see the plan's
/// phasing rationale): standard block-level reaching-definitions dataflow, a narrow
/// immediate-move constant detector (no multi-hop copy propagation), and a bounded backward
/// slice built from register reaching-def chains. Never claims object lifetime, aliasing, or
/// points-to facts -- only "which instruction most recently wrote this register here", reported
/// as explicitly ambiguous rather than guessed when control flow disagrees.
/// </summary>
public sealed class FunctionDataFlowAnalysis {
  private readonly record struct RegisterView(string Family, int Width, int Offset = 0);
  private static readonly Dictionary<string, RegisterView> RegisterViews = BuildRegisterViews();
  private static readonly Dictionary<string, RegisterView> Arm64RegisterViews = new(RegisterViews, StringComparer.OrdinalIgnoreCase) {
    ["sp"] = new("sp", 64), ["wsp"] = new("sp", 32), ["fp"] = new("x29", 64), ["lr"] = new("x30", 64)
  };
  private readonly Machine architecture_;
  private readonly List<SemanticInstruction> flatInstructions_;
  private readonly Dictionary<long, int> instructionIndexByRva_;
  private readonly int[] blockIdForInstruction_;
  private readonly FunctionControlFlowGraph cfg_;

  // Per block: the register->definition state flowing INTO the block's first instruction.
  private readonly Dictionary<int, Dictionary<string, ReachingDefinition>> blockInState_;

  private FunctionDataFlowAnalysis(FunctionControlFlowGraph cfg, List<SemanticInstruction> flatInstructions,
                                   Dictionary<long, int> instructionIndexByRva, int[] blockIdForInstruction,
                                   Dictionary<int, Dictionary<string, ReachingDefinition>> blockInState,
                                   Machine architecture) {
    cfg_ = cfg;
    flatInstructions_ = flatInstructions;
    instructionIndexByRva_ = instructionIndexByRva;
    blockIdForInstruction_ = blockIdForInstruction;
    blockInState_ = blockInState;
    architecture_ = architecture;
  }

  public int InstructionCount => flatInstructions_.Count;
  public SemanticInstruction GetInstruction(int index) => flatInstructions_[index];
  public int? FindInstructionIndexByRva(long rva) => instructionIndexByRva_.TryGetValue(rva, out int idx) ? idx : null;

  /// <summary>
  /// Builds the analysis for a function's CFG (see <see cref="FunctionControlFlowGraph.Build"/>).
  /// Requires the semantic instructions to have been decoded with register-access detail enabled
  /// (<see cref="Disassembler.DisassembleToSemanticList(long, long)"/> with
  /// <c>enableSemanticDetail: true</c>); without it, every register access is invisible and every
  /// query cannot recover a definition from the missing detail.
  /// </summary>
  public static FunctionDataFlowAnalysis Build(FunctionControlFlowGraph cfg) => Build(cfg, Machine.Amd64);

  public static FunctionDataFlowAnalysis Build(FunctionControlFlowGraph cfg, Machine architecture) {
    var flatInstructions = new List<SemanticInstruction>();
    var instructionIndexByRva = new Dictionary<long, int>();
    var blockIdForInstruction = new List<int>();

    foreach (var block in cfg.Blocks) {
      foreach (var instr in block.Instructions) {
        instructionIndexByRva[instr.Rva] = flatInstructions.Count;
        blockIdForInstruction.Add(block.Id);
        flatInstructions.Add(instr);
      }
    }

    var blockInState = ComputeBlockInStates(cfg, flatInstructions, blockIdForInstruction, architecture);

    return new FunctionDataFlowAnalysis(cfg, flatInstructions, instructionIndexByRva,
                                        blockIdForInstruction.ToArray(), blockInState, architecture);
  }

  private static Dictionary<string, RegisterView> BuildRegisterViews() {
    var views = new Dictionary<string, RegisterView>(StringComparer.OrdinalIgnoreCase);
    string[][] families = {
      new[] { "rax", "eax", "ax", "al", "ah" }, new[] { "rbx", "ebx", "bx", "bl", "bh" },
      new[] { "rcx", "ecx", "cx", "cl", "ch" }, new[] { "rdx", "edx", "dx", "dl", "dh" },
      new[] { "rsi", "esi", "si", "sil" }, new[] { "rdi", "edi", "di", "dil" },
      new[] { "rbp", "ebp", "bp", "bpl" }, new[] { "rsp", "esp", "sp", "spl" }
    };

    foreach (var family in families) {
      for (int i = 0; i < family.Length; i++) {
        views[family[i]] = new RegisterView(family[0], i == 0 ? 64 : i == 1 ? 32 : i == 2 ? 16 : 8,
          i == 4 ? 8 : 0);
      }
    }

    for (int i = 8; i <= 15; i++) {
      foreach (var (suffix, width) in new[] { ("", 64), ("d", 32), ("w", 16), ("b", 8) }) {
        views[$"r{i}{suffix}"] = new RegisterView($"r{i}", width);
      }
    }

    for (int i = 0; i <= 30; i++) {
      views[$"x{i}"] = new RegisterView($"x{i}", 64);
      views[$"w{i}"] = new RegisterView($"x{i}", 32);
    }

    for (int i = 0; i < 32; i++) {
      foreach (var (prefix, width) in new[] { ("xmm", 128), ("ymm", 256), ("zmm", 512) }) {
        views[$"{prefix}{i}"] = new RegisterView($"zmm{i}", width);
      }
      foreach (var (prefix, width) in new[] { ("b", 8), ("h", 16), ("s", 32), ("d", 64), ("q", 128), ("v", 128) }) {
        views[$"{prefix}{i}"] = new RegisterView($"v{i}", width);
      }
    }

    return views;
  }

  private static void ApplyRegisterWrite(Dictionary<string, ReachingDefinition> state, string register,
                                         ReachingDefinition definition, Machine architecture) {
    register = register.ToLowerInvariant();
    var registerViews = architecture == Machine.Arm64 ? Arm64RegisterViews : RegisterViews;
    if (!registerViews.TryGetValue(register, out var written)) {
      state[register] = definition;
      return;
    }

    // x64 E-register and ARM64 W-register writes zero-extend the full general-purpose register.
    int writtenWidth = written.Width == 32 &&
                       (written.Family.StartsWith('r') || written.Family.StartsWith('x') || written.Family == "sp")
      ? 64 : written.Width;

    foreach (var (alias, view) in registerViews) {
      if (view.Family != written.Family || view.Offset >= written.Offset + writtenWidth ||
          written.Offset >= view.Offset + view.Width) {
        continue;
      }

      bool fullyWritten = written.Offset <= view.Offset &&
                          written.Offset + writtenWidth >= view.Offset + view.Width;
      state[alias] = fullyWritten ? definition : ReachingDefinition.Ambiguous;
    }
  }

  private static void ApplyInstructionWrites(Dictionary<string, ReachingDefinition> state,
                                             SemanticInstruction instruction, int index, Machine architecture) {
    if (instruction.IsCall) {
      // Capstone describes the call instruction itself, not arbitrary writes performed by its callee.
      string[] volatileRegisters = architecture == Machine.Arm64
        ? new[] { "x0", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11",
                  "x12", "x13", "x14", "x15", "x16", "x17", "x18", "x30", "nzcv" }
        : new[] { "rax", "rcx", "rdx", "r8", "r9", "r10", "r11", "rflags", "eflags" };
      foreach (string register in volatileRegisters) {
        ApplyRegisterWrite(state, register, ReachingDefinition.Ambiguous, architecture);
      }
      for (int i = 0; i < 32; i++) {
        // Vector preservation is lane/ABI-dependent; do not pretend an entire vector is unchanged.
        ApplyRegisterWrite(state, architecture == Machine.Arm64 ? $"v{i}" : $"zmm{i}",
          ReachingDefinition.Ambiguous, architecture);
      }
    }

    foreach (string register in instruction.RegistersWritten) {
      ApplyRegisterWrite(state, register, ReachingDefinition.Unique(index), architecture);
    }
  }

  /// <summary>
  /// Standard iterative reaching-definitions dataflow at block granularity: IN[b] is the merge of
  /// OUT[preds], OUT[b] is IN[b] with each block-local write overwriting ("last write wins" within
  /// the block, matching straight-line execution). The state lattice (Undefined -&gt; Unique -&gt;
  /// Ambiguous) only ever moves toward Ambiguous, so this always terminates.
  /// </summary>
  private static Dictionary<int, Dictionary<string, ReachingDefinition>> ComputeBlockInStates(
      FunctionControlFlowGraph cfg, List<SemanticInstruction> flatInstructions, List<int> blockIdForInstruction,
      Machine architecture) {
    var blockOutState = new Dictionary<int, Dictionary<string, ReachingDefinition>>();
    var blockInState = new Dictionary<int, Dictionary<string, ReachingDefinition>>();

    foreach (var block in cfg.Blocks) {
      blockInState[block.Id] = new Dictionary<string, ReachingDefinition>();
      blockOutState[block.Id] = new Dictionary<string, ReachingDefinition>();
    }

    // Precompute the global start index of each block's first instruction, for ApplyBlockWrites.
    var blockStartIndex = new Dictionary<int, int>();
    for (int i = 0; i < blockIdForInstruction.Count; i++) {
      if (!blockStartIndex.ContainsKey(blockIdForInstruction[i])) {
        blockStartIndex[blockIdForInstruction[i]] = i;
      }
    }

    bool changed = true;
    while (changed) {
      changed = false;

      foreach (var block in cfg.Blocks) {
        var merged = MergePredecessorStates(block, blockOutState, block.Id == cfg.EntryBlockId);

        if (!StatesEqual(merged, blockInState[block.Id])) {
          blockInState[block.Id] = merged;
          changed = true;
        }

        var outState = ApplyBlockWrites(merged, block, blockStartIndex[block.Id], flatInstructions, architecture);

        if (!StatesEqual(outState, blockOutState[block.Id])) {
          blockOutState[block.Id] = outState;
          changed = true;
        }
      }
    }

    return blockInState;
  }

  private static Dictionary<string, ReachingDefinition> MergePredecessorStates(
      BasicBlock block, Dictionary<int, Dictionary<string, ReachingDefinition>> blockOutState, bool isEntry) {
    if (block.PredecessorIds.Count == 0) {
      return new Dictionary<string, ReachingDefinition>(); // Function entry: everything Undefined.
    }

    Dictionary<string, ReachingDefinition>? merged = isEntry ? new() : null;

    foreach (int predId in block.PredecessorIds) {
      var predOut = blockOutState[predId];

      if (merged == null) {
        merged = new Dictionary<string, ReachingDefinition>(predOut);
        continue;
      }

      // Union of registers; any register missing from one predecessor's map (still Undefined
      // there) but present in another is itself a disagreement -> Ambiguous.
      var allRegs = new HashSet<string>(merged.Keys);
      allRegs.UnionWith(predOut.Keys);

      var next = new Dictionary<string, ReachingDefinition>();

      foreach (string reg in allRegs) {
        var a = merged.TryGetValue(reg, out var av) ? av : ReachingDefinition.Undefined;
        var b = predOut.TryGetValue(reg, out var bv) ? bv : ReachingDefinition.Undefined;
        next[reg] = a == b ? a : ReachingDefinition.Ambiguous;
      }

      merged = next;
    }

    return merged!;
  }

  private static Dictionary<string, ReachingDefinition> ApplyBlockWrites(
      Dictionary<string, ReachingDefinition> inState, BasicBlock block, int blockStartGlobalIndex,
      List<SemanticInstruction> flatInstructions, Machine architecture) {
    var state = new Dictionary<string, ReachingDefinition>(inState);

    for (int i = 0; i < block.Instructions.Count; i++) {
      int globalIndex = blockStartGlobalIndex + i;

      ApplyInstructionWrites(state, flatInstructions[globalIndex], globalIndex, architecture);
    }

    return state;
  }

  private static bool StatesEqual(Dictionary<string, ReachingDefinition> a, Dictionary<string, ReachingDefinition> b) {
    if (a.Count != b.Count) {
      return false;
    }

    foreach (var kvp in a) {
      if (!b.TryGetValue(kvp.Key, out var other) || other != kvp.Value) {
        return false;
      }
    }

    return true;
  }

  /// <summary>
  /// The reaching definition of <paramref name="register"/> immediately before instruction
  /// <paramref name="instructionIndex"/> executes (i.e. reflecting all writes strictly earlier in
  /// program order within the same block, or the block's merged incoming state if not written
  /// earlier in this block).
  /// </summary>
  public ReachingDefinition GetReachingDefinition(int instructionIndex, string register) {
    int blockId = blockIdForInstruction_[instructionIndex];
    var block = cfg_.GetBlock(blockId);
    int blockStart = instructionIndex - IndexWithinBlock(instructionIndex, block);
    var state = new Dictionary<string, ReachingDefinition>(blockInState_[blockId]);

    for (int i = blockStart; i < instructionIndex; i++) {
      ApplyInstructionWrites(state, flatInstructions_[i], i, architecture_);
    }

    return state.TryGetValue(register.ToLowerInvariant(), out var def) ? def : ReachingDefinition.Undefined;
  }

  private int IndexWithinBlock(int globalIndex, BasicBlock block) {
    // Blocks are built from a contiguous run of the flattened instruction list (see Build), so the
    // position within the block is just the offset from its first instruction's global index.
    int firstGlobalIndex = instructionIndexByRva_[block.Instructions[0].Rva];
    return globalIndex - firstGlobalIndex;
  }

  // Matches a plain "mov <reg>, <imm>" (x86/x64) or "mov <reg>, #<imm>" (ARM64) -- a direct
  // immediate load with no other addressing/arithmetic. Anything else (register moves, memory
  // operands, arithmetic) is intentionally not treated as a constant -- no multi-hop propagation.
  private static readonly Regex ImmediateMoveWithHashPattern =
    new(@"^\s*(?<dst>[a-zA-Z0-9]+)\s*,\s*#(?<imm>0x[0-9a-fA-F]+|-?\d+)\s*$", RegexOptions.Compiled);
  private static readonly Regex ImmediateMovePattern =
    new(@"^\s*(?<dst>[a-zA-Z0-9]+)\s*,\s*(?<imm>0x[0-9a-fA-F]+|-?\d+)\s*$", RegexOptions.Compiled);

  /// <summary>
  /// True when <paramref name="register"/>'s reaching definition at <paramref name="instructionIndex"/>
  /// is a single, unambiguous direct immediate load ("mov reg, 0xN" / "mov reg, #0xN") -- not a
  /// register-to-register move or any other pattern requiring multi-hop copy propagation (out of
  /// scope for this tier; see the plan's phased design). False whenever the value can't be proven
  /// constant, rather than guessing.
  /// </summary>
  public bool TryGetConstantValue(int instructionIndex, string register, out long value) {
    value = 0;
    var def = GetReachingDefinition(instructionIndex, register);

    if (def.State != ReachingDefinitionState.Unique || def.DefiningInstructionIndex is not int defIndex) {
      return false;
    }

    var defInstr = flatInstructions_[defIndex];

    if (!defInstr.Mnemonic.Equals("mov", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var match = ImmediateMoveWithHashPattern.Match(defInstr.OperandText);

    if (!match.Success) {
      match = ImmediateMovePattern.Match(defInstr.OperandText);
    }

    if (!match.Success) {
      return false;
    }

    string immText = match.Groups["imm"].Value;
    bool parsed = immText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
      ? long.TryParse(immText[2..], System.Globalization.NumberStyles.HexNumber, null, out value)
      : long.TryParse(immText, out value);

    if (!parsed) return false;

    string destination = match.Groups["dst"].Value;
    var registerViews = architecture_ == Machine.Arm64 ? Arm64RegisterViews : RegisterViews;
    if (registerViews.TryGetValue(destination, out var written) &&
        registerViews.TryGetValue(register, out var read) && written.Family == read.Family) {
      ulong bits = (ulong)value;
      if (written.Width < 64) bits &= (1UL << written.Width) - 1;
      bits >>= read.Offset - written.Offset;
      if (read.Width < 64) bits &= (1UL << read.Width) - 1;
      value = (long)bits;
      return true;
    }

    return destination.Equals(register, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Bounded backward slice from <paramref name="instructionIndex"/>: the transitive set of
  /// instructions (including the target itself) whose register writes reach and are read by the
  /// target, found by repeatedly following each instruction's <see cref="SemanticInstruction.RegistersRead"/>
  /// to its unique reaching definition. Stops (does not fabricate a link) at any register whose
  /// reaching definition is <see cref="ReachingDefinitionState.Undefined"/> or
  /// <see cref="ReachingDefinitionState.Ambiguous"/> -- both are reported as dead ends, not guessed
  /// through. Bounded by <paramref name="maxInstructions"/> so a large/cyclic dependency graph
  /// (e.g. a loop counter) can never make this unbounded.
  /// </summary>
  public List<int> BackwardSlice(int instructionIndex, int maxInstructions = 64) {
    return BackwardSlice(instructionIndex, maxInstructions, out _);
  }

  public List<int> BackwardSlice(int instructionIndex, int maxInstructions, out bool wasTruncated) {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInstructions);
    wasTruncated = false;
    var visited = new HashSet<int> { instructionIndex };
    var worklist = new Queue<int>();
    worklist.Enqueue(instructionIndex);

    while (worklist.Count > 0) {
      int current = worklist.Dequeue();
      var instr = flatInstructions_[current];

      foreach (string reg in instr.RegistersRead) {
        var def = GetReachingDefinition(current, reg);

        if (def.State == ReachingDefinitionState.Unique && def.DefiningInstructionIndex is int defIndex) {
          if (!visited.Contains(defIndex) && visited.Count >= maxInstructions) {
            wasTruncated = true;
            continue;
          }

          if (visited.Add(defIndex)) {
            worklist.Enqueue(defIndex);
          }
        }
        // Undefined/Ambiguous: no further edge -- an honest dead end, not a guess.
      }
    }

    var result = new List<int>(visited);
    result.Sort();
    return result;
  }
}
