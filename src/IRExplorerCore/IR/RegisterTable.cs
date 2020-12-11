using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.IR {
    public class RegisterTable {
        private Dictionary<string, RegisterIR> registerMap_;
        private List<RegisterIR> virtualRegisters_;

        protected void PopulateRegisterTable(RegisterIR[] registers) {
            foreach (var register in registers) {
                PupulateRegisterClass(register);
            }
        }

        private void PupulateRegisterClass(RegisterIR register) {
            registerMap_[register.Name] = register;

            foreach(var subreg in register.Subregisters) {
                PupulateRegisterClass(subreg);
            }
        }


        public RegisterTable() {
            registerMap_ = new Dictionary<string, RegisterIR>();
            virtualRegisters_ = new List<RegisterIR>();
        }

        public void AddVirtualRegister(RegisterIR register) {
            //? TODO: Support for gr0-grN, etc
        }

        public void AddRegisterAlias(string register, string registerAlias) {
            //? TODO: Add name aliases representing same register
            //? For ex. UTC IR uses cc_zf instead of zf, etc
        }

        public void AddRegisterAlias(RegisterIR register, string registerAliasa) {

        }

        public virtual RegisterIR GetRegister(string name) {
            if (registerMap_.TryGetValue(name, out var register)) {
                return register;
            }

            //? check if virtual reg
            return null;
        }

        public virtual RegisterIR GetRegister(ReadOnlyMemory<char> name) {
            return null;
        }
    }
}
