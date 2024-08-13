// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Diagnostics;

namespace ProfileExplorer.Core.IR;

public enum OperandKind {
  Variable,
  Temporary,
  IntConstant,
  FloatConstant,
  Indirection,
  Address,
  LabelAddress,
  Other
}

public enum OperandRole {
  Source,
  Destination,
  Parameter,
  Other
}

public sealed class OperandIR : IRElement {
  public OperandIR() {
    // Used by object pool allocation only.
  }

  public OperandIR(IRElementId elementId, OperandKind kind, TypeIR type, TupleIR parent)
    : base(elementId.NextOperand()) {
    Kind = kind;
    Type = type;
    Parent = parent;
    Value = null;
  }

  public OperandKind Kind { get; set; }
  public OperandRole Role { get; set; }
  public bool IsSourceOperand => Role == OperandRole.Source;
  public bool IsDestinationOperand => Role == OperandRole.Destination;
  public bool IsParameterOperand => Role == OperandRole.Parameter;
  public TypeIR Type { get; set; }
  public TupleIR Parent { get; set; }

  //? TODO: Boxing (allocating heap value) can be avoided to reduce GC pressure:
  //? https://stackoverflow.com/questions/6163335/how-to-store-structs-of-different-types-without-boxing
  public object Value { get; set; }
  public bool IsVariable => Kind == OperandKind.Variable;
  public bool IsTemporary => Kind == OperandKind.Temporary;
  public bool IsConstant =>
    Kind == OperandKind.IntConstant || Kind == OperandKind.FloatConstant;
  public bool IsIntConstant => Kind == OperandKind.IntConstant;
  public bool IsFloatConstant => Kind == OperandKind.FloatConstant;
  public bool IsAddress => Kind == OperandKind.Address;
  public bool IsLabelAddress => Kind == OperandKind.LabelAddress;
  public bool IsIndirection => Kind == OperandKind.Indirection;

  public long IntValue {
    get {
      Debug.Assert(Kind == OperandKind.IntConstant);
      Debug.Assert(Value is long);
      return (long)Value;
    }
  }

  public double FloatValue {
    get {
      Debug.Assert(Kind == OperandKind.FloatConstant);
      Debug.Assert(Value is double);
      return (double)Value;
    }
  }

  public override bool HasName =>
    Kind == OperandKind.Variable ||
    Kind == OperandKind.Temporary ||
    Kind == OperandKind.Address ||
    Kind == OperandKind.LabelAddress;

  public override ReadOnlyMemory<char> NameValue {
    get {
      Debug.Assert(HasName);

      return Kind switch {
        OperandKind.Address when Value is OperandIR ir => ir.NameValue,
        OperandKind.LabelAddress                       => BlockLabelValue.NameValue,
        _                                              => (ReadOnlyMemory<char>)Value
      };
    }
  }

  public OperandIR IndirectionBaseValue {
    get {
      Debug.Assert(Kind == OperandKind.Indirection);
      Debug.Assert(Value is OperandIR);
      return (OperandIR)Value;
    }
  }

  public BlockLabelIR BlockLabelValue {
    get {
      Debug.Assert(Kind == OperandKind.LabelAddress);
      Debug.Assert(Value is BlockLabelIR);
      return (BlockLabelIR)Value;
    }
  }

  public override void Accept(IRVisitor visitor) {
    visitor.Visit(this);
  }

  public override string ToString() {
    string result = Kind switch {
      OperandKind.Variable      => $"var {Value}.{Type}",
      OperandKind.Temporary     => $"temp {Value}.{Type}",
      OperandKind.IntConstant   => $"intconst {Value}.{Type}",
      OperandKind.FloatConstant => $"floatconst {Value}.{Type}",
      OperandKind.Indirection   => $"indir {Value}.{Type}",
      OperandKind.Address       => $"address {Value}.{Type}",
      OperandKind.LabelAddress  => $"label {Value}.{Type}",
      OperandKind.Other         => "other",
      _                         => "<unexpected>"
    };

    var ssaTag = GetTag<ISSAValue>();

    if (ssaTag != null) {
      result += $"<{ssaTag.DefinitionId}>";
    }

    return $"{result}, id: {Id}";
  }
}