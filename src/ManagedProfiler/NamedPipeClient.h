// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
#include <stdio.h>
#include <windows.h>
#include <atomic>
#include <memory>
#include <mutex>
#pragma once

enum class PipeMessageKind : int32_t {
  StartSession,
  EndSession,
  FunctionCode,
  FunctionCallTarget,
  RequestFunctionCode,
};

#pragma pack(push, 1)
struct PipeMessageHeader {
  PipeMessageKind Kind;
  int32_t Size;
};
#pragma pack(pop)

#pragma pack(push, 1)
struct FunctionCodeMessage {
  int64_t FunctionId;
  int64_t Address;
  int32_t ReJITId;
  int32_t ProcessId;
  int32_t CodeSize;
  int8_t CodeBytes[];
};
#pragma pack(pop)

#pragma pack(push, 1)
struct FunctionCallTargetMessage {
  int64_t FunctionId;
  int64_t Address;
  int32_t ReJITId;
  int32_t ProcessId;
  int32_t NameLength;
  char Name[];
};
#pragma pack(pop)

#pragma pack(push, 1)
struct RequestFunctionCodeMessage {
  int64_t FunctionId;
  int64_t Address;
  int32_t ReJITId;
  int32_t ProcessId;
};
#pragma pack(pop)

#ifdef PEX_TEST_HOOKS
struct NamedPipeClientTestHooks {
  void* context;
  void (*readPending)(void*);
  void (*readCompleted)(void*);
  void (*afterCancel)(void*);
  void (*beforeClose)(void*);
};
#endif

class NamedPipeClient {
  HANDLE handle_;
  HANDLE readEvent_;
  HANDLE writeEvent_;
  std::atomic<bool> disconnected_;
  std::mutex lock_;
  std::mutex readLock_;
  std::mutex disconnectLock_;
#ifdef PEX_TEST_HOOKS
  NamedPipeClientTestHooks testHooks_{};
#endif

 public:
  NamedPipeClient()
      : handle_(INVALID_HANDLE_VALUE),
        readEvent_(nullptr),
        writeEvent_(nullptr),
        disconnected_(false) {}

#ifdef PEX_TEST_HOOKS
  void SetTestHooks(NamedPipeClientTestHooks hooks) { testHooks_ = hooks; }
#endif

  ~NamedPipeClient() { Disconnect(); }

  bool Initialize(const wchar_t* pipeName) {
    disconnected_.store(false);
    readEvent_ = CreateEvent(nullptr, TRUE, TRUE, nullptr);
    writeEvent_ = CreateEvent(nullptr, TRUE, TRUE, nullptr);
    handle_ = CreateFile(pipeName, GENERIC_WRITE | GENERIC_READ,
                         FILE_SHARE_WRITE | FILE_SHARE_READ, nullptr,
                         OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);

    if (handle_ == INVALID_HANDLE_VALUE || readEvent_ == nullptr ||
        writeEvent_ == nullptr) {
      Disconnect();
      return false;
    }

    return true;
  }

  void Disconnect() {
    std::unique_lock<std::mutex> disconnectLock(disconnectLock_);
    disconnected_.store(true);
    HANDLE currentHandle = handle_;
    if (currentHandle != INVALID_HANDLE_VALUE) {
      CancelIoEx(currentHandle, nullptr);
    }
#ifdef PEX_TEST_HOOKS
    if (testHooks_.afterCancel != nullptr) {
      testHooks_.afterCancel(testHooks_.context);
    }
#endif

    std::unique_lock<std::mutex> readLock(readLock_);
    std::unique_lock<std::mutex> writeLock(lock_);
#ifdef PEX_TEST_HOOKS
    if (testHooks_.beforeClose != nullptr) {
      testHooks_.beforeClose(testHooks_.context);
    }
#endif
    if (handle_ != INVALID_HANDLE_VALUE) {
      CloseHandle(handle_);
      handle_ = INVALID_HANDLE_VALUE;
    }
    if (readEvent_ != nullptr) {
      CloseHandle(readEvent_);
      readEvent_ = nullptr;
    }
    if (writeEvent_ != nullptr) {
      CloseHandle(writeEvent_);
      writeEvent_ = nullptr;
    }
  }

  bool ReadMessage(PipeMessageHeader* header,
                   std::shared_ptr<char[]>& messageBody) {
    messageBody.reset();
    if (!ReadOverlapped(sizeof(PipeMessageHeader), header)) {
      return false;
    }

    const int32_t expectedSize =
        header->Kind == PipeMessageKind::RequestFunctionCode
            ? static_cast<int32_t>(sizeof(PipeMessageHeader) +
                                   sizeof(RequestFunctionCodeMessage))
            : static_cast<int32_t>(sizeof(PipeMessageHeader));
    if ((header->Kind != PipeMessageKind::RequestFunctionCode &&
         header->Kind != PipeMessageKind::EndSession) ||
        header->Size != expectedSize) {
      return false;
    }

    if (header->Size > sizeof(PipeMessageHeader)) {
      int messageSize = header->Size - sizeof(PipeMessageHeader);
      messageBody.reset(new char[messageSize]);

      if (!ReadOverlapped(messageSize, messageBody.get())) {
        return false;
      }
    }

    return true;
  }

  template <typename Action>
  void ReceiveMessages(Action handleMessage, bool& canceled) {
    while (!canceled) {
      PipeMessageHeader header;
      std::shared_ptr<char[]> messageBody;

      if (!ReadMessage(&header, messageBody)) {
        Disconnect();
        break;
      }

      // printf("Received %d, size %d\n", header.Kind, header.Size);
      handleMessage(header, messageBody);
    }
  }

  bool SendMessage(PipeMessageKind kind) {
    std::unique_lock<std::mutex> lock(lock_);
    return WriteMessageHeader(kind, 0);
  }

  template <class T>
  bool SendMessage(PipeMessageKind kind, const T& data) {
    std::unique_lock<std::mutex> lock(lock_);

    if (!WriteMessageHeader(kind, sizeof(T)))
      return false;

    return WriteOverlapped(data, sizeof(T));
  }

  bool SendMessage(PipeMessageKind kind, void* data, size_t dataSize) {
    std::unique_lock<std::mutex> lock(lock_);

    if (!WriteMessageHeader(kind, dataSize))
      return false;
    return WriteOverlapped(data, dataSize);
  }

  bool WriteMessageHeader(PipeMessageKind kind, size_t dataSize) {
    PipeMessageHeader header;
    header.Kind = kind;
    header.Size = (int32_t)dataSize + sizeof(header);
    return WriteOverlapped(&header, sizeof(header));
  }

  bool WriteOverlapped(void* data, size_t dataSize) {
    if (disconnected_.load() || handle_ == INVALID_HANDLE_VALUE ||
        writeEvent_ == nullptr) {
      return false;
    }

    DWORD bytesWritten;
    OVERLAPPED overlapped{};
    overlapped.hEvent = writeEvent_;
    overlapped.Offset = 0;
    overlapped.OffsetHigh = 0;

    if (!WriteFile(handle_, data, static_cast<DWORD>(dataSize), &bytesWritten,
                   &overlapped)) {
      if (GetLastError() != ERROR_IO_PENDING) {
        return false;
      }

      if (disconnected_.load()) {
        CancelIoEx(handle_, &overlapped);
      }

      WaitForSingleObject(writeEvent_, INFINITE);

      if (!GetOverlappedResult(handle_, &overlapped, &bytesWritten, false)) {
        return false;
      }
    }

    return bytesWritten == dataSize;
  }

  bool ReadOverlapped(size_t dataSize, void* dataOut) {
    std::unique_lock<std::mutex> readLock(readLock_);
    size_t totalRead = 0;
    while (totalRead < dataSize) {
      if (disconnected_.load() || handle_ == INVALID_HANDLE_VALUE ||
          readEvent_ == nullptr) {
        return false;
      }

      DWORD bytesRead = 0;
      OVERLAPPED overlapped{};
      overlapped.hEvent = readEvent_;
      if (!ReadFile(handle_, static_cast<char*>(dataOut) + totalRead,
                    static_cast<DWORD>(dataSize - totalRead), &bytesRead,
                    &overlapped)) {
        if (GetLastError() != ERROR_IO_PENDING) {
          return false;
        }
#ifdef PEX_TEST_HOOKS
        if (testHooks_.readPending != nullptr) {
          testHooks_.readPending(testHooks_.context);
        }
#endif

        if (disconnected_.load()) {
          CancelIoEx(handle_, &overlapped);
        }

        WaitForSingleObject(readEvent_, INFINITE);

        if (!GetOverlappedResult(handle_, &overlapped, &bytesRead, false)) {
#ifdef PEX_TEST_HOOKS
          if (testHooks_.readCompleted != nullptr) {
            testHooks_.readCompleted(testHooks_.context);
          }
#endif
          return false;
        }
#ifdef PEX_TEST_HOOKS
        if (testHooks_.readCompleted != nullptr) {
          testHooks_.readCompleted(testHooks_.context);
        }
#endif
      }

      if (bytesRead == 0) {
        return false;
      }
      totalRead += bytesRead;
    }

    return true;
  }
};

static LONG FilterCodeCopyException(DWORD exceptionCode) noexcept {
  return exceptionCode == EXCEPTION_ACCESS_VIOLATION ||
                 exceptionCode == EXCEPTION_IN_PAGE_ERROR
             ? EXCEPTION_EXECUTE_HANDLER
             : EXCEPTION_CONTINUE_SEARCH;
}

static bool TryCopyCodeBytes(void* destination,
                             const void* source,
                             size_t codeSize) {
  __try {
    memcpy(destination, source, codeSize);
    return true;
  } __except (FilterCodeCopyException(GetExceptionCode())) {
    return false;
  }
}

enum class FunctionCodeSendResult {
  Sent,
  InvalidInput,
  CopyFailed,
  TransportFailed,
};

static FunctionCodeSendResult SendFunctionCode(NamedPipeClient& client,
                                               int64_t functionId,
                                               int64_t address,
                                               int32_t reJITId,
                                               int32_t processId,
                                               int32_t codeSize,
                                               void* codeBytes) {
  if (codeSize <= 0 || codeBytes == nullptr) {
    return FunctionCodeSendResult::InvalidInput;
  }

  const size_t BUFFER_SIZE = 4 * 1024;

  char stackBuffer[BUFFER_SIZE];
  std::unique_ptr<char[]> dynamicBuffer;
  FunctionCodeMessage* message;
  size_t messageSize = sizeof(FunctionCodeMessage) + codeSize;

  if (messageSize < BUFFER_SIZE) {
    message = (FunctionCodeMessage*)stackBuffer;
  } else {
    dynamicBuffer =
        std::make_unique<char[]>(sizeof(FunctionCodeMessage) + codeSize);
    message = (FunctionCodeMessage*)dynamicBuffer.get();
  }

  message->FunctionId = functionId;
  message->Address = address;
  message->ReJITId = reJITId;
  message->ProcessId = processId;
  message->CodeSize = codeSize;
  if (!TryCopyCodeBytes(message->CodeBytes, codeBytes,
                        static_cast<size_t>(codeSize))) {
    return FunctionCodeSendResult::CopyFailed;
  }
  return client.SendMessage(PipeMessageKind::FunctionCode, message,
                            messageSize)
             ? FunctionCodeSendResult::Sent
             : FunctionCodeSendResult::TransportFailed;
}

static bool SendFunctionCallTarget(NamedPipeClient& client,
                                   int64_t functionId,
                                   int64_t address,
                                   int32_t reJITId,
                                   int32_t processId,
                                   int32_t nameLength,
                                   const char* name) {
  const size_t BUFFER_SIZE = 4 * 1024;

  char stackBuffer[BUFFER_SIZE];
  std::unique_ptr<char[]> dynamicBuffer;
  FunctionCallTargetMessage* message;
  size_t messageSize = sizeof(FunctionCallTargetMessage) + nameLength;

  if (messageSize < BUFFER_SIZE) {
    message = (FunctionCallTargetMessage*)stackBuffer;
  } else {
    dynamicBuffer = std::make_unique<char[]>(sizeof(FunctionCallTargetMessage) +
                                             nameLength);
    message = (FunctionCallTargetMessage*)dynamicBuffer.get();
  }

  message->FunctionId = functionId;
  message->Address = address;
  message->ReJITId = reJITId;
  message->ProcessId = processId;
  message->NameLength = nameLength;
  memcpy(message->Name, name, nameLength);
  return client.SendMessage(PipeMessageKind::FunctionCallTarget, message,
                            messageSize);
}

static bool SendFunctionCallTarget(NamedPipeClient& client,
                                   int64_t functionId,
                                   int64_t address,
                                   int32_t reJITId,
                                   int32_t processId,
                                   const char* name) {
  return SendFunctionCallTarget(client, functionId, address, reJITId, processId,
                                (int32_t)strlen(name) + 1, name);
}
