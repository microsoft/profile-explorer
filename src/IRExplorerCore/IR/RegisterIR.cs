using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public sealed class RegisterIR {
        public object Register { get; set; }

        public T RegisterAs<T>() where T : Enum {
            return (T)Register;
        }

        public bool RegisterIs<T>(T value) where T : Enum {
            return Register != null && ((T)Register).Equals(value);
        }

        public string Name { get; set; }
        public int BitOffset { get; set; }
        public int BitSize { get; set; }
        public RegisterIR Parent { get; set; }
        public RegisterIR Root {
            get {
                var current = this;
                var parent = Parent;

                while(parent != null) {
                    current = parent;
                    parent = current.Parent;
                }

                return current;
            }
        }
        public List<RegisterIR> Subregisters;

        public RegisterIR(string name, object register, int bitOffset, int bitSize) {
            Register = register;
            Name = name;
            BitOffset = bitOffset;
            BitSize = bitSize;
            Parent = null;
            Subregisters = new List<RegisterIR>();
        }

        public RegisterIR(string name, object register, int bitOffset, int bitSize, 
                          params RegisterIR[] subregisters) : 
            this(name, register, bitOffset, bitSize) {
            foreach (var subreg in subregisters) {
                subreg.Parent = this;
                Subregisters.Add(subreg);
            }
        }

        public bool IsSubregister => Parent != null;
        public bool HasSubregisters => Subregisters.Count > 0;
        public bool OverlapsWith(RegisterIR other) {
            //? TODO: ALso check bit range overlap
            return other.Root == Root;
        }

        public bool CompletelyOverlapsWith(RegisterIR other) {
            //? TODO: Impelement
            return false;
        }

        public override bool Equals(object obj) {
            return obj is RegisterIR iR &&
                   EqualityComparer<object>.Default.Equals(Register, iR.Register) &&
                   BitOffset == iR.BitOffset &&
                   BitSize == iR.BitSize;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Register, BitOffset, BitSize);
        }

        public override string ToString() {
            return $"{Name}";
        }
    }
}
