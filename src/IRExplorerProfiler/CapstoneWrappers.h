// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
#pragma once
#include <capstone/capstone.h>
#include <memory>

// Minimal C++ wrappers over the Capstone C API functions.
class CapstoneHandle : public std::shared_ptr<csh> {
 public:
  CapstoneHandle() : std::shared_ptr<csh>(&handle_, cs_close) {}

 private:
  csh handle_;
};

class InstructionHolder {
 public:
  InstructionHolder(CapstoneHandle handle, cs_insn* instr)
      : handle_(handle), instr_(instr) {}

  cs_insn* operator->() { return instr_; }

 private:
  CapstoneHandle handle_;
  cs_insn* instr_;
};

class InstructionListHolder {
 public:
  size_t Size;
  const void* Address;
  size_t Count;

  InstructionListHolder(CapstoneHandle& handle,
                        const void* address,
                        size_t size,
                        size_t startAddress)
      : handle_(handle), Address(address), Size(size), instrs_(nullptr) {
    Count =
        cs_disasm(*handle_.get(), static_cast<const unsigned char*>(address),
                  size, startAddress, 0, &instrs_);
  }

  ~InstructionListHolder() {
    if (instrs_) {
      cs_free(instrs_, Count);
    }
  }

  InstructionHolder Instruction(size_t index) {
    return *new InstructionHolder(handle_, instrs_ + index);
  }

 private:
  cs_insn* instrs_;
  CapstoneHandle handle_;
};

class CapstoneDisasm {
 public:
  CapstoneDisasm(cs_arch arch, unsigned int mode) {
    cs_open(arch, (cs_mode)mode, handle_.get());
  }

  InstructionListHolder* Disassemble(const void* code,
                                     size_t size,
                                     size_t startAddress = 0) {
    return new InstructionListHolder(handle_, code, size, startAddress);
  }

  bool SetSyntax(cs_opt_value syntax) {
    return !cs_option(*handle_.get(), cs_opt_type::CS_OPT_SYNTAX, syntax);
  }

  bool SetDetail(cs_opt_value detailedInfo) {
    return !cs_option(*handle_.get(), cs_opt_type::CS_OPT_DETAIL, detailedInfo);
  }

 private:
  CapstoneHandle handle_;
};