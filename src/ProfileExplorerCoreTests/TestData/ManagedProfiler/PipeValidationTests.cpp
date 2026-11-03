// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <future>
#include <memory>
#include <stdexcept>
#include <thread>
#include <type_traits>
#include "FunctionCodeRequest.h"

static void Check(bool condition, const char* message) {
  if (!condition) throw std::runtime_error(message);
}

static void ReadServerBytes(HANDLE server, char* buffer, DWORD size) {
  DWORD total = 0;
  while (total < size) {
    DWORD read = 0;
    Check(ReadFile(server, buffer + total, size - total, &read, nullptr) && read != 0,
          "Cannot read test pipe bytes");
    total += read;
  }
}

struct RuntimeCodeMap {
  HRESULT result = S_OK;
  FunctionID id = 0x1234;
  ReJITID rejit = 7;
  LPCBYTE observedAddress = nullptr;
  int calls = 0;
  HRESULT GetFunctionFromIP3(LPCBYTE address, FunctionID* function, ReJITID* version) {
    ++calls;
    observedAddress = address;
    *function = id;
    *version = rejit;
    return result;
  }
};

static void TestIdentityResolution() {
  RuntimeCodeMap runtime;
  RequestFunctionCodeMessage request{0x1234, 0x5678, 7, 42};
  FunctionID id = 0;
  ReJITID rejit = 0;
  Check(ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Valid request rejected");
  Check(id == 0x1234 && rejit == 7, "Must return runtime-resolved identity");
  Check(runtime.observedAddress == reinterpret_cast<LPCBYTE>(0x5678), "Wrong lookup address");
  Check(runtime.calls == 1, "Valid request did not perform exactly one CLR lookup");
  request.FunctionId = 0xDEADBEEF;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Forged FunctionID accepted");
  Check(id == 0 && rejit == 0, "Rejected identity escaped through output");
  request.FunctionId = 0x1234;
  request.ReJITId = 8;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Wrong ReJITID accepted");
  Check(id == 0 && rejit == 0, "Rejected ReJIT escaped through output");
  request.ReJITId = 7;
  runtime.result = E_FAIL;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Failed lookup accepted");
  Check(id == 0 && rejit == 0, "Failed lookup escaped through output");
  runtime.result = S_OK;
  runtime.id = 0;
  request.FunctionId = 0;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Null runtime ID accepted");
  request.Address = 0;
  int calls = runtime.calls;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Null address accepted");
  Check(runtime.calls == calls, "Null address reached CLR");
  runtime.id = 0x1234;
  runtime.rejit = 0;
  request = {0x1234, 0x5678, 0, 42};
  Check(ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Non-ReJIT rundown request rejected");
#ifdef _WIN64
  runtime.rejit = 0x100000007ULL;
  request.ReJITId = 7;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit),
        "Truncated runtime ReJITID accepted");
#endif
#ifndef _WIN64
  request.Address = 0x100005678LL;
  calls = runtime.calls;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Truncated address accepted");
  Check(runtime.calls == calls, "Truncated address reached CLR");
#endif
}

static void TestFrame(PipeMessageKind kind, int32_t size, bool expected,
                      bool truncated = false, bool truncatedHeader = false) {
  static unsigned sequence = 0;
  wchar_t name[128];
  swprintf_s(name, L"\\\\.\\pipe\\PEXValidation-%lu-%lu-%u",
             GetCurrentProcessId(), GetTickCount(), ++sequence);
  HANDLE server = CreateNamedPipeW(name, PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                                   PIPE_TYPE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr);
  Check(server != INVALID_HANDLE_VALUE, "Cannot create isolated test pipe");
  std::unique_ptr<void, decltype(&CloseHandle)> serverOwner(server, &CloseHandle);
  NamedPipeClient client;
  if (!client.Initialize(name)) {
    throw std::runtime_error("Cannot connect test client");
  }
  if (!ConnectNamedPipe(server, nullptr) && GetLastError() != ERROR_PIPE_CONNECTED) {
    throw std::runtime_error("Cannot accept test client");
  }
  Check(client.SendMessage(PipeMessageKind::EndSession),
        "Connected positive-control send failed");
  char control[8]{};
  ReadServerBytes(server, control, sizeof(control));
  const char expectedControl[8] = {1, 0, 0, 0, 8, 0, 0, 0};
  Check(memcmp(control, expectedControl, sizeof(control)) == 0,
        "Connected positive-control frame corrupted");
  bool expectNextFrame = expected && kind == PipeMessageKind::RequestFunctionCode;
  bool sent = true;
  std::jthread writer([&]() {
    unsigned char header[8]{};
    for (unsigned byte = 0; byte < 4; ++byte) {
      header[byte] = static_cast<unsigned char>(static_cast<uint32_t>(kind) >> (byte * 8));
      header[byte + 4] = static_cast<unsigned char>(static_cast<uint32_t>(size) >> (byte * 8));
    }
    const unsigned char body[24] = {
        0x34, 0x12, 0, 0, 0, 0, 0, 0,
        0x78, 0x56, 0, 0, 0, 0, 0, 0,
        7, 0, 0, 0, 42, 0, 0, 0};
    const unsigned char* parts[] = {header, body};
    size_t lengths[] = {truncatedHeader ? 3u : sizeof(header),
                        truncatedHeader ? 0u : truncated ? 2u : sizeof(body)};
    for (int part = 0; part < (kind == PipeMessageKind::RequestFunctionCode && size == 32 ? 2 : 1); ++part) {
      for (size_t byte = 0; byte < lengths[part]; ++byte) {
        DWORD written = 0;
        if (!WriteFile(server, parts[part] + byte, 1, &written, nullptr) || written != 1) {
          sent = false;
          break;
        }
      }
    }
    if (expectNextFrame) {
      const unsigned char endSession[8] = {1, 0, 0, 0, 8, 0, 0, 0};
      DWORD written = 0;
      sent = sent && WriteFile(server, endSession, sizeof(endSession), &written, nullptr) &&
             written == sizeof(endSession);
    }
    if (truncated || truncatedHeader) {
      FlushFileBuffers(server);
      DisconnectNamedPipe(server);
    }
  });
  PipeMessageHeader header{};
  std::shared_ptr<char[]> body;
  bool canceled = false;
  bool accepted = false;
  int dispatchCount = 0;
  bool nextFrameValid = false;
  client.ReceiveMessages(
      [&](PipeMessageHeader receivedHeader, std::shared_ptr<char[]> receivedBody) {
        accepted = true;
        if (++dispatchCount == 1) {
          header = receivedHeader;
          body = receivedBody;
        }
        else {
          nextFrameValid = receivedHeader.Kind == PipeMessageKind::EndSession &&
                           receivedHeader.Size == 8 && !receivedBody;
        }
        canceled = !expectNextFrame || dispatchCount == 2;
      },
      canceled);
  writer.join();
  Check(sent, "Test writer failed");
  Check(accepted == expected, "Wrong frame acceptance");
  if (expectNextFrame) {
    Check(dispatchCount == 2 && nextFrameValid,
          "Consecutive frames lost alignment or retained the previous body");
  }
  if (!expected && !truncated && !truncatedHeader) {
    Check(!client.SendMessage(PipeMessageKind::EndSession),
          "Client remained connected after rejecting invalid frame");
  }
  if (accepted && kind == PipeMessageKind::RequestFunctionCode) {
    Check(body != nullptr, "Accepted request has no body");
    RequestFunctionCodeMessage request{};
    memcpy(&request, body.get(), sizeof(request));
    Check(request.FunctionId == 0x1234 && request.Address == 0x5678 &&
          request.ReJITId == 7 && request.ProcessId == 42, "Request body corrupted");
  }
}

static void TestRejectedFrameCancelsBlockedWrite() {
  wchar_t name[128];
  swprintf_s(name, L"\\\\.\\pipe\\PEXValidation-Blocked-%lu-%lu",
             GetCurrentProcessId(), GetTickCount());
  HANDLE server = CreateNamedPipeW(name, PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                                   PIPE_TYPE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr);
  Check(server != INVALID_HANDLE_VALUE, "Cannot create blocked-write test pipe");
  std::unique_ptr<void, decltype(&CloseHandle)> serverOwner(server, &CloseHandle);
  NamedPipeClient client;
  Check(client.Initialize(name), "Cannot connect blocked-write client");
  Check(ConnectNamedPipe(server, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED,
        "Cannot accept blocked-write client");

  auto sending = std::async(std::launch::async, [&]() {
    char payload[64 * 1024]{};
    return client.SendMessage(PipeMessageKind::FunctionCode, payload, sizeof(payload));
  });
  struct DisconnectBeforeJoin {
    NamedPipeClient& client;
    ~DisconnectBeforeJoin() { client.Disconnect(); }
  } disconnectBeforeJoin{client};
  char prefix[9]{};
  ReadServerBytes(server, prefix, sizeof(prefix));
  Check(sending.wait_for(std::chrono::milliseconds(0)) == std::future_status::timeout,
        "Fixture did not leave a write blocked on the undrained pipe");

  const unsigned char invalidHeader[8] = {4, 0, 0, 0, 8, 0, 0, 0};
  DWORD written = 0;
  Check(WriteFile(server, invalidHeader, sizeof(invalidHeader), &written, nullptr) &&
        written == sizeof(invalidHeader), "Cannot send malformed frame during blocked write");
  bool canceled = false;
  bool dispatched = false;
  client.ReceiveMessages([&](PipeMessageHeader, std::shared_ptr<char[]>) {
    dispatched = true;
    canceled = true;
  }, canceled);
  Check(!dispatched, "Malformed frame dispatched during blocked write");
  Check(sending.wait_for(std::chrono::seconds(5)) == std::future_status::ready,
        "Rejected frame did not release blocked writer");
  Check(!sending.get(), "Canceled write reported success");
  Check(!client.SendMessage(PipeMessageKind::EndSession),
        "Send accepted after blocked-write teardown");
}

int main() {
  try {
    static_assert(sizeof(PipeMessageHeader) == 8);
    static_assert(sizeof(RequestFunctionCodeMessage) == 24);
    static_assert(std::is_trivially_copyable_v<PipeMessageHeader>);
    static_assert(std::is_trivially_copyable_v<RequestFunctionCodeMessage>);
    static_assert(sizeof(FunctionCodeMessage) == 28);
    static_assert(sizeof(FunctionCallTargetMessage) == 28);
    TestIdentityResolution();
    TestFrame(PipeMessageKind::RequestFunctionCode, 32, true);
    TestFrame(PipeMessageKind::EndSession, 8, true);
    for (int size : {-1, 0, 7, 8, 31, 33, INT32_MAX}) {
      TestFrame(PipeMessageKind::RequestFunctionCode, size, false);
    }
    TestFrame(PipeMessageKind::EndSession, 9, false);
    TestFrame(PipeMessageKind::StartSession, 8, false);
    TestFrame(PipeMessageKind::FunctionCode, 8, false);
    TestFrame(PipeMessageKind::FunctionCallTarget, 8, false);
    TestFrame(static_cast<PipeMessageKind>(99), 8, false);
    TestFrame(PipeMessageKind::RequestFunctionCode, 32, false, true);
    TestFrame(PipeMessageKind::RequestFunctionCode, 32, false, false, true);
    TestRejectedFrameCancelsBlockedWrite();
    puts("Native pipe framing and function identity checks passed.");
    return 0;
  } catch (const std::exception& ex) {
    fprintf(stderr, "%s\n", ex.what());
    return 1;
  }
}
