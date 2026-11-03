// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
#include <cstdint>
#include <cstdio>
#include <stdexcept>
#include <thread>
#include "FunctionCodeRequest.h"

static void Check(bool condition, const char* message) {
  if (!condition) throw std::runtime_error(message);
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
  request.FunctionId = 0xDEADBEEF;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Forged FunctionID accepted");
  Check(id == 0 && rejit == 0, "Rejected identity escaped through output");
  request.FunctionId = 0x1234;
  request.ReJITId = 8;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Wrong ReJITID accepted");
  request.ReJITId = 7;
  runtime.result = E_FAIL;
  Check(!ResolveFunctionCodeRequest(&runtime, request, id, rejit), "Failed lookup accepted");
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
  NamedPipeClient client;
  if (!client.Initialize(name)) {
    CloseHandle(server);
    throw std::runtime_error("Cannot connect test client");
  }
  if (!ConnectNamedPipe(server, nullptr) && GetLastError() != ERROR_PIPE_CONNECTED) {
    CloseHandle(server);
    throw std::runtime_error("Cannot accept test client");
  }
  bool sent = true;
  std::thread writer([&]() {
    PipeMessageHeader header{kind, size};
    RequestFunctionCodeMessage body{0x1234, 0x5678, 7, 42};
    const char* parts[] = {reinterpret_cast<const char*>(&header), reinterpret_cast<const char*>(&body)};
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
    if (truncated || truncatedHeader) {
      FlushFileBuffers(server);
      DisconnectNamedPipe(server);
    }
  });
  PipeMessageHeader header{};
  std::shared_ptr<char[]> body;
  bool canceled = false;
  bool accepted = false;
  client.ReceiveMessages(
      [&](PipeMessageHeader receivedHeader, std::shared_ptr<char[]> receivedBody) {
        accepted = true;
        header = receivedHeader;
        body = receivedBody;
        canceled = true;
      },
      canceled);
  writer.join();
  CloseHandle(server);
  Check(sent, "Test writer failed");
  Check(accepted == expected, "Wrong frame acceptance");
  if (!expected) {
    Check(!client.SendMessage(PipeMessageKind::EndSession),
          "Client remained connected after rejecting invalid frame");
  }
  if (accepted && kind == PipeMessageKind::RequestFunctionCode) {
    RequestFunctionCodeMessage request{};
    memcpy(&request, body.get(), sizeof(request));
    Check(request.FunctionId == 0x1234 && request.Address == 0x5678 &&
          request.ReJITId == 7 && request.ProcessId == 42, "Request body corrupted");
  }
}

int main() {
  try {
    static_assert(sizeof(PipeMessageHeader) == 8);
    static_assert(sizeof(RequestFunctionCodeMessage) == 24);
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
    puts("Native pipe framing and function identity checks passed.");
    return 0;
  } catch (const std::exception& ex) {
    fprintf(stderr, "%s\n", ex.what());
    return 1;
  }
}
