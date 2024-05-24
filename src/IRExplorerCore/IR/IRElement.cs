// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;

namespace IRExplorerCore.IR;

public class IRElement : TaggedObject {
  public IRElement() {
    // Used by object pool allocation only.
  }

  public IRElement(IRElementId elementId) {
    Id = elementId;
    TextLocation = default(TextLocation);
    TextLength = 0;
    Tags = null;
  }

  public IRElement(TextLocation location, int length) {
    TextLocation = location;
    TextLength = length;
    Tags = null;
  }

  public virtual bool HasName => false;
  public virtual ReadOnlyMemory<char> NameValue => null;

  public virtual string Name {
    get => NameValue.ToString();
    set => throw new NotImplementedException();
  }

  public ulong Id { get; set; }
  public TextLocation TextLocation { get; set; }
  public int TextLength { get; set; }
  public ulong BlockId => IRElementId.FromLong(Id).BlockId;
  public ulong TupleId => IRElementId.FromLong(Id).TupleId;
  public ulong OperandId => IRElementId.FromLong(Id).OperandId;

  public TupleIR ParentTuple {
    get {
      return this switch {
        TupleIR _   => this as TupleIR,
        OperandIR _ => ((OperandIR)this).Parent,
        _           => null
      };
    }
  }

  public InstructionIR ParentInstruction {
    get {
      return this switch {
        InstructionIR _ => this as InstructionIR,
        OperandIR _     => ((OperandIR)this).Parent as InstructionIR,
        _               => null
      };
    }
  }

  public BlockIR ParentBlock {
    get {
      if (this is BlockIR) {
        return this as BlockIR;
      }

      return ParentTuple?.Parent;
    }
  }

  public FunctionIR ParentFunction {
    get {
      var block = ParentBlock;
      return block?.Parent;
    }
  }

  public virtual void Accept(IRVisitor visitor) {
    visitor.Visit(this);
  }

  public void SetTextRange(TextLocation location, int length) {
    TextLocation = location;
    TextLength = length;
  }

  public ReadOnlyMemory<char> GetText(string source) {
    return source.AsMemory(TextLocation.Offset, TextLength);
  }

  public ReadOnlyMemory<char> GetText(ReadOnlyMemory<char> source) {
    return source.Slice(TextLocation.Offset, TextLength);
  }

  public override bool Equals(object obj) {
    return ReferenceEquals(this, obj) || obj is IRElement other && Equals(other);
  }

  public override int GetHashCode() {
    return Id.GetHashCode();
  }

  protected bool Equals(IRElement other) {
    return Id == other.Id;
  }
}
