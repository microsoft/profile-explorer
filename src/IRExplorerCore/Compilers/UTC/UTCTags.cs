// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.UTC {
    [Flags]
    public enum SymbolAnnotationKind {
        None = 0,
        Volatile = 1 << 0,     // ^
        Writethrough = 1 << 1, // ~
        CantMakeSDSU = 1 << 2, // -
        Dead = 1 << 3          // !
    }

    public class SymbolAnnotationTag : ITag {
        public SymbolAnnotationTag() {

        }

        public SymbolAnnotationKind Kind { get; set; }
        public string Name => "Symbol annotation";

        public bool HasVolatile => Kind.HasFlag(SymbolAnnotationKind.Volatile);
        public bool HasWritethrough => Kind.HasFlag(SymbolAnnotationKind.Writethrough);
        public bool HasCantMakeSDSU => Kind.HasFlag(SymbolAnnotationKind.CantMakeSDSU);

        public void AddKind(SymbolAnnotationKind kind) {
            Kind |= kind;
        }

        public TaggedObject Owner { get; set; }

        public override string ToString() {
            return $"symbol annotation: {Kind}";
        }

        public override bool Equals(object obj) {
            return obj is SymbolAnnotationTag tag &&
                   Kind == tag.Kind;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Kind);
        }
    }

    public class SymbolOffsetTag : ITag {
        public SymbolOffsetTag(long offset) {
            Offset = offset;
        }

        public long Offset { get; set; }
        public string Name => "Symbol offset";
        public TaggedObject Owner { get; set; }

        public override bool Equals(object obj) {
            return obj is SymbolOffsetTag tag &&
                   Offset == tag.Offset;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Offset);
        }

        public override string ToString() {
            return $"symbol offset: {Offset}";
        }
    }
    public class PointsAtSetTag : ITag {
        public PointsAtSetTag(int pas) {
            Pas = pas;
        }

        public int Pas { get; set; }
        public string Name => "PointsAtSet";
        public TaggedObject Owner { get; set; }

        public override bool Equals(object obj) {
            return obj is PointsAtSetTag tag &&
                   Pas == tag.Pas;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Pas);
        }

        public override string ToString() {
            return $"pas: {Pas}";
        }
    }

    public class InterferenceTag : ITag {
        public Dictionary<int, HashSet<int>> InterferingPasMap { get; }
        public Dictionary<int, List<string>> PasToSymMap { get; }
        public Dictionary<string, int> SymToPasMap { get; }

        public string Name => "Interference";
        public TaggedObject Owner { get; set; }

        public InterferenceTag() {
            InterferingPasMap = new Dictionary<int, HashSet<int>>();
            PasToSymMap = new Dictionary<int, List<string>>();
            SymToPasMap = new Dictionary<string, int>();
        }

        public override string ToString() {
            return $"interf: pass count {PasToSymMap.Count}";
        }
    }
}
