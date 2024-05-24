// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;

namespace IRExplorerCore.IR;

public sealed class FunctionIR : IRElement {
  private Dictionary<ulong, IRElement> elementMap_;
  private int instructionCount_;
  private int tupleCount_;
  private string name_;

  public FunctionIR(string name = null) : base(IRElementId.NewFunctionId()) {
    ReturnType = TypeIR.GetUnknown();
    Parameters = new List<OperandIR>();
    Blocks = new List<BlockIR>();
    name_ = name;
  }

  public FunctionIR(string name, TypeIR returnType) : this() {
    name_ = name;
    ReturnType = returnType;
  }

  public override bool HasName => !string.IsNullOrEmpty(Name);
  public override ReadOnlyMemory<char> NameValue => name_.AsMemory();

  public override string Name {
    get => name_;
    set => name_ = value;
  }

  public TypeIR ReturnType { get; set; }
  public List<OperandIR> Parameters { get; }
  public List<BlockIR> Blocks { get; }
  public List<BlockIR> SortedBlocks { get; private set; }
  public BlockIR EntryBlock => Blocks.Count > 0 ? Blocks[0] : null;
  public BlockIR ExitBlock => Blocks.Count > 0 ? Blocks[^1] : null;
  public int BlockCount => Blocks.Count;

  public IEnumerable<IRElement> AllElements {
    get {
      foreach (var block in Blocks) {
        yield return block;

        foreach (var tuple in block.Tuples) {
          yield return tuple;

          if (!(tuple is InstructionIR instr)) {
            continue;
          }

          foreach (var op in instr.Destinations) {
            yield return op;
          }

          foreach (var op in instr.Sources) {
            yield return op;
          }
        }
      }
    }
  }

  public IEnumerable<TupleIR> AllTuples {
    get {
      foreach (var block in Blocks) {
        foreach (var tuple in block.Tuples) {
          yield return tuple;
        }
      }
    }
  }

  public IEnumerable<InstructionIR> AllInstructions {
    get {
      foreach (var block in Blocks) {
        foreach (var tuple in block.Tuples) {
          if (tuple is InstructionIR instr) {
            yield return instr;
          }
        }
      }
    }
  }

  public int InstructionCount {
    get {
      if (instructionCount_ == 0) {
        ForEachInstruction(instr => {
          instructionCount_++;
          return true;
        });
      }

      return instructionCount_;
    }
    set => instructionCount_ = value;
  }

  public int TupleCount {
    get {
      if (tupleCount_ == 0) {
        ForEachTuple(tuple => {
          tupleCount_++;
          return true;
        });
      }

      return tupleCount_;
    }
    set => tupleCount_ = value;
  }

  public IRElement GetElementWithId(ulong id) {
    BuildElementIdMap();
    return elementMap_.TryGetValue(id, out var value) ? value : null;
  }

  public void BuildElementIdMap() {
    if (elementMap_ != null) {
      return;
    }

    elementMap_ = new Dictionary<ulong, IRElement>();

    foreach (var block in Blocks) {
      elementMap_[block.Id] = block;

      foreach (var tuple in block.Tuples) {
        elementMap_[tuple.Id] = tuple;

        if (!(tuple is InstructionIR instr)) {
          continue;
        }

        foreach (var op in instr.Destinations) {
          elementMap_[op.Id] = op;
        }

        foreach (var op in instr.Sources) {
          elementMap_[op.Id] = op;
        }
      }
    }
  }

  public void AssignBlockIndices(bool setBlockNumbers = false) {
    // Assign block index as they show up in the text,
    // not RDFO or how the blocks where forward-referenced.
    var blockList = new List<BlockIR>(Blocks.Count);
    bool needsSorting = false;

    for (int i = 0; i < Blocks.Count; i++) {
      var block = Blocks[i];
      blockList.Add(block);
      needsSorting = i > 1 && !needsSorting &&
                     blockList[^1].TextLocation >= block.TextLocation;
    }

    if (needsSorting) {
      blockList.Sort((a, b) => a.TextLocation.CompareTo(b.TextLocation));
    }

    for (int i = 0; i < blockList.Count; i++) {
      blockList[i].IndexInFunction = i;

      if (setBlockNumbers) {
        blockList[i].Number = i;
      }
    }

    SortedBlocks = blockList;
  }

  public void ForEachElement(Func<IRElement, bool> action) {
    foreach (var element in AllElements) {
      if (!action(element)) {
        return;
      }
    }
  }

  public void ForEachTuple(Func<TupleIR, bool> action) {
    foreach (var tuple in AllTuples) {
      if (!action(tuple)) {
        return;
      }
    }
  }

  public void ForEachInstruction(Func<InstructionIR, bool> action) {
    foreach (var instr in AllInstructions) {
      if (!action(instr)) {
        return;
      }
    }
  }

  public override void Accept(IRVisitor visitor) {
    visitor.Visit(this);
  }

  public override bool Equals(object obj) {
    return ReferenceEquals(this, obj);
  }

  public override int GetHashCode() {
    return Name?.GetHashCode(StringComparison.Ordinal) ?? 0;
  }
}
