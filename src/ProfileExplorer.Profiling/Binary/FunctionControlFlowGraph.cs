// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// One basic block: a maximal straight-line instruction run with a single entry (only reached at
/// its first instruction) and single exit (only leaves at its last instruction). Boundaries are
/// derived purely from control-transfer facts already decoded onto <see cref="SemanticInstruction"/>
/// (resolved branch targets, conditional/unconditional/return classification) -- never from text
/// parsing or scanning.
/// </summary>
public sealed class BasicBlock {
  public int Id { get; init; }
  public long StartRva { get; init; }

  /// <summary>Exclusive end -- the RVA immediately after the block's last instruction.</summary>
  public long EndRva { get; init; }

  public IReadOnlyList<SemanticInstruction> Instructions { get; init; } = Array.Empty<SemanticInstruction>();

  public List<int> SuccessorIds { get; } = new();
  public List<int> PredecessorIds { get; } = new();

  /// <summary>True when the block's last instruction is a return -- an exit with no successors.</summary>
  public bool EndsInReturn { get; init; }

  /// <summary>
  /// True when the block's last instruction is an unconditional jump/branch whose target could not
  /// be statically resolved (e.g. an indirect jump through a register/computed address) --
  /// surfaced explicitly as a reason the block has no taken-branch successor, rather than silently
  /// treating it as an unexplained dead end.
  /// </summary>
  public bool EndsInUnresolvedIndirectTransfer { get; init; }

  /// <summary>
  /// True when the block's last instruction jumps/branches to a resolved target outside the
  /// analyzed function's [StartRva, EndRva) range entirely (e.g. a tail call) -- the target address
  /// is known, it just isn't part of this function's CFG.
  /// </summary>
  public bool EndsInOutOfRangeTransfer { get; init; }

  /// <summary>True for any block with no successors (return, unresolved indirect transfer, or
  /// out-of-range transfer) -- a terminal node of this function's CFG.</summary>
  public bool IsExit => SuccessorIds.Count == 0;
}

/// <summary>
/// A natural loop: identified from a back edge (<see cref="BackEdgeSourceBlockId"/> -&gt;
/// <see cref="HeaderBlockId"/>) where the header dominates the edge's source. <see cref="BodyBlockIds"/>
/// is every block (including the header) that can reach <see cref="BackEdgeSourceBlockId"/> without
/// leaving the loop through the header -- the standard definition used for loop-nesting analysis.
/// </summary>
public sealed record NaturalLoop(int HeaderBlockId, int BackEdgeSourceBlockId, IReadOnlyList<int> BodyBlockIds);

/// <summary>
/// Function-scoped control flow graph, built purely from statically-resolved control transfers
/// within one function's <c>[FunctionStartRva, FunctionEndRva)</c> range -- the CFG tier of the
/// reverse-engineering enhancement plan's evidence package. Calls are never CFG edges (the callee
/// is a different function/graph); only intraprocedural branches/jumps/fallthrough are.
/// </summary>
public sealed class FunctionControlFlowGraph {
  public long FunctionStartRva { get; init; }
  public long FunctionEndRva { get; init; }
  public IReadOnlyList<BasicBlock> Blocks { get; init; } = Array.Empty<BasicBlock>();
  public int EntryBlockId { get; init; }

  /// <summary>
  /// Immediate dominator block id for each *reachable* block id (the entry block is its own idom).
  /// Blocks unreachable from the entry (e.g. dead code after an unconditional jump with nothing
  /// branching back to it) are deliberately absent rather than assigned a fabricated dominator.
  /// </summary>
  public IReadOnlyDictionary<int, int> ImmediateDominator { get; init; } = new Dictionary<int, int>();

  public IReadOnlyList<NaturalLoop> Loops { get; init; } = Array.Empty<NaturalLoop>();

  public BasicBlock GetBlock(int id) => Blocks[id];

  /// <summary>
  /// True when <paramref name="candidateDominatorId"/> dominates <paramref name="blockId"/> (every
  /// path from the entry block to <paramref name="blockId"/> passes through it) -- includes the
  /// reflexive case (a block dominates itself). False for either id if <paramref name="blockId"/>
  /// isn't reachable from the entry (no dominance information exists).
  /// </summary>
  public bool Dominates(int candidateDominatorId, int blockId) {
    return DominatesCore(ImmediateDominator, candidateDominatorId, blockId);
  }

  /// <summary>
  /// Builds a CFG for one function from its already-decoded semantic instructions (see
  /// <see cref="Disassembler.DisassembleToSemanticList(long, long)"/>, which must have been
  /// produced by a disassembler created with semantic detail enabled so branch targets are
  /// available). Returns null when <paramref name="instructions"/> is empty -- never fabricates a
  /// graph out of nothing.
  /// </summary>
  public static FunctionControlFlowGraph? Build(IReadOnlyList<SemanticInstruction> instructions,
                                                long functionStartRva, long functionEndRva) {
    if (instructions.Count == 0) {
      return null;
    }

    var leaders = ComputeLeaders(instructions, functionStartRva, functionEndRva);
    var (blocks, blockIdByStartRva) = BuildBlocks(instructions, leaders, functionStartRva, functionEndRva);

    if (blocks.Count == 0) {
      return null;
    }

    WireEdges(blocks, blockIdByStartRva, functionStartRva, functionEndRva);

    int entryBlockId = blockIdByStartRva.TryGetValue(functionStartRva, out int eid) ? eid : 0;
    var idom = ComputeDominators(blocks, entryBlockId);
    var loops = FindNaturalLoops(blocks, idom);

    return new FunctionControlFlowGraph {
      FunctionStartRva = functionStartRva,
      FunctionEndRva = functionEndRva,
      Blocks = blocks,
      EntryBlockId = entryBlockId,
      ImmediateDominator = idom,
      Loops = loops
    };
  }

  private static SortedSet<long> ComputeLeaders(IReadOnlyList<SemanticInstruction> instructions,
                                                long functionStartRva, long functionEndRva) {
    var leaders = new SortedSet<long> { instructions[0].Rva };

    for (int i = 0; i < instructions.Count; i++) {
      var instr = instructions[i];
      bool endsBlock = instr.IsReturn || instr.IsUnconditionalJump || instr.IsConditionalBranch;

      if (instr.TargetRva is long targetRva && targetRva >= functionStartRva && targetRva < functionEndRva) {
        leaders.Add(targetRva);
      }

      if (endsBlock && i + 1 < instructions.Count) {
        leaders.Add(instructions[i + 1].Rva);
      }
    }

    return leaders;
  }

  private static (List<BasicBlock> Blocks, Dictionary<long, int> BlockIdByStartRva) BuildBlocks(
      IReadOnlyList<SemanticInstruction> instructions, SortedSet<long> leaders,
      long functionStartRva, long functionEndRva) {
    var leaderList = leaders.ToList();
    var blocks = new List<BasicBlock>(leaderList.Count);
    var blockIdByStartRva = new Dictionary<long, int>(leaderList.Count);
    int instrIndex = 0;

    for (int b = 0; b < leaderList.Count; b++) {
      long blockStart = leaderList[b];
      long blockEndExclusive = b + 1 < leaderList.Count ? leaderList[b + 1] : functionEndRva;

      // Advance past any instructions that precede this leader -- shouldn't happen given how
      // leaders are derived from the same instruction list, but guards against silently
      // misaligning blocks if it ever does.
      while (instrIndex < instructions.Count && instructions[instrIndex].Rva < blockStart) {
        instrIndex++;
      }

      var blockInstructions = new List<SemanticInstruction>();

      while (instrIndex < instructions.Count && instructions[instrIndex].Rva < blockEndExclusive) {
        blockInstructions.Add(instructions[instrIndex]);
        instrIndex++;
      }

      if (blockInstructions.Count == 0) {
        continue; // A leader RVA with no decoded instructions before the next leader/function end.
      }

      var lastInstr = blockInstructions[^1];
      bool isTransfer = lastInstr.IsUnconditionalJump || lastInstr.IsConditionalBranch;
      bool endsInUnresolvedIndirect = isTransfer && lastInstr.TargetRva == null;
      bool endsInOutOfRange = isTransfer && lastInstr.TargetRva is long t &&
                              !(t >= functionStartRva && t < functionEndRva);

      blockIdByStartRva[blockStart] = blocks.Count;
      blocks.Add(new BasicBlock {
        Id = blocks.Count,
        StartRva = blockStart,
        EndRva = lastInstr.Rva + lastInstr.Size,
        Instructions = blockInstructions,
        EndsInReturn = lastInstr.IsReturn,
        EndsInUnresolvedIndirectTransfer = endsInUnresolvedIndirect,
        EndsInOutOfRangeTransfer = endsInOutOfRange
      });
    }

    return (blocks, blockIdByStartRva);
  }

  private static void WireEdges(List<BasicBlock> blocks, Dictionary<long, int> blockIdByStartRva,
                                long functionStartRva, long functionEndRva) {
    for (int b = 0; b < blocks.Count; b++) {
      var block = blocks[b];
      var lastInstr = block.Instructions[^1];

      if (lastInstr.IsReturn) {
        continue; // No successors -- an exit block.
      }

      if (lastInstr.IsUnconditionalJump) {
        // No fallthrough for an unconditional jump, resolved or not.
        if (lastInstr.TargetRva is long targetRva && blockIdByStartRva.TryGetValue(targetRva, out int targetBlockId)) {
          AddEdge(blocks, b, targetBlockId);
        }

        continue;
      }

      if (lastInstr.IsConditionalBranch) {
        // A conditional branch always has a fallthrough successor in addition to its taken target.
        if (lastInstr.TargetRva is long targetRva && blockIdByStartRva.TryGetValue(targetRva, out int targetBlockId)) {
          AddEdge(blocks, b, targetBlockId);
        }

        if (blockIdByStartRva.TryGetValue(block.EndRva, out int fallthroughId)) {
          AddEdge(blocks, b, fallthroughId);
        }

        continue;
      }

      // Block wasn't split by its own last instruction (it was split because some *other*
      // instruction's target landed here) -- plain fallthrough into whatever comes next.
      if (blockIdByStartRva.TryGetValue(block.EndRva, out int nextId)) {
        AddEdge(blocks, b, nextId);
      }
    }
  }

  private static void AddEdge(List<BasicBlock> blocks, int fromId, int toId) {
    if (!blocks[fromId].SuccessorIds.Contains(toId)) {
      blocks[fromId].SuccessorIds.Add(toId);
    }

    if (!blocks[toId].PredecessorIds.Contains(fromId)) {
      blocks[toId].PredecessorIds.Add(fromId);
    }
  }

  /// <summary>
  /// Standard iterative dominance algorithm (Cooper, Harvey &amp; Kennedy, "A Simple, Fast
  /// Dominance Algorithm"). Only blocks reachable from <paramref name="entryId"/> via successor
  /// edges participate; unreachable blocks are absent from the result.
  /// </summary>
  private static Dictionary<int, int> ComputeDominators(List<BasicBlock> blocks, int entryId) {
    var (visited, postorder) = ComputePostorder(blocks, entryId);

    var postOrderNumber = new Dictionary<int, int>(postorder.Count);

    for (int i = 0; i < postorder.Count; i++) {
      postOrderNumber[postorder[i]] = i;
    }

    var reversePostorder = new List<int>(postorder);
    reversePostorder.Reverse();

    var idom = new Dictionary<int, int> { [entryId] = entryId };
    bool changed = true;

    while (changed) {
      changed = false;

      foreach (int b in reversePostorder) {
        if (b == entryId || !visited.Contains(b)) {
          continue;
        }

        int? newIdom = null;

        foreach (int pred in blocks[b].PredecessorIds) {
          if (!idom.ContainsKey(pred)) {
            continue; // Predecessor not yet processed this pass.
          }

          newIdom = newIdom is null ? pred : Intersect(newIdom.Value, pred, idom, postOrderNumber);
        }

        if (newIdom is int computed && (!idom.TryGetValue(b, out int existing) || existing != computed)) {
          idom[b] = computed;
          changed = true;
        }
      }
    }

    return idom;
  }

  private static (HashSet<int> Visited, List<int> Postorder) ComputePostorder(List<BasicBlock> blocks, int entryId) {
    var visited = new HashSet<int> { entryId };
    var postorder = new List<int>();
    var stack = new Stack<(int Id, int NextChildIndex)>();
    stack.Push((entryId, 0));

    // Iterative (not recursive) DFS postorder -- avoids any recursion-depth concern on unusually
    // large decompiled functions.
    while (stack.Count > 0) {
      var (id, childIndex) = stack.Pop();
      var successors = blocks[id].SuccessorIds;

      if (childIndex < successors.Count) {
        stack.Push((id, childIndex + 1));
        int next = successors[childIndex];

        if (visited.Add(next)) {
          stack.Push((next, 0));
        }
      }
      else {
        postorder.Add(id);
      }
    }

    return (visited, postorder);
  }

  private static int Intersect(int b1, int b2, Dictionary<int, int> idom, Dictionary<int, int> postOrderNumber) {
    while (b1 != b2) {
      while (postOrderNumber[b1] < postOrderNumber[b2]) {
        b1 = idom[b1];
      }

      while (postOrderNumber[b2] < postOrderNumber[b1]) {
        b2 = idom[b2];
      }
    }

    return b1;
  }

  private static bool DominatesCore(IReadOnlyDictionary<int, int> idom, int candidateDominatorId, int blockId) {
    int current = blockId;

    while (true) {
      if (current == candidateDominatorId) {
        return true;
      }

      if (!idom.TryGetValue(current, out int parent) || parent == current) {
        return current == candidateDominatorId;
      }

      current = parent;
    }
  }

  /// <summary>
  /// A back edge is any CFG edge (b -&gt; h) where h dominates b. Its natural loop body is h plus
  /// every block that can reach b without passing back through h, found via a reverse (predecessor)
  /// walk from b that stops at h.
  /// </summary>
  private static List<NaturalLoop> FindNaturalLoops(List<BasicBlock> blocks, IReadOnlyDictionary<int, int> idom) {
    var loops = new List<NaturalLoop>();

    foreach (var block in blocks) {
      foreach (int succId in block.SuccessorIds) {
        if (!DominatesCore(idom, succId, block.Id)) {
          continue;
        }

        var body = new HashSet<int> { succId };
        var worklist = new Stack<int>();

        if (block.Id != succId) {
          body.Add(block.Id);
          worklist.Push(block.Id);
        }

        while (worklist.Count > 0) {
          int current = worklist.Pop();

          foreach (int predId in blocks[current].PredecessorIds) {
            if (predId != succId && body.Add(predId)) {
              worklist.Push(predId);
            }
          }
        }

        loops.Add(new NaturalLoop(succId, block.Id, body.OrderBy(x => x).ToList()));
      }
    }

    return loops;
  }
}
