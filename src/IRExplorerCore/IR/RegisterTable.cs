// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;

namespace IRExplorerCore.IR;

public class RegisterTable {
  private Dictionary<string, RegisterIR> registerMap_;
  private List<RegisterIR> virtualRegisters_;

  public RegisterTable() {
    registerMap_ = new Dictionary<string, RegisterIR>();
    virtualRegisters_ = new List<RegisterIR>();
  }

  public RegisterIR this[string name] {
    get => GetRegister(name);
    set => throw new NotImplementedException();
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

  public void AddVirtualRegister(RegisterIR register) {
    //? TODO: Support for gr0-grN, etc
  }

  public void AddRegisterAlias(string registerAlias, string register) {
    registerMap_[registerAlias] = registerMap_[register];
  }

  public void AddRegisterAlias(string registerAlias, RegisterIR register) {
    registerMap_[registerAlias] = register;
  }

  protected void PopulateRegisterTable(RegisterIR[] registers) {
    foreach (var register in registers) {
      PupulateRegisterClass(register);
    }
  }

  private void PupulateRegisterClass(RegisterIR register) {
    registerMap_[register.Name] = register;

    foreach (var subreg in register.Subregisters) {
      PupulateRegisterClass(subreg);
    }
  }
}
