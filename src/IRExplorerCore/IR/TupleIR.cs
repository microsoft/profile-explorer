// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
namespace IRExplorerCore.IR;

public enum TupleKind {
  Instruction,
  Label,
  Exception,
  Metadata,
  Other
}

public class TupleIR : IRElement {
  public TupleIR(IRElementId elementId, TupleKind kind, BlockIR parent) :
    base(elementId.NextTuple()) {
    Kind = kind;
    Parent = parent;
  }

  public BlockIR Parent { get; set; }
  public int IndexInBlock { get; set; }
  public bool HasOddIndexInBlock => (IndexInBlock & 1) == 1;
  public bool HasEvenIndexInBlock => (IndexInBlock & 1) == 0;
  public TupleKind Kind { get; set; }
  public bool IsInstruction => Kind == TupleKind.Instruction;
  public bool IsLabel => Kind == TupleKind.Label;
  public bool IsException => Kind == TupleKind.Exception;
  public bool IsMetadata => Kind == TupleKind.Metadata;

  public override void Accept(IRVisitor visitor) {
    visitor.Visit(this);
  }

  public override string ToString() {
    return $"tuple kind: {Kind}, id: {Id}";
  }
}
