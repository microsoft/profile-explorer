#pragma once

#include <windows.h>
#include <TlHelp32.h>
#include <assert.h>
#include <cor.h>
#include <cordebug.h>
#include <corprof.h>
#include <crosscomp.h>
#include <dacprivate.h>
#include <metahost.h>
#include <stdio.h>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <string>

#undef min
#undef max

class CLRDataTarget : public ICLRDataTarget {
 public:
  HANDLE process_;

  CLRDataTarget() { process_ = GetCurrentProcess(); }

  virtual HRESULT STDMETHODCALLTYPE QueryInterface(THIS_ IN REFIID InterfaceId,
                                                   OUT PVOID* Interface) {
    if (InterfaceId == IID_IUnknown || InterfaceId == IID_ICLRDataTarget) {
      *Interface = (ICLRDataTarget*)this;
      // No need to refcount as this class is contained.
      return S_OK;
    } else {
      *Interface = nullptr;
      return E_NOINTERFACE;
    }
  }

  virtual STDMETHODIMP_(ULONG) AddRef(THIS) { return 1; }

  virtual STDMETHODIMP_(ULONG) Release(THIS) { return 0; }

  virtual HRESULT STDMETHODCALLTYPE GetMachineType(
      /* [out] */ ULONG32* machineType) {
    *machineType = IMAGE_FILE_MACHINE_AMD64;
    return S_OK;
  }

  virtual HRESULT STDMETHODCALLTYPE GetPointerSize(
      /* [out] */ ULONG32* pointerSize) {
    *pointerSize = 8;
    return S_OK;
  }

  virtual HRESULT STDMETHODCALLTYPE GetImageBase(
      /* [string][in] */ LPCWSTR imagePath,
      /* [out] */ CLRDATA_ADDRESS* baseAddress) {
    *baseAddress =
        (CLRDATA_ADDRESS)GetModuleBaseAddress(GetCurrentProcessId(), imagePath);
    return S_OK;
  }

  virtual HRESULT STDMETHODCALLTYPE ReadVirtual(
      /* [in] */ CLRDATA_ADDRESS address,
      /* [length_is][size_is][out] */ BYTE* buffer,
      /* [in] */ ULONG32 bytesRequested,
      /* [out] */ ULONG32* bytesRead) {
    SIZE_T read;
    ReadProcessMemory(process_, (LPCVOID)address, buffer, bytesRequested,
                      &read);
    *bytesRead = (ULONG32)read;
    return S_OK;
  }

  virtual HRESULT STDMETHODCALLTYPE WriteVirtual(
      /* [in] */ CLRDATA_ADDRESS address,
      /* [size_is][in] */ BYTE* buffer,
      /* [in] */ ULONG32 bytesRequested,
      /* [out] */ ULONG32* bytesWritten) {
    return E_NOTIMPL;
  }

  virtual HRESULT STDMETHODCALLTYPE GetTLSValue(
      /* [in] */ ULONG32 threadID,
      /* [in] */ ULONG32 index,
      /* [out] */ CLRDATA_ADDRESS* value) {
    return E_NOTIMPL;
  }

  virtual HRESULT STDMETHODCALLTYPE SetTLSValue(
      /* [in] */ ULONG32 threadID,
      /* [in] */ ULONG32 index,
      /* [in] */ CLRDATA_ADDRESS value) {
    return E_NOTIMPL;
  }

  virtual HRESULT STDMETHODCALLTYPE GetCurrentThreadID(
      /* [out] */ ULONG32* threadID) {
    return E_NOTIMPL;
  }

  virtual HRESULT STDMETHODCALLTYPE GetPlatform(
      /* [out] */ CorDebugPlatform* pTargetPlatform) {
    // CORDB_PLATFORM_WINDOWS_AMD64
    // ORDB_PLATFORM_WINDOWS_ARM64

    *pTargetPlatform = CORDB_PLATFORM_WINDOWS_AMD64;
    return S_OK;
  }

  virtual HRESULT STDMETHODCALLTYPE GetThreadContext(
      /* [in] */ ULONG32 threadID,
      /* [in] */ ULONG32 contextFlags,
      /* [in] */ ULONG32 contextSize,
      /* [size_is][out] */ BYTE* pContext) {
    CorDebugPlatform platform;
    GetPlatform(&platform);

    HRESULT result = E_FAIL;
    HANDLE hThread = OpenThread(THREAD_ALL_ACCESS, false, threadID);

    if (hThread) {
      if (platform == CORDB_PLATFORM_WINDOWS_X86) {
        WOW64_CONTEXT context;
        context.ContextFlags = contextFlags;

        if (Wow64GetThreadContext(hThread, &context)) {
          ZeroMemory(pContext, contextSize);
          CopyMemory(pContext, &context,
                     std::min(contextSize, (ULONG32)sizeof(context)));
          result = S_OK;
        }
      } else if (platform == CORDB_PLATFORM_WINDOWS_AMD64) {
        CONTEXT context;
        context.ContextFlags = contextFlags;

        if (::GetThreadContext(hThread, &context)) {
          ZeroMemory(pContext, contextSize);
          CopyMemory(pContext, &context,
                     std::min(contextSize, (ULONG32)sizeof(context)));
          result = S_OK;
        }
      }

      CloseHandle(hThread);
    }

    return result;
  }

  virtual HRESULT STDMETHODCALLTYPE SetThreadContext(
      /* [in] */ ULONG32 threadID,
      /* [in] */ ULONG32 contextSize,
      /* [size_is][in] */ BYTE* context) {
    return E_NOTIMPL;
  }

  virtual HRESULT STDMETHODCALLTYPE Request(
      /* [in] */ ULONG32 reqCode,
      /* [in] */ ULONG32 inBufferSize,
      /* [size_is][in] */ BYTE* inBuffer,
      /* [in] */ ULONG32 outBufferSize,
      /* [size_is][out] */ BYTE* outBuffer) {
    return E_NOTIMPL;
  }

  static uintptr_t GetModuleBaseAddress(DWORD procId, const wchar_t* modName) {
    uintptr_t modBaseAddr = 0;
    HANDLE hSnap = CreateToolhelp32Snapshot(
        TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, procId);
    if (hSnap != INVALID_HANDLE_VALUE) {
      MODULEENTRY32 modEntry;
      modEntry.dwSize = sizeof(modEntry);
      if (Module32First(hSnap, &modEntry)) {
        do {
          if (!_wcsicmp(modEntry.szModule, modName)) {
            modBaseAddr = (uintptr_t)modEntry.modBaseAddr;
            break;
          }
        } while (Module32Next(hSnap, &modEntry));
      }
    }
    CloseHandle(hSnap);
    return modBaseAddr;
  }

  static std::wstring GetModulePath(DWORD procId, const wchar_t* modName) {
    uintptr_t modBaseAddr = 0;
    HANDLE hSnap = CreateToolhelp32Snapshot(
        TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, procId);
    if (hSnap != INVALID_HANDLE_VALUE) {
      MODULEENTRY32 modEntry;
      modEntry.dwSize = sizeof(modEntry);
      if (Module32First(hSnap, &modEntry)) {
        do {
          if (!_wcsicmp(modEntry.szModule, modName)) {
            return modEntry.szExePath;
          }
        } while (Module32Next(hSnap, &modEntry));
      }
    }
    CloseHandle(hSnap);
    return L"";
  }

  static std::wstring GetDirectory(const std::wstring& path) {
    size_t found = path.find_last_of(L"/\\");
    return (path.substr(0, found));
  }

  static std::wstring FindDacBinary(ICorProfilerInfo8* _info, int machineType) {
    USHORT pClrInstanceId;
    COR_PRF_RUNTIME_TYPE pRuntimeType;
    USHORT pMajorVersion;
    USHORT pMinorVersion;
    USHORT pBuildNumber;
    USHORT pQFEVersion;
    WCHAR verstr[100];
    ULONG dummy;

    _info->GetRuntimeInformation(&pClrInstanceId, &pRuntimeType, &pMajorVersion,
                                 &pMinorVersion, &pBuildNumber, &pQFEVersion,
                                 100, &dummy, verstr);

    static const wchar_t* DesktopCLRModule = L"clr.dll";
    static const wchar_t* CoreCLRModule = L"coreclr.dll";
    static const wchar_t* DesktopDacModule = L"mscordacwks.dll";
    static const wchar_t* CoreDacModule = L"mscordaccore.dll";

    const wchar_t* clrModule;
    const wchar_t* dacModule;

    if (pRuntimeType == COR_PRF_CORE_CLR) {
      clrModule = CoreCLRModule;
      dacModule = CoreDacModule;
    } else {
      clrModule = DesktopCLRModule;
      dacModule = DesktopDacModule;
    }

    auto clrPath = GetModulePath(GetCurrentProcessId(), clrModule);
    auto clrDir = std::filesystem::path(clrPath).parent_path();
    auto dacPath = clrDir / std::filesystem::path(dacModule);

    if (std::filesystem::exists(dacPath)) {
      return dacPath.wstring();
    }

    return L"";
  }
};