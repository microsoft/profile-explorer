#include <assert.h>
#include <vector>
#include <string>
#include "CoreProfiler.h"
#include <stdio.h>
#include <TlHelp32.h>
#include <metahost.h>  
#include <crosscomp.h>
#include <dacprivate.h>
#include <cordebug.h>
#include <mutex>
#include <unordered_set>
#include <fstream>
#include <vector>
#include "CLRDataTarget.h"

static const wchar_t* ProfilerPipeName = L"\\\\.\\pipe\\IRXProfilerPipe";

CComPtr<ISOSDacInterface> dac_;
std::unordered_set<UINT_PTR> recordedAddrs_;
std::mutex lock_;
bool sessionEnded_;

HRESULT __stdcall CoreProfiler::QueryInterface(REFIID riid, void** ppvObject) {
  Log(L"IRX: QueryInterface");

  if (ppvObject == nullptr)
    return E_POINTER;

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

  return E_NOINTERFACE;
}

ULONG __stdcall CoreProfiler::AddRef(void) {
  return ++refCount_;
}

ULONG __stdcall CoreProfiler::Release(void) {
  auto count = --refCount_;
  if (count == 0)
    delete this;

  return count;
}

int64_t GetMethodHandleForIP(uint64_t ip) {
  uint64_t md = 0;

  if (FAILED(dac_->GetMethodDescPtrFromIP(ip, &md)) || md == 0) {
    DacpCodeHeaderData headerData;
    if (FAILED(dac_->GetCodeHeaderData(ip, &headerData))) {
      return 0;
    }

    md = headerData.MethodDescPtr;
  }

  return md;
}

void FindRuntimeArchitecture() {
  HRESULT hr;

  ICLRMetaHost* metaHost = nullptr;

  if ((hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost,
                              (LPVOID*)&metaHost)) != S_OK) {
    return;
  }

  CComPtr<IEnumUnknown> runtime;

  if ((hr = metaHost->EnumerateInstalledRuntimes(&runtime)) != S_OK) {
    return;
  }

  auto frameworkName = (LPWSTR)LocalAlloc(LPTR, 2048);
  IUnknown* enumRuntime;

  while (runtime->Next(1, &enumRuntime, 0) == S_OK) {
    CComPtr<ICLRRuntimeInfo> runtimeInfo;
    if (enumRuntime->QueryInterface<ICLRRuntimeInfo>(&runtimeInfo) == S_OK) {
      if (runtimeInfo != nullptr) {
        DWORD bytes;
        runtimeInfo->GetVersionString(frameworkName, &bytes);
      }
    }
  }
}

bool IsWindowsVersionOrGreater(WORD wMajorVersion,
                               WORD wMinorVersion = 0,
                               WORD wBuildNumber = 0) {
  OSVERSIONINFOEXW osvi = {};
  osvi.dwOSVersionInfoSize = sizeof(osvi);
  auto dwlConditionMask = VerSetConditionMask(
      VerSetConditionMask(0, VER_MAJORVERSION, VER_GREATER_EQUAL),
      VER_MINORVERSION, VER_GREATER_EQUAL);
  if (wBuildNumber != 0) {
    VerSetConditionMask(dwlConditionMask, VER_BUILDNUMBER, VER_GREATER_EQUAL);
  }

  osvi.dwMajorVersion = wMajorVersion;
  osvi.dwMinorVersion = wMinorVersion;
  osvi.wServicePackMajor = wBuildNumber;

  return VerifyVersionInfoW(
             &osvi, VER_MAJORVERSION | VER_MINORVERSION | VER_SERVICEPACKMAJOR,
             dwlConditionMask) != FALSE;
}

bool TryGetWow64(HANDLE proc, bool& result) {
  if (IsWindowsVersionOrGreater(5, 1)) {
    BOOL value;
    return IsWow64Process(proc, &value);
  }

  return false;
}

bool TryGetWow64_2(HANDLE proc, USHORT& processMachine, USHORT& nativeMachine) {
  if (IsWindowsVersionOrGreater(11) ||
      IsWindowsVersionOrGreater(10, 0, 10586)) {
    return IsWow64Process2(proc, &processMachine, &nativeMachine);
  }

  return false;
}

USHORT GetMachineType() {
  SYSTEM_INFO sysInfo;
  GetSystemInfo(&sysInfo);

  switch (sysInfo.wProcessorArchitecture) {
    case PROCESSOR_ARCHITECTURE_AMD64:
      return IMAGE_FILE_MACHINE_AMD64;
    case PROCESSOR_ARCHITECTURE_ARM:
      return IMAGE_FILE_MACHINE_ARM;
    case PROCESSOR_ARCHITECTURE_ARM64:
      return IMAGE_FILE_MACHINE_ARM64;
    default:
      return IMAGE_FILE_MACHINE_I386;
  }
}

USHORT GetTargetMachine() {
  auto handle = GetCurrentProcess();

  USHORT processMachine = IMAGE_FILE_MACHINE_UNKNOWN;
  USHORT nativeMachine = IMAGE_FILE_MACHINE_UNKNOWN;
  USHORT targetMachine = IMAGE_FILE_MACHINE_UNKNOWN;

  if (TryGetWow64_2(handle, processMachine, nativeMachine)) {
    targetMachine = processMachine != IMAGE_FILE_MACHINE_UNKNOWN
                        ? processMachine
                        : nativeMachine;
  } else {
    bool isWow64 = false;
    TryGetWow64(handle, isWow64);
    targetMachine = isWow64 ? IMAGE_FILE_MACHINE_I386 : GetMachineType();
  }

  return targetMachine;
}

HRESULT CoreProfiler::Initialize(IUnknown* pICorProfilerInfoUnk) {
  Log("IRXProfiler: Initialize");

  pICorProfilerInfoUnk->QueryInterface(&profilerInfo_);
  profilerInfo_->SetEventMask2(
      // COR_PRF_MONITOR_MODULE_LOADS |
      // COR_PRF_MONITOR_ASSEMBLY_LOADS |
      // COR_PRF_MONITOR_GC |
      // COR_PRF_MONITOR_CLASS_LOADS |
      // COR_PRF_MONITOR_THREADS |
      // COR_PRF_MONITOR_EXCEPTIONS |
      COR_PRF_MONITOR_JIT_COMPILATION, COR_PRF_HIGH_MONITOR_EVENT_PIPE);

  /*while (!::IsDebuggerPresent())
  {
          Log(L"IRX: waiting");
          ::Sleep(1000);
  }*/

  sessionEnded_.store(false);
  recordedAddrs_.clear();

  machineType_ = GetTargetMachine();
  processId_ = GetCurrentProcessId();
  pipeClient_ = new NamedPipeClient();

  if (!pipeClient_->Initialize(ProfilerPipeName)) {
    Log("IRXProfiler: Failed to connect to pipe\n");
    return S_OK;
  }

  Log("IRXProfiler: Connected to pipe for proc %d\n", processId_);

  pipeClientThread_ = new std::thread([this]() {
    Log("IRXProfiler: Started pipe thread\n");
    bool canceled = false;

    pipeClient_->ReceiveMessages(
        [&](PipeMessageHeader header, std::shared_ptr<char[]> messageBody) {
          Log("IRXProfiler: Message %d, size %d\n", header.Kind, header.Size);

          switch (header.Kind) {
            case PipeMessageKind::RequestFunctionCode: {
              auto request = (RequestFunctionCodeMessage*)messageBody.get();
              Log("IRXProfiler: Request %lld id %lld\n", request->Address,
                  request->FunctionId);

              if (request->ProcessId == processId_) {
                SendRequestedFunctionCode(*request);
              }
              break;
            }
            case PipeMessageKind::EndSession: {
              sessionEnded_.store(true);
              pipeClient_->Disconnect();

              Log("IRXProfiler: Detaching profiler for proc %d\n", processId_);

              /*if (FAILED(profilerInfo_->RequestProfilerDetach(10000))) {
                      Log("IRXProfiler: Failed to detach proc %d\n",
              processId_);
              }
              else {
                      Log("IRXProfiler: Profiler detached for proc %d\n",
              processId_);
              }*/

              return;  // Exit thread.
            }
          }
        },
        canceled);

    Log(">IRX: Stop pipe thread\n");
  });

  // Load DAC, used mostly to get JIT helper function names.
  auto dacPath = CLRDataTarget::FindDacBinary(profilerInfo_, machineType_);
  auto dacModule = LoadLibrary(dacPath.c_str());

  if (dacModule) {
    auto createProc = GetProcAddress(dacModule, "CLRDataCreateInstance");
    auto createFunc = (PFN_CLRDataCreateInstance)createProc;
    auto clrDataTarget = new CLRDataTarget();
    IXCLRDataProcess* dataProc;

    auto result = createFunc(__uuidof(IXCLRDataProcess), clrDataTarget,
                             (void**)&dataProc);

    if (SUCCEEDED(result)) {
      auto result = dataProc->QueryInterface(&dac_);

      if (SUCCEEDED(result)) {
        Log("IRXProfiler: DAC initialized");
      } else {
        Log("IRXProfiler: DAC initialization failed: %d", result);
      }
    }
  }

  return S_OK;
}

bool CoreProfiler::SendRequestedFunctionCode(
    RequestFunctionCodeMessage& request) {
  if (sessionEnded_.load()) {
    return true;
  }

  Log("IRXProfiler: SendRequestedFunctionCode: %s",
      GetMethodName(request.FunctionId).c_str());

  ModuleID module;
  mdToken token;
  mdTypeDef type;
  ClassID classId;
  if (FAILED(profilerInfo_->GetFunctionInfo(request.FunctionId, &classId,
                                            &module, &token))) {
    Log("IRXProfiler: Failed GetFunctionInfo\n");
    return false;
  }

  ULONG rejitCount;
  if (FAILED(profilerInfo_->GetReJITIDs(request.FunctionId, 0, &rejitCount,
                                        nullptr))) {
    Log("IRXProfiler: Failed GetReJITIDs\n");
    return false;
  }

  std::vector<ReJITID> rejitIds(rejitCount);  //? TODO: Use pre-allocated array

  if (FAILED(profilerInfo_->GetReJITIDs(request.FunctionId, rejitCount,
                                        &rejitCount, rejitIds.data()))) {
    Log("IRXProfiler: Failed GetReJITIDs\n");
    return false;
  }

  for (auto&& rejit : rejitIds) {
    if (rejit != request.ReJITId) {
      continue;
    }

    // Log("IRXProfiler: Handle RejitID %d\n", rejit);
    ULONG32 addrs = 0;
    profilerInfo_->GetNativeCodeStartAddresses(request.FunctionId, rejit, 0,
                                               &addrs, nullptr);
    std::vector<UINT_PTR> addr(addrs);
    profilerInfo_->GetNativeCodeStartAddresses(request.FunctionId, rejit, addrs,
                                               &addrs, addr.data());

    for (int rejit = 0; rejit < addrs; rejit++) {
      ULONG32 cCodeInfos2;
      profilerInfo_->GetCodeInfo4(addr[rejit], 0, &cCodeInfos2, nullptr);
      std::vector<COR_PRF_CODE_INFO> codeInfos2(cCodeInfos2);
      profilerInfo_->GetCodeInfo4(addr[rejit], cCodeInfos2, &cCodeInfos2,
                                  codeInfos2.data());

      for (auto&& codeInfo : codeInfos2) {
        //? TODO: Lock could be moved down if R/W
        //?   also not needed after adding
        std::lock_guard<std::mutex> lock(lock_);

        if (recordedAddrs_.find(codeInfo.startAddress) !=
            recordedAddrs_.end()) {
          continue;
        }

        recordedAddrs_.insert(codeInfo.startAddress);
        SendLoadedFunctionCode(request.FunctionId, codeInfo.startAddress, rejit,
                               (uint32_t)codeInfo.size,
                               (char*)codeInfo.startAddress);
        SendCallTargets(request.FunctionId, rejit, (uint32_t)codeInfo.size,
                        (char*)codeInfo.startAddress);
      }
    }
  }

  return true;
}

bool CoreProfiler::SendLoadedFunctionCode(uint64_t funcId,
                                          uint64_t address,
                                          uint32_t rejitId,
                                          uint32_t codeSize,
                                          char* codeByes) {
  if (pipeClient_ != nullptr) {
    Log("IRXProfiler: Sending code for funcId %llu, IP %llu, code size %d\n",
        funcId, address, codeSize);
    SendFunctionCode(*pipeClient_, funcId, address, rejitId, processId_,
                     codeSize, codeByes);
    Log("IRXProfiler: Sent code for funcId %llu, IP %llu, code size %d\n",
        funcId, address, codeSize);
  }

  return true;
}

HRESULT CoreProfiler::Shutdown() {
  Log("IRXProfiler: Shutdown");

  profilerInfo_.Release();
  return S_OK;
}

HRESULT CoreProfiler::AppDomainCreationStarted(AppDomainID appDomainId) {
  return S_OK;
}

HRESULT CoreProfiler::AppDomainCreationFinished(AppDomainID appDomainId,
                                                HRESULT hrStatus) {
  return S_OK;
}

HRESULT CoreProfiler::AppDomainShutdownStarted(AppDomainID appDomainId) {
  return S_OK;
}

HRESULT CoreProfiler::AppDomainShutdownFinished(AppDomainID appDomainId,
                                                HRESULT hrStatus) {
  return S_OK;
}

HRESULT CoreProfiler::AssemblyLoadStarted(AssemblyID assemblyId) {
  return S_OK;
}

HRESULT CoreProfiler::AssemblyLoadFinished(AssemblyID assemblyId,
                                           HRESULT hrStatus) {
  WCHAR name[512];
  ULONG size;
  AppDomainID ad;
  return S_OK;
}

HRESULT CoreProfiler::AssemblyUnloadStarted(AssemblyID assemblyId) {
  return S_OK;
}

HRESULT CoreProfiler::AssemblyUnloadFinished(AssemblyID assemblyId,
                                             HRESULT hrStatus) {
  return S_OK;
}

HRESULT CoreProfiler::ModuleLoadStarted(ModuleID moduleId) {
  return S_OK;
}

HRESULT CoreProfiler::ModuleLoadFinished(ModuleID moduleId, HRESULT hrStatus) {
  return S_OK;
}

HRESULT CoreProfiler::ModuleUnloadStarted(ModuleID moduleId) {
  return S_OK;
}

HRESULT CoreProfiler::ModuleUnloadFinished(ModuleID moduleId,
                                           HRESULT hrStatus) {
  return S_OK;
}

HRESULT CoreProfiler::ModuleAttachedToAssembly(ModuleID moduleId,
                                               AssemblyID AssemblyId) {
  return S_OK;
}

HRESULT CoreProfiler::ClassLoadStarted(ClassID classId) {
  return S_OK;
}

HRESULT CoreProfiler::ClassLoadFinished(ClassID classId, HRESULT hrStatus) {
  return S_OK;
}

HRESULT CoreProfiler::ClassUnloadStarted(ClassID classId) {
  return S_OK;
}

HRESULT CoreProfiler::ClassUnloadFinished(ClassID classId, HRESULT hrStatus) {
  return S_OK;
}

HRESULT CoreProfiler::FunctionUnloadStarted(FunctionID functionId) {
  return S_OK;
}

HRESULT CoreProfiler::JITCompilationStarted(FunctionID functionId,
                                            BOOL fIsSafeToBlock) {
  // Logger::Debug("JIT compilation started: %s",
  // GetMethodName(functionId).c_str());
  return S_OK;
}

HRESULT CoreProfiler::JITCompilationFinished(FunctionID functionId,
                                             HRESULT hrStatus,
                                             BOOL fIsSafeToBlock) {
  Log("IRXProfiler: JITCompilationFinished: %s",
      GetMethodName(functionId).c_str());

  if (fIsSafeToBlock) {
    profilerInfo_->SuspendRuntime();
  }

  HandleLoadedFunction(functionId);

  if (fIsSafeToBlock) {
    profilerInfo_->ResumeRuntime();
  }

  return S_OK;
}

bool CoreProfiler::SendCallTargetName(uint64_t ip,
                                      uint64_t funcId,
                                      uint32_t rejitId) {
  auto md = GetMethodHandleForIP(ip);

  if (md != 0) {
    unsigned needed;
    if (SUCCEEDED(dac_->GetMethodDescName(md, 0, nullptr, &needed))) {
      auto buffer = std::make_unique<std::wstring>(needed - 1, 0);

      if (SUCCEEDED(dac_->GetMethodDescName(
              md, needed, (wchar_t*)buffer->c_str(), &needed))) {
        //? Hacky conversion to UTF8.
        auto result = std::make_unique<std::string>(needed - 1, 0);
        for (size_t i = 0; i < needed; i++) {
          (*result)[i] = (char)(*buffer)[i];
        }

        SendFunctionCallTarget(*pipeClient_, funcId, ip, rejitId, processId_,
                               result->size() + 1, result->c_str());
      }
    }
  } else {
    unsigned needed;
    if (SUCCEEDED(dac_->GetJitHelperFunctionName(ip, 0, nullptr, &needed))) {
      char buffer[1024];

      if (SUCCEEDED(
              dac_->GetJitHelperFunctionName(ip, needed, buffer, &needed))) {
        SendFunctionCallTarget(*pipeClient_, funcId, ip, rejitId, processId_,
                               needed, buffer);
      }
    }
  }

  return true;
}

bool CoreProfiler::HandleLoadedFunction(uint64_t functionId) {
  if (sessionEnded_.load()) {
    return true;
  }

  Log("IRXProfiler: JITCompilationFinished: %s",
      GetMethodName(functionId).c_str());

  ModuleID module;
  mdToken token;
  mdTypeDef type;
  ClassID classId;
  if (FAILED(profilerInfo_->GetFunctionInfo(functionId, &classId, &module,
                                            &token))) {
    Log("IRXProfiler: Failed GetFunctionInfo\n");
    return false;
  }

  ULONG rejitCount;
  if (FAILED(profilerInfo_->GetReJITIDs(functionId, 0, &rejitCount, nullptr))) {
    Log("IRXProfiler: Failed GetReJITIDs\n");
    return false;
  }

  std::vector<ReJITID> rejitIds(rejitCount);  //? TODO: Use pre-allocated array

  if (FAILED(profilerInfo_->GetReJITIDs(functionId, rejitCount, &rejitCount,
                                        rejitIds.data()))) {
    Log("IRXProfiler: Failed GetReJITIDs\n");
    return false;
  }

  for (auto&& rejit : rejitIds) {
    ULONG32 addrs = 0;
    profilerInfo_->GetNativeCodeStartAddresses(functionId, rejit, 0, &addrs,
                                               nullptr);
    std::vector<UINT_PTR> addr(addrs);
    profilerInfo_->GetNativeCodeStartAddresses(functionId, rejit, addrs, &addrs,
                                               addr.data());

    for (int rejit = 0; rejit < addrs; rejit++) {
      ULONG32 cCodeInfos2;
      profilerInfo_->GetCodeInfo4(addr[rejit], 0, &cCodeInfos2, nullptr);
      std::vector<COR_PRF_CODE_INFO> codeInfos2(cCodeInfos2);
      profilerInfo_->GetCodeInfo4(addr[rejit], cCodeInfos2, &cCodeInfos2,
                                  codeInfos2.data());

      for (auto&& codeInfo : codeInfos2) {
        //? TODO: Lock could be moved down if R/W
        //?   also not needed after adding
        std::lock_guard<std::mutex> lock(lock_);

        if (recordedAddrs_.find(codeInfo.startAddress) !=
            recordedAddrs_.end()) {
          continue;
        }

        recordedAddrs_.insert(codeInfo.startAddress);
        SendLoadedFunctionCode(functionId, codeInfo.startAddress, rejit,
                               (uint32_t)codeInfo.size,
                               (char*)codeInfo.startAddress);
        SendCallTargets(functionId, rejit, (uint32_t)codeInfo.size,
                        (char*)codeInfo.startAddress);
      }
    }
  }

  return true;
}

bool CoreProfiler::SendCallTargets(uint64_t funcId,
                                   uint32_t rejitId,
                                   uint32_t codeSize,
                                   char* codeBytes) {
  if (sessionEnded_.load()) {
    return true;
  }

  __try {
    switch (machineType_) {
      case IMAGE_FILE_MACHINE_AMD64: {
        CollectCallTargets<CX86Disasm64>(funcId, rejitId, codeBytes, codeSize);
        break;
      }
      case IMAGE_FILE_MACHINE_ARM: {
        //? TODO: Is ARM32 required anymore?
        CollectCallTargetsArm64(funcId, rejitId, codeBytes, codeSize);
        break;
      }
      case IMAGE_FILE_MACHINE_ARM64: {
        CollectCallTargetsArm64(funcId, rejitId, codeBytes, codeSize);
        break;
      }
      case IMAGE_FILE_MACHINE_I386: {
        CollectCallTargets<CX86Disasm86>(funcId, rejitId, codeBytes, codeSize);
        break;
      }
    }
  } __except (EXCEPTION_EXECUTE_HANDLER) {
    // Log("Exception disassembling: %llx, size %d\n", codeInfo.startAddress,
    // codeSize);
    return false;
  }

  return true;
}

HRESULT CoreProfiler::JITCachedFunctionSearchStarted(
    FunctionID functionId,
    BOOL* pbUseCachedFunction) {
  return S_OK;
}

HRESULT CoreProfiler::JITCachedFunctionSearchFinished(
    FunctionID functionId,
    COR_PRF_JIT_CACHE result) {
  return S_OK;
}

HRESULT CoreProfiler::JITFunctionPitched(FunctionID functionId) {
  return S_OK;
}

HRESULT CoreProfiler::JITInlining(FunctionID callerId,
                                  FunctionID calleeId,
                                  BOOL* pfShouldInline) {
  return S_OK;
}

HRESULT CoreProfiler::ThreadCreated(ThreadID threadId) {
  return S_OK;
}

HRESULT CoreProfiler::ThreadDestroyed(ThreadID threadId) {
  return S_OK;
}

HRESULT CoreProfiler::ThreadAssignedToOSThread(ThreadID managedThreadId,
                                               DWORD osThreadId) {
  return S_OK;
}

HRESULT CoreProfiler::RemotingClientInvocationStarted() {
  return S_OK;
}

HRESULT CoreProfiler::RemotingClientSendingMessage(GUID* pCookie,
                                                   BOOL fIsAsync) {
  return S_OK;
}

HRESULT CoreProfiler::RemotingClientReceivingReply(GUID* pCookie,
                                                   BOOL fIsAsync) {
  return S_OK;
}

HRESULT CoreProfiler::RemotingClientInvocationFinished() {
  return S_OK;
}

HRESULT CoreProfiler::RemotingServerReceivingMessage(GUID* pCookie,
                                                     BOOL fIsAsync) {
  return S_OK;
}

HRESULT CoreProfiler::RemotingServerInvocationStarted() {
  return S_OK;
}

HRESULT CoreProfiler::RemotingServerInvocationReturned() {
  return S_OK;
}

HRESULT CoreProfiler::RemotingServerSendingReply(GUID* pCookie, BOOL fIsAsync) {
  return S_OK;
}

HRESULT CoreProfiler::UnmanagedToManagedTransition(
    FunctionID functionId,
    COR_PRF_TRANSITION_REASON reason) {
  return S_OK;
}

HRESULT CoreProfiler::ManagedToUnmanagedTransition(
    FunctionID functionId,
    COR_PRF_TRANSITION_REASON reason) {
  return S_OK;
}

HRESULT CoreProfiler::RuntimeSuspendStarted(
    COR_PRF_SUSPEND_REASON suspendReason) {
  return S_OK;
}

HRESULT CoreProfiler::RuntimeSuspendFinished() {
  return S_OK;
}

HRESULT CoreProfiler::RuntimeSuspendAborted() {
  return S_OK;
}

HRESULT CoreProfiler::RuntimeResumeStarted() {
  return S_OK;
}

HRESULT CoreProfiler::RuntimeResumeFinished() {
  return S_OK;
}

HRESULT CoreProfiler::RuntimeThreadSuspended(ThreadID threadId) {
  return S_OK;
}

HRESULT CoreProfiler::RuntimeThreadResumed(ThreadID threadId) {
  return S_OK;
}

HRESULT CoreProfiler::MovedReferences(ULONG cMovedObjectIDRanges,
                                      ObjectID* oldObjectIDRangeStart,
                                      ObjectID* newObjectIDRangeStart,
                                      ULONG* cObjectIDRangeLength) {
  return S_OK;
}

HRESULT CoreProfiler::ObjectAllocated(ObjectID objectId, ClassID classId) {
  /*ModuleID module;
  mdTypeDef type;
  if (SUCCEEDED(_info->GetClassIDInfo(classId, &module, &type))) {
          auto name = GetTypeName(type, module);
  }*/
  return S_OK;
}

HRESULT CoreProfiler::ObjectsAllocatedByClass(ULONG cClassCount,
                                              ClassID* classIds,
                                              ULONG* cObjects) {
  return S_OK;
}

HRESULT CoreProfiler::ObjectReferences(ObjectID objectId,
                                       ClassID classId,
                                       ULONG cObjectRefs,
                                       ObjectID* objectRefIds) {
  return S_OK;
}

HRESULT CoreProfiler::RootReferences(ULONG cRootRefs, ObjectID* rootRefIds) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionThrown(ObjectID thrownObjectId) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionSearchFunctionEnter(FunctionID functionId) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionSearchFunctionLeave() {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionSearchFilterEnter(FunctionID functionId) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionSearchFilterLeave() {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionSearchCatcherFound(FunctionID functionId) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionOSHandlerEnter(UINT_PTR __unused) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionOSHandlerLeave(UINT_PTR __unused) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionUnwindFunctionEnter(FunctionID functionId) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionUnwindFunctionLeave() {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionUnwindFinallyEnter(FunctionID functionId) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionUnwindFinallyLeave() {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionCatcherEnter(FunctionID functionId,
                                            ObjectID objectId) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionCatcherLeave() {
  return S_OK;
}

HRESULT CoreProfiler::COMClassicVTableCreated(ClassID wrappedClassId,
                                              const GUID& implementedIID,
                                              void* pVTable,
                                              ULONG cSlots) {
  return S_OK;
}

HRESULT CoreProfiler::COMClassicVTableDestroyed(ClassID wrappedClassId,
                                                const GUID& implementedIID,
                                                void* pVTable) {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionCLRCatcherFound() {
  return S_OK;
}

HRESULT CoreProfiler::ExceptionCLRCatcherExecute() {
  return S_OK;
}

HRESULT CoreProfiler::ThreadNameChanged(ThreadID threadId,
                                        ULONG cchName,
                                        WCHAR* name) {
  return S_OK;
}

HRESULT CoreProfiler::GarbageCollectionStarted(int cGenerations,
                                               BOOL* generationCollected,
                                               COR_PRF_GC_REASON reason) {
  return S_OK;
}

HRESULT CoreProfiler::SurvivingReferences(ULONG cSurvivingObjectIDRanges,
                                          ObjectID* objectIDRangeStart,
                                          ULONG* cObjectIDRangeLength) {
  return S_OK;
}

HRESULT CoreProfiler::GarbageCollectionFinished() {
  return S_OK;
}

HRESULT CoreProfiler::FinalizeableObjectQueued(DWORD finalizerFlags,
                                               ObjectID objectID) {
  return S_OK;
}

HRESULT CoreProfiler::RootReferences2(ULONG cRootRefs,
                                      ObjectID* rootRefIds,
                                      COR_PRF_GC_ROOT_KIND* rootKinds,
                                      COR_PRF_GC_ROOT_FLAGS* rootFlags,
                                      UINT_PTR* rootIds) {
  return S_OK;
}

HRESULT CoreProfiler::HandleCreated(GCHandleID handleId,
                                    ObjectID initialObjectId) {
  return S_OK;
}

HRESULT CoreProfiler::HandleDestroyed(GCHandleID handleId) {
  return S_OK;
}

HRESULT CoreProfiler::InitializeForAttach(IUnknown* pCorProfilerInfoUnk,
                                          void* pvClientData,
                                          UINT cbClientData) {
  Log("IRXProfiler: InitializeForAttach, data %d\n", cbClientData);
  return Initialize(pCorProfilerInfoUnk);
}

HRESULT CoreProfiler::ProfilerAttachComplete() {
  Log("IRXProfiler: ProfilerAttachComplete\n");

  return S_OK;
}

HRESULT CoreProfiler::ProfilerDetachSucceeded() {
  Log("IRXProfiler: ProfilerDetachSucceeded");

  return S_OK;
}

HRESULT CoreProfiler::ReJITCompilationStarted(FunctionID functionId,
                                              ReJITID rejitId,
                                              BOOL fIsSafeToBlock) {
  return S_OK;
}

HRESULT CoreProfiler::GetReJITParameters(
    ModuleID moduleId,
    mdMethodDef methodId,
    ICorProfilerFunctionControl* pFunctionControl) {
  return S_OK;
}

HRESULT CoreProfiler::ReJITCompilationFinished(FunctionID functionId,
                                               ReJITID rejitId,
                                               HRESULT hrStatus,
                                               BOOL fIsSafeToBlock) {
  return S_OK;
}

HRESULT CoreProfiler::ReJITError(ModuleID moduleId,
                                 mdMethodDef methodId,
                                 FunctionID functionId,
                                 HRESULT hrStatus) {
  return S_OK;
}

HRESULT CoreProfiler::MovedReferences2(ULONG cMovedObjectIDRanges,
                                       ObjectID* oldObjectIDRangeStart,
                                       ObjectID* newObjectIDRangeStart,
                                       SIZE_T* cObjectIDRangeLength) {
  return S_OK;
}

HRESULT CoreProfiler::SurvivingReferences2(ULONG cSurvivingObjectIDRanges,
                                           ObjectID* objectIDRangeStart,
                                           SIZE_T* cObjectIDRangeLength) {
  return S_OK;
}

HRESULT CoreProfiler::ConditionalWeakTableElementReferences(
    ULONG cRootRefs,
    ObjectID* keyRefIds,
    ObjectID* valueRefIds,
    GCHandleID* rootIds) {
  return S_OK;
}

HRESULT CoreProfiler::GetAssemblyReferences(
    const WCHAR* wszAssemblyPath,
    ICorProfilerAssemblyReferenceProvider* pAsmRefProvider) {
  return S_OK;
}

HRESULT CoreProfiler::ModuleInMemorySymbolsUpdated(ModuleID moduleId) {
  return S_OK;
}

HRESULT CoreProfiler::DynamicMethodJITCompilationStarted(FunctionID functionId,
                                                         BOOL fIsSafeToBlock,
                                                         LPCBYTE pILHeader,
                                                         ULONG cbILHeader) {
  return S_OK;
}

HRESULT CoreProfiler::DynamicMethodJITCompilationFinished(FunctionID functionId,
                                                          HRESULT hrStatus,
                                                          BOOL fIsSafeToBlock) {
  return S_OK;
}

HRESULT STDMETHODCALLTYPE
CoreProfiler::EventPipeEventDelivered(EVENTPIPE_PROVIDER provider,
                                      DWORD eventId,
                                      DWORD eventVersion,
                                      ULONG cbMetadataBlob,
                                      LPCBYTE metadataBlob,
                                      ULONG cbEventData,
                                      LPCBYTE eventData,
                                      LPCGUID pActivityId,
                                      LPCGUID pRelatedActivityId,
                                      ThreadID eventThread,
                                      ULONG numStackFrames,
                                      UINT_PTR stackFrames[]) {
  return S_OK;
}

HRESULT CoreProfiler::EventPipeProviderCreated(EVENTPIPE_PROVIDER provider) {
  Log("IRXProfiler: Created provider %llu\n", provider);
  return S_OK;
}

std::string UnicodeToAnsi(const WCHAR* str) {
#ifdef _WINDOWS
  std::wstring ws(str);
#else
  std::basic_string<WCHAR> ws(str);
#endif
  return std::string(ws.begin(), ws.end());
}

std::string CoreProfiler::GetTypeName(mdTypeDef type, ModuleID module) const {
  CComPtr<IMetaDataImport> spMetadata;
  if (SUCCEEDED(profilerInfo_->GetModuleMetaData(
          module, ofRead, IID_IMetaDataImport,
          reinterpret_cast<IUnknown**>(&spMetadata)))) {
    WCHAR name[256];
    ULONG nameSize = 256;
    DWORD flags;
    mdTypeDef baseType;
    if (SUCCEEDED(spMetadata->GetTypeDefProps(type, name, 256, &nameSize,
                                              &flags, &baseType))) {
      return UnicodeToAnsi(name);
    }
  }
  return "";
}

std::string CoreProfiler::GetMethodName(FunctionID function) const {
  ModuleID module;
  mdToken token;
  mdTypeDef type;
  ClassID classId;
  if (FAILED(
          profilerInfo_->GetFunctionInfo(function, &classId, &module, &token)))
    return "";

  CComPtr<IMetaDataImport> spMetadata;
  if (FAILED(profilerInfo_->GetModuleMetaData(
          module, ofRead, IID_IMetaDataImport,
          reinterpret_cast<IUnknown**>(&spMetadata))))
    return "";
  PCCOR_SIGNATURE sig;
  ULONG blobSize, size, attributes;
  WCHAR name[256];
  DWORD flags;
  ULONG codeRva;
  if (FAILED(spMetadata->GetMethodProps(token, &type, name, 256, &size,
                                        &attributes, &sig, &blobSize, &codeRva,
                                        &flags)))
    return "";

  return GetTypeName(type, module) + "::" + UnicodeToAnsi(name);
}
