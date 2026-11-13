// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
#include <chrono>
#include <barrier>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <future>
#include <memory>
#include <stdexcept>
#include <string>
#include <thread>
#include <type_traits>
#include <unordered_set>
#include <vector>
#include "FunctionCodePublication.h"
#include "FunctionCodeRequest.h"

#ifdef PEX_BUILD_TEST_PROFILER_DLL

class __declspec(uuid("805A308B-061C-47F3-9B30-F785C3186E81"))
    AttachedValidationProfiler final : public ICorProfilerCallback10 {
  ICorProfilerInfo12* profilerInfo_ = nullptr;
  std::atomic<ULONG> refCount_{1};
  int32_t processId_ = 0;
  NamedPipeClient pipeClient_;
  std::thread pipeThread_;
  std::atomic<bool> sessionEnded_{false};
  std::unordered_set<UINT_PTR> recordedAddrs_;
  std::mutex lock_;

  bool SendRequestedFunctionCode(const RequestFunctionCodeMessage& request) {
    if (sessionEnded_.load()) {
      return true;
    }

    FunctionID functionId = 0;
    ReJITID requestedRejit = 0;
    if (!ResolveFunctionCodeRequest(profilerInfo_, request, functionId, requestedRejit)) {
      return false;
    }

    const ReJITID rejit = requestedRejit;
    ULONG32 addrs = 0;
    if (FAILED(profilerInfo_->GetNativeCodeStartAddresses(functionId, rejit, 0,
                                                          &addrs, nullptr))) {
      return false;
    }
    std::vector<UINT_PTR> addr(addrs);
    if (FAILED(profilerInfo_->GetNativeCodeStartAddresses(functionId, rejit, addrs,
                                                          &addrs, addr.data())) ||
        addrs > addr.size()) {
      return false;
    }

    for (ULONG32 index = 0; index < addrs; index++) {
      ULONG32 cCodeInfos2 = 0;
      if (FAILED(profilerInfo_->GetCodeInfo4(addr[index], 0, &cCodeInfos2, nullptr))) {
        return false;
      }
      std::vector<COR_PRF_CODE_INFO> codeInfos2(cCodeInfos2);
      if (FAILED(profilerInfo_->GetCodeInfo4(addr[index], cCodeInfos2, &cCodeInfos2,
                                             codeInfos2.data())) ||
          cCodeInfos2 > codeInfos2.size()) {
        return false;
      }
      codeInfos2.resize(cCodeInfos2);

      for (auto&& codeInfo : codeInfos2) {
        auto result = PublishFunctionCode(
            pipeClient_, sessionEnded_, lock_, recordedAddrs_, functionId,
            codeInfo.startAddress, static_cast<int32_t>(rejit), processId_,
            static_cast<int32_t>(codeInfo.size),
            reinterpret_cast<void*>(codeInfo.startAddress), [] {});
        if (result == FunctionCodePublicationResult::TransportFailed) {
          return false;
        }
      }
    }

    return true;
  }

 public:
  ~AttachedValidationProfiler() {
    Shutdown();
  }

  HRESULT __stdcall QueryInterface(REFIID riid, void** ppvObject) override {
    if (ppvObject == nullptr) {
      return E_POINTER;
    }
    if (riid == __uuidof(IUnknown) || riid == __uuidof(ICorProfilerCallback) ||
        riid == __uuidof(ICorProfilerCallback2) ||
        riid == __uuidof(ICorProfilerCallback3) ||
        riid == __uuidof(ICorProfilerCallback4) ||
        riid == __uuidof(ICorProfilerCallback5) ||
        riid == __uuidof(ICorProfilerCallback6) ||
        riid == __uuidof(ICorProfilerCallback7) ||
        riid == __uuidof(ICorProfilerCallback8) ||
        riid == __uuidof(ICorProfilerCallback9) ||
        riid == __uuidof(ICorProfilerCallback10)) {
      AddRef();
      *ppvObject = static_cast<ICorProfilerCallback10*>(this);
      return S_OK;
    }
    *ppvObject = nullptr;
    return E_NOINTERFACE;
  }

  ULONG __stdcall AddRef() override { return ++refCount_; }

  ULONG __stdcall Release() override {
    ULONG count = --refCount_;
    if (count == 0) {
      delete this;
    }
    return count;
  }

  HRESULT __stdcall Initialize(IUnknown* pICorProfilerInfoUnk) override {
    return InitializeForAttach(pICorProfilerInfoUnk, nullptr, 0);
  }

  HRESULT __stdcall InitializeForAttach(IUnknown* pCorProfilerInfoUnk,
                                        void* pvClientData,
                                        UINT cbClientData) override {
    if (pCorProfilerInfoUnk == nullptr ||
        FAILED(pCorProfilerInfoUnk->QueryInterface(
            __uuidof(ICorProfilerInfo12),
            reinterpret_cast<void**>(&profilerInfo_))) ||
        profilerInfo_ == nullptr) {
      return E_FAIL;
    }

    profilerInfo_->SetEventMask2(COR_PRF_MONITOR_JIT_COMPILATION,
                                 COR_PRF_HIGH_MONITOR_EVENT_PIPE);

    std::wstring pipeName = L"\\\\.\\pipe\\PEXProfilerPipe";
    if (pvClientData != nullptr && cbClientData >= sizeof(wchar_t)) {
      const auto* chars = static_cast<const wchar_t*>(pvClientData);
      size_t maxChars = cbClientData / sizeof(wchar_t);
      size_t length = wcsnlen(chars, maxChars);
      if (length > 0) {
        pipeName.assign(chars, length);
      }
    }

    sessionEnded_.store(false);
    recordedAddrs_.clear();
    processId_ = static_cast<int32_t>(GetCurrentProcessId());
    if (!pipeClient_.Initialize(pipeName.c_str())) {
      return E_FAIL;
    }

    pipeThread_ = std::thread([this]() {
      bool canceled = false;
      pipeClient_.ReceiveMessages(
          [&](PipeMessageHeader header, std::shared_ptr<char[]> messageBody) {
            switch (header.Kind) {
              case PipeMessageKind::RequestFunctionCode: {
                RequestFunctionCodeMessage request{};
                if (ValidateFunctionCodeRequestFrame(
                        header, messageBody.get(), processId_, request) ==
                    FunctionCodeRequestFrameStatus::Valid) {
                  SendRequestedFunctionCode(request);
                }
                break;
              }
              case PipeMessageKind::EndSession: {
                canceled = true;
                sessionEnded_.store(true);
                pipeClient_.Disconnect();
                return;
              }
            }
          },
          canceled);
      sessionEnded_.store(true);
      pipeClient_.Disconnect();
    });

    return S_OK;
  }

  HRESULT __stdcall Shutdown() override {
    sessionEnded_.store(true);
    pipeClient_.Disconnect();
    if (pipeThread_.joinable() &&
        pipeThread_.get_id() != std::this_thread::get_id()) {
      pipeThread_.join();
    }
    if (profilerInfo_ != nullptr) {
      profilerInfo_->Release();
      profilerInfo_ = nullptr;
    }
    return S_OK;
  }

  HRESULT __stdcall AppDomainCreationStarted(AppDomainID) override { return S_OK; }
  HRESULT __stdcall AppDomainCreationFinished(AppDomainID, HRESULT) override { return S_OK; }
  HRESULT __stdcall AppDomainShutdownStarted(AppDomainID) override { return S_OK; }
  HRESULT __stdcall AppDomainShutdownFinished(AppDomainID, HRESULT) override { return S_OK; }
  HRESULT __stdcall AssemblyLoadStarted(AssemblyID) override { return S_OK; }
  HRESULT __stdcall AssemblyLoadFinished(AssemblyID, HRESULT) override { return S_OK; }
  HRESULT __stdcall AssemblyUnloadStarted(AssemblyID) override { return S_OK; }
  HRESULT __stdcall AssemblyUnloadFinished(AssemblyID, HRESULT) override { return S_OK; }
  HRESULT __stdcall ModuleLoadStarted(ModuleID) override { return S_OK; }
  HRESULT __stdcall ModuleLoadFinished(ModuleID, HRESULT) override { return S_OK; }
  HRESULT __stdcall ModuleUnloadStarted(ModuleID) override { return S_OK; }
  HRESULT __stdcall ModuleUnloadFinished(ModuleID, HRESULT) override { return S_OK; }
  HRESULT __stdcall ModuleAttachedToAssembly(ModuleID, AssemblyID) override { return S_OK; }
  HRESULT __stdcall ClassLoadStarted(ClassID) override { return S_OK; }
  HRESULT __stdcall ClassLoadFinished(ClassID, HRESULT) override { return S_OK; }
  HRESULT __stdcall ClassUnloadStarted(ClassID) override { return S_OK; }
  HRESULT __stdcall ClassUnloadFinished(ClassID, HRESULT) override { return S_OK; }
  HRESULT __stdcall FunctionUnloadStarted(FunctionID) override { return S_OK; }
  HRESULT __stdcall JITCompilationStarted(FunctionID, BOOL) override { return S_OK; }
  HRESULT __stdcall JITCompilationFinished(FunctionID, HRESULT, BOOL) override { return S_OK; }
  HRESULT __stdcall JITCachedFunctionSearchStarted(FunctionID, BOOL*) override { return S_OK; }
  HRESULT __stdcall JITCachedFunctionSearchFinished(FunctionID, COR_PRF_JIT_CACHE) override { return S_OK; }
  HRESULT __stdcall JITFunctionPitched(FunctionID) override { return S_OK; }
  HRESULT __stdcall JITInlining(FunctionID, FunctionID, BOOL*) override { return S_OK; }
  HRESULT __stdcall ThreadCreated(ThreadID) override { return S_OK; }
  HRESULT __stdcall ThreadDestroyed(ThreadID) override { return S_OK; }
  HRESULT __stdcall ThreadAssignedToOSThread(ThreadID, DWORD) override { return S_OK; }
  HRESULT __stdcall RemotingClientInvocationStarted() override { return S_OK; }
  HRESULT __stdcall RemotingClientSendingMessage(GUID*, BOOL) override { return S_OK; }
  HRESULT __stdcall RemotingClientReceivingReply(GUID*, BOOL) override { return S_OK; }
  HRESULT __stdcall RemotingClientInvocationFinished() override { return S_OK; }
  HRESULT __stdcall RemotingServerReceivingMessage(GUID*, BOOL) override { return S_OK; }
  HRESULT __stdcall RemotingServerInvocationStarted() override { return S_OK; }
  HRESULT __stdcall RemotingServerInvocationReturned() override { return S_OK; }
  HRESULT __stdcall RemotingServerSendingReply(GUID*, BOOL) override { return S_OK; }
  HRESULT __stdcall UnmanagedToManagedTransition(FunctionID, COR_PRF_TRANSITION_REASON) override { return S_OK; }
  HRESULT __stdcall ManagedToUnmanagedTransition(FunctionID, COR_PRF_TRANSITION_REASON) override { return S_OK; }
  HRESULT __stdcall RuntimeSuspendStarted(COR_PRF_SUSPEND_REASON) override { return S_OK; }
  HRESULT __stdcall RuntimeSuspendFinished() override { return S_OK; }
  HRESULT __stdcall RuntimeSuspendAborted() override { return S_OK; }
  HRESULT __stdcall RuntimeResumeStarted() override { return S_OK; }
  HRESULT __stdcall RuntimeResumeFinished() override { return S_OK; }
  HRESULT __stdcall RuntimeThreadSuspended(ThreadID) override { return S_OK; }
  HRESULT __stdcall RuntimeThreadResumed(ThreadID) override { return S_OK; }
  HRESULT __stdcall MovedReferences(ULONG, ObjectID[], ObjectID[], ULONG[]) override { return S_OK; }
  HRESULT __stdcall ObjectAllocated(ObjectID, ClassID) override { return S_OK; }
  HRESULT __stdcall ObjectsAllocatedByClass(ULONG, ClassID[], ULONG[]) override { return S_OK; }
  HRESULT __stdcall ObjectReferences(ObjectID, ClassID, ULONG, ObjectID[]) override { return S_OK; }
  HRESULT __stdcall RootReferences(ULONG, ObjectID[]) override { return S_OK; }
  HRESULT __stdcall ExceptionThrown(ObjectID) override { return S_OK; }
  HRESULT __stdcall ExceptionSearchFunctionEnter(FunctionID) override { return S_OK; }
  HRESULT __stdcall ExceptionSearchFunctionLeave() override { return S_OK; }
  HRESULT __stdcall ExceptionSearchFilterEnter(FunctionID) override { return S_OK; }
  HRESULT __stdcall ExceptionSearchFilterLeave() override { return S_OK; }
  HRESULT __stdcall ExceptionSearchCatcherFound(FunctionID) override { return S_OK; }
  HRESULT __stdcall ExceptionOSHandlerEnter(UINT_PTR) override { return S_OK; }
  HRESULT __stdcall ExceptionOSHandlerLeave(UINT_PTR) override { return S_OK; }
  HRESULT __stdcall ExceptionUnwindFunctionEnter(FunctionID) override { return S_OK; }
  HRESULT __stdcall ExceptionUnwindFunctionLeave() override { return S_OK; }
  HRESULT __stdcall ExceptionUnwindFinallyEnter(FunctionID) override { return S_OK; }
  HRESULT __stdcall ExceptionUnwindFinallyLeave() override { return S_OK; }
  HRESULT __stdcall ExceptionCatcherEnter(FunctionID, ObjectID) override { return S_OK; }
  HRESULT __stdcall ExceptionCatcherLeave() override { return S_OK; }
  HRESULT __stdcall COMClassicVTableCreated(ClassID, REFGUID, void*, ULONG) override { return S_OK; }
  HRESULT __stdcall COMClassicVTableDestroyed(ClassID, REFGUID, void*) override { return S_OK; }
  HRESULT __stdcall ExceptionCLRCatcherFound() override { return S_OK; }
  HRESULT __stdcall ExceptionCLRCatcherExecute() override { return S_OK; }
  HRESULT __stdcall ThreadNameChanged(ThreadID, ULONG, WCHAR[]) override { return S_OK; }
  HRESULT __stdcall GarbageCollectionStarted(int, BOOL[], COR_PRF_GC_REASON) override { return S_OK; }
  HRESULT __stdcall SurvivingReferences(ULONG, ObjectID[], ULONG[]) override { return S_OK; }
  HRESULT __stdcall GarbageCollectionFinished() override { return S_OK; }
  HRESULT __stdcall FinalizeableObjectQueued(DWORD, ObjectID) override { return S_OK; }
  HRESULT __stdcall RootReferences2(ULONG, ObjectID[], COR_PRF_GC_ROOT_KIND[], COR_PRF_GC_ROOT_FLAGS[], UINT_PTR[]) override { return S_OK; }
  HRESULT __stdcall HandleCreated(GCHandleID, ObjectID) override { return S_OK; }
  HRESULT __stdcall HandleDestroyed(GCHandleID) override { return S_OK; }
  HRESULT __stdcall ProfilerAttachComplete() override { return S_OK; }
  HRESULT __stdcall ProfilerDetachSucceeded() override { return S_OK; }
  HRESULT __stdcall ReJITCompilationStarted(FunctionID, ReJITID, BOOL) override { return S_OK; }
  HRESULT __stdcall GetReJITParameters(ModuleID, mdMethodDef, ICorProfilerFunctionControl*) override { return S_OK; }
  HRESULT __stdcall ReJITCompilationFinished(FunctionID, ReJITID, HRESULT, BOOL) override { return S_OK; }
  HRESULT __stdcall ReJITError(ModuleID, mdMethodDef, FunctionID, HRESULT) override { return S_OK; }
  HRESULT __stdcall MovedReferences2(ULONG, ObjectID[], ObjectID[], SIZE_T[]) override { return S_OK; }
  HRESULT __stdcall SurvivingReferences2(ULONG, ObjectID[], SIZE_T[]) override { return S_OK; }
  HRESULT __stdcall ConditionalWeakTableElementReferences(ULONG, ObjectID[], ObjectID[], GCHandleID[]) override { return S_OK; }
  HRESULT __stdcall GetAssemblyReferences(const WCHAR*, ICorProfilerAssemblyReferenceProvider*) override { return S_OK; }
  HRESULT __stdcall ModuleInMemorySymbolsUpdated(ModuleID) override { return S_OK; }
  HRESULT __stdcall DynamicMethodJITCompilationStarted(FunctionID, BOOL, LPCBYTE, ULONG) override { return S_OK; }
  HRESULT __stdcall DynamicMethodJITCompilationFinished(FunctionID, HRESULT, BOOL) override { return S_OK; }
  HRESULT __stdcall DynamicMethodUnloaded(FunctionID) override { return S_OK; }
  HRESULT __stdcall EventPipeEventDelivered(EVENTPIPE_PROVIDER, DWORD, DWORD, ULONG, LPCBYTE, ULONG, LPCBYTE, LPCGUID, LPCGUID, ThreadID, ULONG, UINT_PTR[]) override { return S_OK; }
  HRESULT __stdcall EventPipeProviderCreated(EVENTPIPE_PROVIDER) override { return S_OK; }
};

class AttachedValidationProfilerFactory final : public IClassFactory {
 public:
  HRESULT __stdcall QueryInterface(REFIID riid, void** ppvObject) override {
    if (ppvObject == nullptr) {
      return E_POINTER;
    }
    if (riid == __uuidof(IUnknown) || riid == __uuidof(IClassFactory)) {
      *ppvObject = static_cast<IClassFactory*>(this);
      return S_OK;
    }
    *ppvObject = nullptr;
    return E_NOINTERFACE;
  }

  ULONG __stdcall AddRef() override { return 2; }
  ULONG __stdcall Release() override { return 1; }

  HRESULT __stdcall CreateInstance(IUnknown* pUnkOuter,
                                   REFIID riid,
                                   void** ppvObject) override {
    if (pUnkOuter != nullptr) {
      return CLASS_E_NOAGGREGATION;
    }
    auto* profiler = new (std::nothrow) AttachedValidationProfiler();
    if (profiler == nullptr) {
      return E_OUTOFMEMORY;
    }
    HRESULT hr = profiler->QueryInterface(riid, ppvObject);
    profiler->Release();
    return hr;
  }

  HRESULT __stdcall LockServer(BOOL) override { return S_OK; }
};

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID rclsid,
                                               REFIID riid,
                                               void** ppv) {
  if (rclsid == __uuidof(AttachedValidationProfiler)) {
    static AttachedValidationProfilerFactory factory;
    return factory.QueryInterface(riid, ppv);
  }
  return CLASS_E_CLASSNOTAVAILABLE;
}

#else

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

static void TestRequestFrameValidation() {
  PipeMessageHeader header{
      PipeMessageKind::RequestFunctionCode,
      static_cast<int32_t>(sizeof(PipeMessageHeader) +
                           sizeof(RequestFunctionCodeMessage))};
  const RequestFunctionCodeMessage body{0x1234, 0x5678, 7, 42};
  const auto* bodyBytes = reinterpret_cast<const char*>(&body);
  RequestFunctionCodeMessage request{};

  Check(ValidateFunctionCodeRequestFrame(header, bodyBytes, 42, request) ==
            FunctionCodeRequestFrameStatus::Valid,
        "Valid request frame rejected");
  Check(request.FunctionId == body.FunctionId && request.Address == body.Address &&
            request.ReJITId == body.ReJITId && request.ProcessId == body.ProcessId,
        "Request frame contents changed during validation");
  Check(ValidateFunctionCodeRequestFrame(header, nullptr, 42, request) ==
            FunctionCodeRequestFrameStatus::InvalidBody,
        "Request frame without a body accepted");
  --header.Size;
  Check(ValidateFunctionCodeRequestFrame(header, bodyBytes, 42, request) ==
            FunctionCodeRequestFrameStatus::InvalidBody,
        "Wrong-sized request frame accepted");
  ++header.Size;
  Check(ValidateFunctionCodeRequestFrame(header, bodyBytes, 43, request) ==
            FunctionCodeRequestFrameStatus::WrongProcess,
        "Request frame for another process accepted");
}

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

static void TestDisconnectWaitsForPendingRead() {
  wchar_t name[128];
  swprintf_s(name, L"\\\\.\\pipe\\PEXValidation-BlockedRead-%lu-%lu",
             GetCurrentProcessId(), GetTickCount());
  HANDLE server = CreateNamedPipeW(name, PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                                   PIPE_TYPE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr);
  Check(server != INVALID_HANDLE_VALUE, "Cannot create blocked-read test pipe");
  std::unique_ptr<void, decltype(&CloseHandle)> serverOwner(server, &CloseHandle);
  NamedPipeClient client;
  Check(client.Initialize(name), "Cannot connect blocked-read client");
  Check(ConnectNamedPipe(server, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED,
        "Cannot accept blocked-read client");

  struct ReadTestContext {
    HANDLE pending;
    HANDLE allowCompletion;
    HANDLE completed;
    HANDLE afterCancel;
    HANDLE beforeClose;
    std::atomic<int> sequence{0};
    std::atomic<int> completionOrder{0};
    std::atomic<int> closeOrder{0};
    std::atomic<bool> closedBeforeCompletion{false};
  } context{
      CreateEvent(nullptr, TRUE, FALSE, nullptr),
      CreateEvent(nullptr, TRUE, FALSE, nullptr),
      CreateEvent(nullptr, TRUE, FALSE, nullptr),
      CreateEvent(nullptr, TRUE, FALSE, nullptr),
      CreateEvent(nullptr, TRUE, FALSE, nullptr)};
  struct CloseTestEvents {
    ReadTestContext& context;
    ~CloseTestEvents() {
      CloseHandle(context.pending);
      CloseHandle(context.allowCompletion);
      CloseHandle(context.completed);
      CloseHandle(context.afterCancel);
      CloseHandle(context.beforeClose);
    }
  } closeTestEvents{context};
  Check(context.pending != nullptr && context.allowCompletion != nullptr &&
        context.completed != nullptr && context.afterCancel != nullptr &&
        context.beforeClose != nullptr, "Cannot create pending-read test events");
  struct ClearTestHooks {
    NamedPipeClient& client;
    ~ClearTestHooks() { client.SetTestHooks({}); }
  } clearTestHooks{client};
  client.SetTestHooks({
      &context,
      [](void* value) {
        auto& state = *static_cast<ReadTestContext*>(value);
        SetEvent(state.pending);
        WaitForSingleObject(state.allowCompletion, 30000);
      },
      [](void* value) {
        auto& state = *static_cast<ReadTestContext*>(value);
        state.completionOrder.store(++state.sequence);
        SetEvent(state.completed);
      },
      [](void* value) {
        auto& state = *static_cast<ReadTestContext*>(value);
        SetEvent(state.afterCancel);
      },
      [](void* value) {
        auto& state = *static_cast<ReadTestContext*>(value);
        state.closeOrder.store(++state.sequence);
        if (state.completionOrder.load() == 0) {
          state.closedBeforeCompletion.store(true);
        }
        SetEvent(state.beforeClose);
      }});

  std::atomic<bool> dispatched{false};
  auto receiving = std::async(std::launch::async, [&]() {
    bool canceled = false;
    client.ReceiveMessages(
        [&](PipeMessageHeader, std::shared_ptr<char[]>) { dispatched.store(true); },
        canceled);
  });
  struct ReleaseReadBeforeJoin {
    HANDLE allowCompletion;
    NamedPipeClient& client;
    ~ReleaseReadBeforeJoin() {
      SetEvent(allowCompletion);
      client.Disconnect();
    }
  } releaseReadBeforeJoin{context.allowCompletion, client};

  bool pendingReadObserved =
      WaitForSingleObject(context.pending, 5000) == WAIT_OBJECT_0;
  auto disconnecting = std::async(std::launch::async, [&]() {
    client.Disconnect();
  });
  struct JoinWorkersBeforeCleanup {
    HANDLE allowCompletion;
    NamedPipeClient& client;
    std::future<void>& receiving;
    std::future<void>& disconnecting;
    ~JoinWorkersBeforeCleanup() {
      SetEvent(allowCompletion);
      client.Disconnect();
      if (disconnecting.valid()) {
        disconnecting.wait();
      }
      if (receiving.valid()) {
        receiving.wait();
      }
    }
  } joinWorkersBeforeCleanup{context.allowCompletion, client, receiving,
                             disconnecting};
  bool cancellationObserved =
      WaitForSingleObject(context.afterCancel, 5000) == WAIT_OBJECT_0;
  bool closedWhilePending =
      WaitForSingleObject(context.beforeClose, 250) == WAIT_OBJECT_0;
  SetEvent(context.allowCompletion);
  bool readCompletionObserved =
      WaitForSingleObject(context.completed, 5000) == WAIT_OBJECT_0;
  bool closeObserved =
      WaitForSingleObject(context.beforeClose, 5000) == WAIT_OBJECT_0;
  bool disconnectFinished =
      disconnecting.wait_for(std::chrono::seconds(5)) == std::future_status::ready;
  if (disconnectFinished) {
    disconnecting.get();
  }
  bool receiverFinished =
      receiving.wait_for(std::chrono::seconds(5)) == std::future_status::ready;
  if (receiverFinished) {
    receiving.get();
  }
  Check(pendingReadObserved,
        "ReadFile did not enter the pending-I/O path");
  Check(cancellationObserved,
        "Disconnect did not request cancellation of the pending read");
  Check(!closedWhilePending,
        "Disconnect closed pipe resources before the pending read completed");
  Check(readCompletionObserved,
        "Canceled read did not report overlapped completion");
  Check(closeObserved,
        "Disconnect did not close pipe resources after read completion");
  Check(disconnectFinished,
        "External disconnect did not finish after read completion");
  Check(receiverFinished, "Reader did not finish after disconnect");
  Check(!context.closedBeforeCompletion.load() &&
        context.completionOrder.load() < context.closeOrder.load(),
        "Pipe resources were closed before overlapped read completion");
  Check(!dispatched.load(), "Incomplete message was dispatched during disconnect");
}

static void TestUnmappedCodeBytesRejected() {
  wchar_t name[128];
  swprintf_s(name, L"\\\\.\\pipe\\PEXValidation-Unmapped-%lu-%lu",
             GetCurrentProcessId(), GetTickCount());
  HANDLE server = CreateNamedPipeW(name, PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                                   PIPE_TYPE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr);
  Check(server != INVALID_HANDLE_VALUE, "Cannot create unmapped-code test pipe");
  std::unique_ptr<void, decltype(&CloseHandle)> serverOwner(server, &CloseHandle);
  NamedPipeClient client;
  Check(client.Initialize(name), "Cannot connect unmapped-code client");
  Check(ConnectNamedPipe(server, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED,
        "Cannot accept unmapped-code client");

  char byte = 0;
  Check(SendFunctionCode(client, 0x1234, 0x5678, 0, 42, 0, &byte) ==
            FunctionCodeSendResult::InvalidInput,
        "Zero-sized code region accepted");
  Check(SendFunctionCode(client, 0x1234, 0x5678, 0, 42, -1, &byte) ==
            FunctionCodeSendResult::InvalidInput,
        "Negative-sized code region accepted");
  Check(SendFunctionCode(client, 0x1234, 0x5678, 0, 42, 1, nullptr) ==
            FunctionCodeSendResult::InvalidInput,
        "Null code region accepted");

  void* page = VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  Check(page != nullptr, "Cannot allocate test page");
  Check(VirtualFree(page, 0, MEM_RELEASE) != 0, "Cannot release test page");
  Check(SendFunctionCode(client, 0x1234, reinterpret_cast<uintptr_t>(page), 0, 42,
                         64, page) == FunctionCodeSendResult::CopyFailed,
        "Unmapped code region was not rejected");
  Check(client.SendMessage(PipeMessageKind::EndSession),
        "Pipe corrupted after rejecting unmapped code region");
  char control[8]{};
  ReadServerBytes(server, control, sizeof(control));
  const char expectedControl[8] = {1, 0, 0, 0, 8, 0, 0, 0};
  Check(memcmp(control, expectedControl, sizeof(control)) == 0,
        "Follow-up control frame corrupted after unmapped code rejection");
}

static void TestFunctionCodePublicationRetriesAndSuppressesDuplicates() {
  wchar_t name[128];
  swprintf_s(name, L"\\\\.\\pipe\\PEXValidation-Publication-%lu-%lu",
             GetCurrentProcessId(), GetTickCount());
  HANDLE server = CreateNamedPipeW(name, PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                                   PIPE_TYPE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr);
  Check(server != INVALID_HANDLE_VALUE, "Cannot create publication test pipe");
  std::unique_ptr<void, decltype(&CloseHandle)> serverOwner(server, &CloseHandle);
  NamedPipeClient client;
  Check(client.Initialize(name), "Cannot connect publication client");
  Check(ConnectNamedPipe(server, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED,
        "Cannot accept publication client");

  std::mutex addressesLock;
  std::unordered_set<UINT_PTR> recordedAddresses;
  std::atomic<bool> sessionEnded{false};
  std::atomic<int> targetCount{0};
  constexpr int codeSize = 64;
  void* page = VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_NOACCESS);
  Check(page != nullptr, "Cannot allocate publication test page");
  struct FreePage {
    void operator()(void* address) const {
      if (address != nullptr) {
        VirtualFree(address, 0, MEM_RELEASE);
      }
    }
  };
  std::unique_ptr<void, FreePage> pageOwner(page);
  UINT_PTR address = reinterpret_cast<UINT_PTR>(page);

  auto copyFailure = PublishFunctionCode(
      client, sessionEnded, addressesLock, recordedAddresses, 0x1234, address,
      7, 42, codeSize, page, [&]() { ++targetCount; });
  Check(copyFailure == FunctionCodePublicationResult::CopyFailed,
        "Inaccessible code bytes were not reported as a pre-send failure");
  Check(recordedAddresses.find(address) == recordedAddresses.end(),
        "Failed code preparation retained its address reservation");
  Check(targetCount.load() == 0,
        "Call targets were sent after code preparation failed");

  DWORD oldProtection = 0;
  Check(VirtualProtect(page, 4096, PAGE_READWRITE, &oldProtection) != 0,
        "Cannot make publication test code readable");
  memset(page, 0x5A, codeSize);
  auto retry = PublishFunctionCode(
      client, sessionEnded, addressesLock, recordedAddresses, 0x1234, address,
      7, 42, codeSize, page, [&]() { ++targetCount; });
  Check(retry == FunctionCodePublicationResult::Published,
        "Valid retry after a pre-send failure was not published");
  Check(targetCount.load() == 1, "Call targets were not sent after code publication");

  PipeMessageHeader header{};
  ReadServerBytes(server, reinterpret_cast<char*>(&header), sizeof(header));
  Check(header.Kind == PipeMessageKind::FunctionCode &&
        header.Size == static_cast<int32_t>(sizeof(PipeMessageHeader) +
                                            sizeof(FunctionCodeMessage) +
                                            codeSize),
        "Retry emitted an invalid code frame");
  std::vector<char> body(header.Size - sizeof(PipeMessageHeader));
  ReadServerBytes(server, body.data(), static_cast<DWORD>(body.size()));
  auto* message = reinterpret_cast<const FunctionCodeMessage*>(body.data());
  Check(message->Address == static_cast<int64_t>(address) &&
        message->CodeSize == codeSize &&
        memcmp(message->CodeBytes, page, codeSize) == 0,
        "Retry emitted incorrect code metadata or bytes");

  auto duplicate = PublishFunctionCode(
      client, sessionEnded, addressesLock, recordedAddresses, 0x1234, address,
      7, 42, codeSize, page, [&]() { ++targetCount; });
  Check(duplicate == FunctionCodePublicationResult::AlreadyPublished &&
        targetCount.load() == 1,
        "Already-published code emitted a duplicate frame or call targets");

  void* concurrentPage = VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE,
                                      PAGE_READWRITE);
  Check(concurrentPage != nullptr, "Cannot allocate concurrent publication page");
  std::unique_ptr<void, FreePage> concurrentPageOwner(concurrentPage);
  memset(concurrentPage, 0xA5, codeSize);
  UINT_PTR concurrentAddress = reinterpret_cast<UINT_PTR>(concurrentPage);
  std::atomic<int> concurrentTargetCount{0};
  std::barrier startGate(3);
  FunctionCodePublicationResult firstResult;
  FunctionCodePublicationResult secondResult;
  auto publishConcurrent = [&](FunctionCodePublicationResult& result) {
    startGate.arrive_and_wait();
    result = PublishFunctionCode(
        client, sessionEnded, addressesLock, recordedAddresses, 0x5678,
        concurrentAddress, 3, 42, codeSize, concurrentPage,
        [&]() { ++concurrentTargetCount; });
  };
  std::thread first(publishConcurrent, std::ref(firstResult));
  std::thread second(publishConcurrent, std::ref(secondResult));
  startGate.arrive_and_wait();
  first.join();
  second.join();
  Check((firstResult == FunctionCodePublicationResult::Published &&
         secondResult == FunctionCodePublicationResult::AlreadyPublished) ||
        (secondResult == FunctionCodePublicationResult::Published &&
         firstResult == FunctionCodePublicationResult::AlreadyPublished),
        "Concurrent publication did not reserve the code address exactly once");
  Check(concurrentTargetCount.load() == 1,
        "Concurrent publication sent call targets more than once");
  ReadServerBytes(server, reinterpret_cast<char*>(&header), sizeof(header));
  Check(header.Kind == PipeMessageKind::FunctionCode,
        "Concurrent publication emitted an unexpected frame");
  body.resize(header.Size - sizeof(PipeMessageHeader));
  ReadServerBytes(server, body.data(), static_cast<DWORD>(body.size()));
  DWORD available = 0;
  Check(PeekNamedPipe(server, nullptr, 0, nullptr, &available, nullptr) &&
        available == 0,
        "Concurrent duplicate publication emitted an extra pipe frame");
}

static void TestTransportFailureEndsPublicationSession() {
  wchar_t name[128];
  swprintf_s(name, L"\\\\.\\pipe\\PEXValidation-PublicationFailure-%lu-%lu",
             GetCurrentProcessId(), GetTickCount());
  HANDLE server = CreateNamedPipeW(name, PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                                   PIPE_TYPE_BYTE | PIPE_WAIT, 1, 4096, 4096, 0, nullptr);
  Check(server != INVALID_HANDLE_VALUE, "Cannot create failed-publication test pipe");
  std::unique_ptr<void, decltype(&CloseHandle)> serverOwner(server, &CloseHandle);
  NamedPipeClient client;
  Check(client.Initialize(name), "Cannot connect failed-publication client");
  Check(ConnectNamedPipe(server, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED,
        "Cannot accept failed-publication client");

  std::mutex addressesLock;
  std::unordered_set<UINT_PTR> recordedAddresses;
  std::atomic<bool> sessionEnded{false};
  std::atomic<int> targetCount{0};
  void* page = VirtualAlloc(nullptr, 64 * 1024, MEM_COMMIT | MEM_RESERVE,
                            PAGE_READWRITE);
  Check(page != nullptr, "Cannot allocate blocked-publication page");
  memset(page, 0x5A, 64 * 1024);
  struct FreePage {
    void operator()(void* address) const {
      if (address != nullptr) {
        VirtualFree(address, 0, MEM_RELEASE);
      }
    }
  };
  std::unique_ptr<void, FreePage> pageOwner(page);
  auto sending = std::async(std::launch::async, [&]() {
    return PublishFunctionCode(
        client, sessionEnded, addressesLock, recordedAddresses, 0x1234,
        reinterpret_cast<UINT_PTR>(page), 7, 42, 64 * 1024, page,
        [&]() { ++targetCount; });
  });
  struct DisconnectBeforeJoin {
    NamedPipeClient& client;
    ~DisconnectBeforeJoin() { client.Disconnect(); }
  } disconnectBeforeJoin{client};

  PipeMessageHeader header{};
  ReadServerBytes(server, reinterpret_cast<char*>(&header), sizeof(header));
  Check(header.Kind == PipeMessageKind::FunctionCode,
        "Blocked publication did not start a code frame");
  char bodyByte = 0;
  ReadServerBytes(server, &bodyByte, sizeof(bodyByte));
  Check(static_cast<unsigned char>(bodyByte) == 0x34,
        "Blocked publication did not transmit the FunctionCode metadata");
  Check(sending.wait_for(std::chrono::milliseconds(0)) ==
            std::future_status::timeout,
        "Large code frame did not block on the undrained pipe");
  client.Disconnect();
  Check(sending.wait_for(std::chrono::seconds(5)) == std::future_status::ready,
        "Disconnect did not release the blocked code publication");
  Check(sending.get() == FunctionCodePublicationResult::TransportFailed,
        "Partial transport failure was not propagated");
  Check(sessionEnded.load() && targetCount.load() == 0,
        "Transport failure left the session active or sent call targets");
  Check(recordedAddresses.find(reinterpret_cast<UINT_PTR>(page)) !=
            recordedAddresses.end(),
        "Transport failure released an address with an unknown send outcome");
  Check(!client.SendMessage(PipeMessageKind::EndSession),
        "Pipe remained usable after a partial frame failure");
  Check(PublishFunctionCode(
            client, sessionEnded, addressesLock, recordedAddresses, 0x5678,
            reinterpret_cast<UINT_PTR>(page), 1, 42, 1, page,
            [&]() { ++targetCount; }) ==
            FunctionCodePublicationResult::TransportFailed,
        "Publication reused a session after a partial frame failure");
  Check(targetCount.load() == 0,
        "Call targets were sent after transport failure");
}

static bool GuardPageExceptionPropagates(const void* source) {
  char destination[64]{};
  __try {
    TryCopyCodeBytes(destination, source, sizeof(destination));
    return false;
  } __except (GetExceptionCode() == EXCEPTION_GUARD_PAGE
                  ? EXCEPTION_EXECUTE_HANDLER
                  : EXCEPTION_CONTINUE_SEARCH) {
    return true;
  }
}

static void TestCodeCopyExceptionFilter() {
  Check(FilterCodeCopyException(EXCEPTION_ACCESS_VIOLATION) == EXCEPTION_EXECUTE_HANDLER,
        "Access violation was not handled");
  Check(FilterCodeCopyException(EXCEPTION_IN_PAGE_ERROR) == EXCEPTION_EXECUTE_HANDLER,
        "In-page error was not handled");
  Check(FilterCodeCopyException(EXCEPTION_GUARD_PAGE) == EXCEPTION_CONTINUE_SEARCH,
        "Guard-page exception was swallowed");

  struct FreePage {
    void operator()(void* address) const {
      if (address != nullptr) {
        VirtualFree(address, 0, MEM_RELEASE);
      }
    }
  };
  std::unique_ptr<void, FreePage> page(
      VirtualAlloc(nullptr, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
  Check(page != nullptr, "Cannot allocate guard-page test memory");
  DWORD oldProtection = 0;
  Check(VirtualProtect(page.get(), 4096, PAGE_READWRITE | PAGE_GUARD,
                       &oldProtection) != 0,
        "Cannot protect guard-page test memory");
  Check(GuardPageExceptionPropagates(page.get()),
        "Unrelated structured exception did not propagate");
}

int main() {
  try {
    static_assert(sizeof(PipeMessageHeader) == 8);
    static_assert(sizeof(RequestFunctionCodeMessage) == 24);
    static_assert(std::is_trivially_copyable_v<PipeMessageHeader>);
    static_assert(std::is_trivially_copyable_v<RequestFunctionCodeMessage>);
    static_assert(sizeof(FunctionCodeMessage) == 28);
    static_assert(sizeof(FunctionCallTargetMessage) == 28);
    TestRequestFrameValidation();
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
    TestDisconnectWaitsForPendingRead();
    TestUnmappedCodeBytesRejected();
    TestFunctionCodePublicationRetriesAndSuppressesDuplicates();
    TestTransportFailureEndsPublicationSession();
    TestCodeCopyExceptionFilter();
    puts("Native pipe framing and function identity checks passed.");
    return 0;
  } catch (const std::exception& ex) {
    fprintf(stderr, "%s\n", ex.what());
    return 1;
  }
}

#endif
