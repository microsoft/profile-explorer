// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace ProfileExplorer.Core.IR;

public static class RegisterTables {
  // Static instances to have the tables built only once.
  private static X86RegisterTable x86RegisterTable_;
  private static ARM64RegisterTable arm64RegisterTable_;

  static RegisterTables() {
    x86RegisterTable_ = new X86RegisterTable();
    arm64RegisterTable_ = new ARM64RegisterTable();

    x86RegisterTable_.AddRegisterAlias("cc_zf", "zf");
    x86RegisterTable_.AddRegisterAlias("cc_cf", "cf");
    x86RegisterTable_.AddRegisterAlias("cc_pf", "pf");
    x86RegisterTable_.AddRegisterAlias("cc_sf", "sf");
    x86RegisterTable_.AddRegisterAlias("cc_of", "of");
    x86RegisterTable_.AddRegisterAlias("cc_so", "flags");
    x86RegisterTable_.AddRegisterAlias("cc_soz", "flags");
    x86RegisterTable_.AddRegisterAlias("cc", "flags");

    x86RegisterTable_.AddRegisterAlias("ixmm0", "xmm0");
    x86RegisterTable_.AddRegisterAlias("ixmm1", "xmm1");
    x86RegisterTable_.AddRegisterAlias("ixmm2", "xmm2");
    x86RegisterTable_.AddRegisterAlias("ixmm3", "xmm3");
    x86RegisterTable_.AddRegisterAlias("ixmm4", "xmm4");
    x86RegisterTable_.AddRegisterAlias("ixmm5", "xmm5");
    x86RegisterTable_.AddRegisterAlias("ixmm6", "xmm6");
    x86RegisterTable_.AddRegisterAlias("ixmm7", "xmm7");
    x86RegisterTable_.AddRegisterAlias("ixmm8", "xmm8");
    x86RegisterTable_.AddRegisterAlias("ixmm9", "xmm9");
    x86RegisterTable_.AddRegisterAlias("ixmm10", "xmm10");
    x86RegisterTable_.AddRegisterAlias("ixmm11", "xmm11");
    x86RegisterTable_.AddRegisterAlias("ixmm12", "xmm12");
    x86RegisterTable_.AddRegisterAlias("ixmm13", "xmm13");
    x86RegisterTable_.AddRegisterAlias("ixmm14", "xmm14");
    x86RegisterTable_.AddRegisterAlias("ixmm15", "xmm15");

    x86RegisterTable_.AddRegisterAlias("fxmm0l", "xmm0");
    x86RegisterTable_.AddRegisterAlias("fxmm1l", "xmm1");
    x86RegisterTable_.AddRegisterAlias("fxmm2l", "xmm2");
    x86RegisterTable_.AddRegisterAlias("fxmm3l", "xmm3");
    x86RegisterTable_.AddRegisterAlias("fxmm4l", "xmm4");
    x86RegisterTable_.AddRegisterAlias("fxmm5l", "xmm5");
    x86RegisterTable_.AddRegisterAlias("fxmm6l", "xmm6");
    x86RegisterTable_.AddRegisterAlias("fxmm7l", "xmm7");
    x86RegisterTable_.AddRegisterAlias("fxmm8l", "xmm8");
    x86RegisterTable_.AddRegisterAlias("fxmm9l", "xmm9");
    x86RegisterTable_.AddRegisterAlias("fxmm10l", "xmm10");
    x86RegisterTable_.AddRegisterAlias("fxmm11l", "xmm11");
    x86RegisterTable_.AddRegisterAlias("fxmm12l", "xmm12");
    x86RegisterTable_.AddRegisterAlias("fxmm13l", "xmm13");
    x86RegisterTable_.AddRegisterAlias("fxmm14l", "xmm14");
    x86RegisterTable_.AddRegisterAlias("fxmm15l", "xmm15");

    x86RegisterTable_.AddRegisterAlias("fxmm0s", "xmm0");
    x86RegisterTable_.AddRegisterAlias("fxmm1s", "xmm1");
    x86RegisterTable_.AddRegisterAlias("fxmm2s", "xmm2");
    x86RegisterTable_.AddRegisterAlias("fxmm3s", "xmm3");
    x86RegisterTable_.AddRegisterAlias("fxmm4s", "xmm4");
    x86RegisterTable_.AddRegisterAlias("fxmm5s", "xmm5");
    x86RegisterTable_.AddRegisterAlias("fxmm6s", "xmm6");
    x86RegisterTable_.AddRegisterAlias("fxmm7s", "xmm7");
    x86RegisterTable_.AddRegisterAlias("fxmm8s", "xmm8");
    x86RegisterTable_.AddRegisterAlias("fxmm9s", "xmm9");
    x86RegisterTable_.AddRegisterAlias("fxmm10s", "xmm10");
    x86RegisterTable_.AddRegisterAlias("fxmm11s", "xmm11");
    x86RegisterTable_.AddRegisterAlias("fxmm12s", "xmm12");
    x86RegisterTable_.AddRegisterAlias("fxmm13s", "xmm13");
    x86RegisterTable_.AddRegisterAlias("fxmm14s", "xmm14");
    x86RegisterTable_.AddRegisterAlias("fxmm15s", "xmm15");

    x86RegisterTable_.AddRegisterAlias("iymm0", "ymm0");
    x86RegisterTable_.AddRegisterAlias("iymm1", "ymm1");
    x86RegisterTable_.AddRegisterAlias("iymm2", "ymm2");
    x86RegisterTable_.AddRegisterAlias("iymm3", "ymm3");
    x86RegisterTable_.AddRegisterAlias("iymm4", "ymm4");
    x86RegisterTable_.AddRegisterAlias("iymm5", "ymm5");
    x86RegisterTable_.AddRegisterAlias("iymm6", "ymm6");
    x86RegisterTable_.AddRegisterAlias("iymm7", "ymm7");
    x86RegisterTable_.AddRegisterAlias("iymm8", "ymm8");
    x86RegisterTable_.AddRegisterAlias("iymm9", "ymm9");
    x86RegisterTable_.AddRegisterAlias("iymm10", "ymm10");
    x86RegisterTable_.AddRegisterAlias("iymm11", "ymm11");
    x86RegisterTable_.AddRegisterAlias("iymm12", "ymm12");
    x86RegisterTable_.AddRegisterAlias("iymm13", "ymm13");
    x86RegisterTable_.AddRegisterAlias("iymm14", "ymm14");
    x86RegisterTable_.AddRegisterAlias("iymm15", "ymm15");
  }

  public static RegisterTable SelectRegisterTable(IRMode irMode) {
    return irMode switch {
      IRMode.x86_64  => x86RegisterTable_,
      IRMode.ARM64   => arm64RegisterTable_,
      IRMode.Default => x86RegisterTable_,
      _              => throw new ArgumentException("invalid valid", nameof(irMode))
    };
  }
}