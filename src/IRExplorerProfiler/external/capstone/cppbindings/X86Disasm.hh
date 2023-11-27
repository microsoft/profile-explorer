#pragma once

#include "Disasm.hpp"
#include "CsIns.hpp"

using CX86InsClass = CCsIns<x86_insn_group, x86_reg, x86_op_type, x86_insn>;

class CX86Disasm64 :
	public CCsDisasm<CX86InsClass>
{
public:
	CX86Disasm64(
		__in unsigned int mode = cs_mode::CS_MODE_64 + cs_mode::CS_MODE_LITTLE_ENDIAN
		) : CCsDisasm(cs_arch::CS_ARCH_X86, mode)
	{
	}
};

class CX86Disasm86 :
	public CCsDisasm<CX86InsClass>
{
public:
	CX86Disasm86(
		__in unsigned int mode = cs_mode::CS_MODE_32 + cs_mode::CS_MODE_LITTLE_ENDIAN
		) : CCsDisasm(cs_arch::CS_ARCH_X86, mode)
	{
	}
};

class CX86Disasm16 :
	public CCsDisasm<CX86InsClass>
{
public:
	CX86Disasm16(
		__in unsigned int mode = cs_mode::CS_MODE_16 + cs_mode::CS_MODE_LITTLE_ENDIAN
		) : CCsDisasm(cs_arch::CS_ARCH_X86, mode)
	{
	}
};
