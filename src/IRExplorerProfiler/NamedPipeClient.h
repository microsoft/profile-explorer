#include <windows.h>
#include <stdio.h>
#include <memory>
#include <mutex>
#pragma once

enum class PipeMessageKind : int32_t
{
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


class NamedPipeClient
{
    HANDLE handle_;
    HANDLE readEvent_;
    HANDLE writeEvent_;
    std::mutex lock_;

public:
    NamedPipeClient()
    {
        handle_ = INVALID_HANDLE_VALUE;
    }

    ~NamedPipeClient()
    {
        Disconnect();
    }

    bool Initialize(const wchar_t* pipeName)
    {
        readEvent_ = CreateEvent(nullptr, TRUE, TRUE, nullptr);
        writeEvent_ = CreateEvent(nullptr, TRUE, TRUE, nullptr);
        handle_ = CreateFile(pipeName, GENERIC_WRITE | GENERIC_READ, FILE_SHARE_WRITE | FILE_SHARE_READ, nullptr,
                             OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
        
        return handle_ != INVALID_HANDLE_VALUE &&
            readEvent_ != INVALID_HANDLE_VALUE &&
            writeEvent_ != INVALID_HANDLE_VALUE;

    }

    void Disconnect()
    {
        if (handle_ != INVALID_HANDLE_VALUE) {
            CloseHandle(handle_);
            CloseHandle(readEvent_);
            CloseHandle(writeEvent_);
            handle_ = readEvent_ = writeEvent_ = INVALID_HANDLE_VALUE;
        }
    }

    bool ReadMessage(PipeMessageHeader* header, std::shared_ptr<char[]>& messageBody)
    {
        if (!ReadOverlapped(sizeof(PipeMessageHeader), header))
        {
            return false;
        }

        if (header->Size > sizeof(PipeMessageHeader))
        {
            int messageSize = header->Size - sizeof(PipeMessageHeader);
            messageBody.reset(new char[messageSize]);

            if (!ReadOverlapped(messageSize, messageBody.get()))
            {
                return false;
            }
        }

        return true;
    }

    template<typename Action>
    void ReceiveMessages(Action handleMessage, bool& canceled)
    {
        while (!canceled)
        {
            PipeMessageHeader header;
            std::shared_ptr<char[]> messageBody;

            if (!ReadMessage(&header, messageBody))
            {
                break;
            }

            //printf("Received %d, size %d\n", header.Kind, header.Size);
            handleMessage(header, messageBody);
        }
    }

    bool SendMessage(PipeMessageKind kind)
    {
        return WriteMessageHeader(kind, 0);
    }

    template <class T>
    bool SendMessage(PipeMessageKind kind, const T& data)
    {
        std::unique_lock<std::mutex> lock(lock_);

        if (!WriteMessageHeader(kind, sizeof(T)))
            return false;

        return WriteOverlapped(data, sizeof(T));
    }

    bool SendMessage(PipeMessageKind kind, void* data, size_t dataSize)
    {
        std::unique_lock<std::mutex> lock(lock_);

        if (!WriteMessageHeader(kind, dataSize))
            return false;
        return WriteOverlapped(data, dataSize);
    }

    bool WriteMessageHeader(PipeMessageKind kind, size_t dataSize)
    {
        PipeMessageHeader header;
        header.Kind = kind;
        header.Size = (int32_t)dataSize + sizeof(header);
        return WriteOverlapped(&header, sizeof(header));
    }

    bool WriteOverlapped(void* data, size_t dataSize)
    {
        DWORD bytesWritten;
        OVERLAPPED overlapped;
        overlapped.hEvent = writeEvent_;
        overlapped.Offset = 0;
        overlapped.OffsetHigh = 0;

        if (!WriteFile(handle_, data, dataSize, &bytesWritten, &overlapped)) {
            if (GetLastError() != ERROR_IO_PENDING)
            {
                return false;
            }

            WaitForSingleObject(writeEvent_, INFINITE);

            if (!GetOverlappedResult(handle_, &overlapped, &bytesWritten, false)) {
                return false;
            }
        }

        return bytesWritten == dataSize;
    }

    bool ReadOverlapped(size_t dataSize, void* dataOut)
    {
        DWORD bytesRead;
        OVERLAPPED overlapped;
        overlapped.hEvent = readEvent_;
        overlapped.Offset = 0;
        overlapped.OffsetHigh = 0;

        if (!ReadFile(handle_, dataOut, dataSize, &bytesRead, &overlapped))
        {
            if (GetLastError() != ERROR_IO_PENDING)
            {
                return false;
            }

            WaitForSingleObject(readEvent_, INFINITE);

            if (!GetOverlappedResult(handle_, &overlapped, &bytesRead, false)) {
                return false;
            }
        }

        return bytesRead == dataSize;
    }
};

static bool SendFunctionCode(NamedPipeClient& client, int64_t functionId,
    int64_t address,
    int32_t reJITId,
    int32_t processId,
    int32_t codeSize,
    void* codeBytes)
{
    const size_t BUFFER_SIZE = 4 * 1024;

    char stackBuffer[BUFFER_SIZE];
    std::unique_ptr<char[]> dynamicBuffer;
    FunctionCodeMessage* message;
    size_t messageSize = sizeof(FunctionCodeMessage) + codeSize;

    if (messageSize < BUFFER_SIZE)
    {
        message = (FunctionCodeMessage*)stackBuffer;
    }
    else {
        dynamicBuffer = std::make_unique<char[]>(sizeof(FunctionCodeMessage) + codeSize);
        message = (FunctionCodeMessage*)dynamicBuffer.get();
    }

    message->FunctionId = functionId;
    message->Address = address;
    message->ReJITId = reJITId;
    message->ProcessId = processId;
    message->CodeSize = codeSize;
    memcpy(message->CodeBytes, codeBytes, codeSize);
    return client.SendMessage(PipeMessageKind::FunctionCode, message, messageSize);
}

static bool SendFunctionCallTarget(NamedPipeClient& client, int64_t functionId,
    int64_t address,
    int32_t reJITId,
    int32_t processId,
    int32_t nameLength,
    const char* name)
{
    const size_t BUFFER_SIZE = 4 * 1024;

    char stackBuffer[BUFFER_SIZE];
    std::unique_ptr<char[]> dynamicBuffer;
    FunctionCallTargetMessage* message;
    size_t messageSize = sizeof(FunctionCallTargetMessage) + nameLength;

    if (messageSize < BUFFER_SIZE)
    {
        message = (FunctionCallTargetMessage*)stackBuffer;
    }
    else {
        dynamicBuffer = std::make_unique<char[]>(sizeof(FunctionCallTargetMessage) + nameLength);
        message = (FunctionCallTargetMessage*)dynamicBuffer.get();
    }

    message->FunctionId = functionId;
    message->Address = address;
    message->ReJITId = reJITId;
    message->ProcessId = processId;
    message->NameLength = nameLength;
    memcpy(message->Name, name, nameLength);
    return client.SendMessage(PipeMessageKind::FunctionCallTarget, message, messageSize);
}

static bool SendFunctionCallTarget(NamedPipeClient& client, int64_t functionId,
    int64_t address,
    int32_t reJITId,
    int32_t processId,
    const char* name) {
    return SendFunctionCallTarget(client, functionId, address, reJITId, processId, (int32_t)strlen(name) + 1, name);
}
