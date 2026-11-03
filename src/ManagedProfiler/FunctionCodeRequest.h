// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
#pragma once
#include <cstdint>
#include <cor.h>
#include <corprof.h>
#include "NamedPipeClient.h"

template <typename ProfilerInfo>
bool ResolveFunctionCodeRequest(ProfilerInfo* profilerInfo,
                                const RequestFunctionCodeMessage& request,
                                FunctionID& functionId, ReJITID& rejitId) {
  functionId = 0;
  rejitId = 0;
  const auto address = static_cast<UINT_PTR>(request.Address);
  if (request.Address == 0 ||
      static_cast<uint64_t>(address) != static_cast<uint64_t>(request.Address)) {
    return false;
  }

  // Only the CLR code map may turn an untrusted instruction address into a FunctionID.
  FunctionID resolvedFunction = 0;
  ReJITID resolvedRejit = 0;
  if (FAILED(profilerInfo->GetFunctionFromIP3(
          reinterpret_cast<LPCBYTE>(address), &resolvedFunction, &resolvedRejit)) ||
      resolvedFunction == 0 ||
      static_cast<uint64_t>(resolvedFunction) != static_cast<uint64_t>(request.FunctionId) ||
      resolvedRejit != static_cast<uint32_t>(request.ReJITId)) {
    return false;
  }
  functionId = resolvedFunction;
  rejitId = resolvedRejit;
  return true;
}
