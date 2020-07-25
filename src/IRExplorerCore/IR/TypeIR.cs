// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;

namespace IRExplorerCore.IR {
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
        private static TypeIR boolType_ = new TypeIR(TypeKind.Integer, 0);
        private static TypeIR doubleType_ = new TypeIR(TypeKind.Float, 8);
        private static TypeIR floatType_ = new TypeIR(TypeKind.Float, 4);

        private static TypeIR[] signedIntTypes_ = {
            new TypeIR(TypeKind.Integer, 1, TypeFlags.SignedInt),
            new TypeIR(TypeKind.Integer, 2, TypeFlags.SignedInt),
            new TypeIR(TypeKind.Integer, 4, TypeFlags.SignedInt),
            new TypeIR(TypeKind.Integer, 8, TypeFlags.SignedInt)
        };

        private static ConcurrentDictionary<TypeIR, TypeIR> uniqueTypes_ =
            new ConcurrentDictionary<TypeIR, TypeIR>();
        private static TypeIR unknownType_ = new TypeIR(TypeKind.Unknown, 0);

        private static TypeIR[] unsignedIntTypes_ = {
            new TypeIR(TypeKind.Integer, 1, TypeFlags.UnsignedInt),
            new TypeIR(TypeKind.Integer, 2, TypeFlags.UnsignedInt),
            new TypeIR(TypeKind.Integer, 4, TypeFlags.UnsignedInt),
            new TypeIR(TypeKind.Integer, 8, TypeFlags.UnsignedInt)
        };

        private static TypeIR voidType_ = new TypeIR(TypeKind.Void, 0);

        private TypeIR(TypeKind kind, int size, TypeFlags flags = TypeFlags.None) {
            Kind = kind;
            Size = size;
            Flags = flags;
        }

        private TypeFlags Flags { get; set; }
        private TypeKind Kind { get; set; }
        private int Size { get; set; }

        public bool IsFloat => Kind == TypeKind.Float;

        public bool IsInt => Kind == TypeKind.Integer && Flags != TypeFlags.UnsignedInt;

        public bool IsInteger => Kind == TypeKind.Integer;
        public bool IsMultibyte => Kind == TypeKind.Multibyte;

        public bool IsUInt => Kind == TypeKind.Integer && Flags == TypeFlags.UnsignedInt;

        public bool IsUnknown => Kind == TypeKind.Unknown;
        public bool IsVoid => Kind == TypeKind.Void;

        public override bool Equals(object obj) {
            return obj is TypeIR type &&
                   Kind == type.Kind &&
                   Size == type.Size &&
                   Flags == type.Flags;
        }

        public static TypeIR GetBool() {
            return boolType_;
        }

        public static TypeIR GetDouble() {
            return doubleType_;
        }

        public static TypeIR GetFloat() {
            return floatType_;
        }

        public override int GetHashCode() {
            int hashCode = -438129221;
            hashCode = hashCode * -1521134295 + Kind.GetHashCode();
            hashCode = hashCode * -1521134295 + Size.GetHashCode();
            hashCode = hashCode * -1521134295 + Flags.GetHashCode();
            return hashCode;
        }

        public static TypeIR GetInt(int size) {
            return size switch
            {
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
            return size switch
            {
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

        public override string ToString() {
            switch (Kind) {
                case TypeKind.Integer: {
                    if (IsUInt) {
                        return $"uint{Size * 8}";
                    }
                    else {
                        return $"int{Size * 8}";
                    }
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
                    return "<TODO:array>";
                case TypeKind.Pointer:
                    return "<TODO:array>";
                case TypeKind.Void:
                    return "void";
                case TypeKind.Bool:
                    return "bool";
                case TypeKind.Unknown:
                    return "unknown";
            }

            return "<unexpected>";
        }
    }
}
