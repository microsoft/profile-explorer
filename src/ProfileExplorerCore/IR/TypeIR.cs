// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;

namespace ProfileExplorer.Core.IR;

public enum TypeKind {
  Integer,
  Float,
  Vector,
  Multibyte,
  Array,
  Struct,
  Pointer,
  Void,
  Bool,
  Unknown
}

[Flags]
public enum TypeFlags {
  None = 0,
  SignedInt = 1,
  UnsignedInt = 2
}

public sealed class TypeIR {
  private static readonly TypeIR boolType_ = new(TypeKind.Integer, 0);
  private static readonly TypeIR doubleType_ = new(TypeKind.Float, 8);
  private static readonly TypeIR floatType_ = new(TypeKind.Float, 4);
  private static readonly TypeIR[] signedIntTypes_ = {
    new(TypeKind.Integer, 1, TypeFlags.SignedInt),
    new(TypeKind.Integer, 2, TypeFlags.SignedInt),
    new(TypeKind.Integer, 4, TypeFlags.SignedInt),
    new(TypeKind.Integer, 8, TypeFlags.SignedInt)
  };
  private static readonly ConcurrentDictionary<TypeIR, TypeIR> uniqueTypes_ = new();
  private static readonly TypeIR unknownType_ = new(TypeKind.Unknown, 0);
  private static readonly TypeIR[] unsignedIntTypes_ = {
    new(TypeKind.Integer, 1, TypeFlags.UnsignedInt),
    new(TypeKind.Integer, 2, TypeFlags.UnsignedInt),
    new(TypeKind.Integer, 4, TypeFlags.UnsignedInt),
    new(TypeKind.Integer, 8, TypeFlags.UnsignedInt)
  };
  private static readonly TypeIR voidType_ = new(TypeKind.Void, 0);

  private TypeIR(TypeKind kind, int size, TypeFlags flags = TypeFlags.None) {
    Kind = kind;
    Size = size;
    Flags = flags;
  }

  public bool IsFloat => Kind == TypeKind.Float;
  public bool IsInt => Kind == TypeKind.Integer && Flags != TypeFlags.UnsignedInt;
  public bool IsInteger => Kind == TypeKind.Integer;
  public bool IsMultibyte => Kind == TypeKind.Multibyte;
  public bool IsUInt => Kind == TypeKind.Integer && Flags == TypeFlags.UnsignedInt;
  public bool IsUnknown => Kind == TypeKind.Unknown;
  public bool IsVoid => Kind == TypeKind.Void;
  private TypeFlags Flags { get; set; }
  private TypeKind Kind { get; set; }
  private int Size { get; set; }

  public static TypeIR GetBool() {
    return boolType_;
  }

  public static TypeIR GetDouble() {
    return doubleType_;
  }

  public static TypeIR GetFloat() {
    return floatType_;
  }

  public static TypeIR GetInt(int size) {
    return size switch {
      1 => signedIntTypes_[0],
      2 => signedIntTypes_[1],
      4 => signedIntTypes_[2],
      8 => signedIntTypes_[3],
      _ => GetType(TypeKind.Integer, size, TypeFlags.SignedInt)
    };
  }

  public static TypeIR GetInt16() {
    return signedIntTypes_[1];
  }

  public static TypeIR GetInt32() {
    return signedIntTypes_[2];
  }

  public static TypeIR GetInt64() {
    return signedIntTypes_[3];
  }

  public static TypeIR GetInt8() {
    return signedIntTypes_[0];
  }

  public static TypeIR GetMultibyte(int size) {
    return GetType(TypeKind.Multibyte, size);
  }

  public static TypeIR GetType(TypeKind kind, int size,
                               TypeFlags flags = TypeFlags.None) {
    var type = new TypeIR(kind, size, flags);
    return uniqueTypes_.GetOrAdd(type, type);
  }

  public static TypeIR GetUInt(int size) {
    return size switch {
      1 => unsignedIntTypes_[0],
      2 => unsignedIntTypes_[1],
      4 => unsignedIntTypes_[2],
      8 => unsignedIntTypes_[3],
      _ => GetType(TypeKind.Integer, size, TypeFlags.UnsignedInt)
    };
  }

  public static TypeIR GetUInt16() {
    return unsignedIntTypes_[1];
  }

  public static TypeIR GetUInt32() {
    return unsignedIntTypes_[2];
  }

  public static TypeIR GetUInt64() {
    return unsignedIntTypes_[3];
  }

  public static TypeIR GetUInt8() {
    return unsignedIntTypes_[0];
  }

  public static TypeIR GetUnknown() {
    return unknownType_;
  }

  public static TypeIR GetVoid() {
    return voidType_;
  }

  public override bool Equals(object obj) {
    return ReferenceEquals(this, obj) || obj is TypeIR other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine((int)Flags, (int)Kind, Size);
  }

  public override string ToString() {
    switch (Kind) {
      case TypeKind.Integer: {
        return IsUInt ? $"uint{Size * 8}" : $"int{Size * 8}";
      }
      case TypeKind.Float:
        return $"float{Size * 8}";
      case TypeKind.Vector:
        return $"vector{Size * 8}";
      case TypeKind.Multibyte:
        return $"mb{Size}";
      case TypeKind.Array:
        return "<TODO:array>";
      case TypeKind.Struct:
        return "<TODO:struct>";
      case TypeKind.Pointer:
        return "<TODO:ptr>";
      case TypeKind.Void:
        return "void";
      case TypeKind.Bool:
        return "bool";
      case TypeKind.Unknown:
        return "unk";
    }

    return "<unexpected>";
  }

  private bool Equals(TypeIR other) {
    return Flags == other.Flags && Kind == other.Kind && Size == other.Size;
  }
}