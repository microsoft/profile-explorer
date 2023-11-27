#pragma once

#include "Disasm.hpp"
#include "CsIns.hpp"

using CPPCInsClass = CCsIns<ppc_insn_group, ppc_reg, ppc_op_type, ppc_insn>;

class CPPCDisasm :
	public CCsDisasm<CPPCInsClass>
{
public:
	CPPCDisasm(
		__in unsigned int mode = cs_mode::CS_MODE_BIG_ENDIAN
		) : CCsDisasm(cs_arch::CS_ARCH_PPC, mode)
	{
	}
};
