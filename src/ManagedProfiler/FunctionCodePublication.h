// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
#pragma once
#include <atomic>
#include <mutex>
#include <unordered_set>
#include "NamedPipeClient.h"

enum class FunctionCodePublicationResult {
  Published,
  AlreadyPublished,
  InvalidInput,
  CopyFailed,
  TransportFailed,
};

template <typename SendCallTargets>
FunctionCodePublicationResult PublishFunctionCode(
    NamedPipeClient& client, std::atomic<bool>& sessionEnded,
    std::mutex& recordedAddressesLock,
    std::unordered_set<UINT_PTR>& recordedAddresses, int64_t functionId,
    UINT_PTR address, int32_t reJITId, int32_t processId, int32_t codeSize,
    void* codeBytes, SendCallTargets&& sendCallTargets) {
  if (sessionEnded.load()) {
    return FunctionCodePublicationResult::TransportFailed;
  }

  {
    std::lock_guard<std::mutex> lock(recordedAddressesLock);
    if (!recordedAddresses.insert(address).second) {
      return FunctionCodePublicationResult::AlreadyPublished;
    }
  }

  auto result = SendFunctionCode(client, functionId, static_cast<int64_t>(address),
                                 reJITId, processId, codeSize, codeBytes);
  switch (result) {
    case FunctionCodeSendResult::Sent:
      sendCallTargets();
      return sessionEnded.load() ? FunctionCodePublicationResult::TransportFailed
                                 : FunctionCodePublicationResult::Published;
    case FunctionCodeSendResult::InvalidInput:
    case FunctionCodeSendResult::CopyFailed: {
      std::lock_guard<std::mutex> lock(recordedAddressesLock);
      recordedAddresses.erase(address);
      return result == FunctionCodeSendResult::InvalidInput
                 ? FunctionCodePublicationResult::InvalidInput
                 : FunctionCodePublicationResult::CopyFailed;
    }
    case FunctionCodeSendResult::TransportFailed:
      sessionEnded.store(true);
      client.Disconnect();
      return FunctionCodePublicationResult::TransportFailed;
  }

  return FunctionCodePublicationResult::TransportFailed;
}
