#define _WIN32_WINNT 0x600
#define _SILENCE_ALL_CXX20_DEPRECATION_WARNINGS 1
#define _SILENCE_ALL_CXX17_DEPRECATION_WARNINGS 1

#pragma warning(disable: 4251 4508 4988)
#include "irexplorer/DebugService.grpc.pb.h"
#include "irexplorer/DebugService.pb.h"
#include <grpc/grpc.h>
#include <grpcpp/channel.h>
#include <grpcpp/client_context.h>
#include <grpcpp/create_channel.h>
#include <grpcpp/security/credentials.h>
#include <windows.h>
#include <chrono>

static bool ClientConnected;
static std::shared_ptr<grpc::Channel> ChannelInstance;
static std::shared_ptr<DebugService::Stub> ClientInstance;
static int64_t SessionId;
static const int RequestTimeout = 2;

static bool ConnectClient() {
    if (ClientConnected) {
        return true;
    }

    ChannelInstance = grpc::CreateChannel("localhost:50051", grpc::InsecureChannelCredentials());
    ClientInstance = DebugService::NewStub(ChannelInstance);
    ClientConnected = true;

    grpc::ClientContext context;
    context.set_deadline(std::chrono::system_clock::now() + std::chrono::seconds(RequestTimeout));

    StartSessionResult startResponse;
    StartSessionRequest startRequest;
    startRequest.set_kind(ClientKind::runtime);
    startRequest.set_processid(::GetCurrentProcessId());

    ClientInstance->StartSession(&context, startRequest, &startResponse);
    SessionId = startResponse.sessionid();
    return true;
}

static bool IsDebuggerAttached() {
    return ::IsDebuggerPresent();
}

extern "C" {
int __cdecl IrxUpdateIR(const char* text) {
    if (!IsDebuggerAttached()) {
        return true;
    }

    if (!ConnectClient()) {
        return false;
    }

    grpc::ClientContext context;
    //context.set_compression_algorithm(GRPC_COMPRESS_STREAM_GZIP);
    context.set_deadline(std::chrono::system_clock::now() + std::chrono::seconds(RequestTimeout));

    UpdateIRRequest request;
    Result response;
    request.set_sessionid(SessionId);
    request.set_text(text);

    ClientInstance->UpdateIR(&context, request, &response);
    return response.success();
}

int __cdecl IrxMarkElement(void* elementAddress, const char* label = nullptr) {
    if (!IsDebuggerAttached()) {
        return true;
    }

    if (!ConnectClient()) {
        return false;
    }

    grpc::ClientContext context;
    context.set_deadline(std::chrono::system_clock::now() + std::chrono::seconds(RequestTimeout));

    MarkElementRequest request;
    Result response;
    //request.set_sessionid(SessionId);
    request.set_elementaddress(reinterpret_cast<int64_t>(elementAddress));
    request.set_label(label);

    ClientInstance->MarkElement(&context, request, &response);
    return response.success();
}

int __cdecl IrxHasActiveBreakpoint(void* elementAddress, bool* result) {
    if (!IsDebuggerAttached()) {
        return true;
    }

    if (!ConnectClient()) {
        return false;
    }

    grpc::ClientContext context;
    context.set_deadline(std::chrono::system_clock::now() + std::chrono::seconds(RequestTimeout));

    ActiveBreakpointRequest request;
    ActiveBreakpointResult response;
    //request.set_sessionid(SessionId);
    request.set_elementaddress(reinterpret_cast<int64_t>(elementAddress));

    ClientInstance->HasActiveBreakpoint(&context, request, &response);
    *result = response.hasbreakpoint();
    return response.success();
}

int __cdecl IrxSetCurrentElement(int elementId, void* elementAddress, int elementKind, const char* label = nullptr) {
    if (!IsDebuggerAttached()) {
        return true;
    }

    if (!ConnectClient()) {
        return false;
    }

    grpc::ClientContext context;
    context.set_deadline(std::chrono::system_clock::now() + std::chrono::seconds(RequestTimeout));

    SetCurrentElementRequest request;
    Result response;
    //request.set_sessionid(SessionId);
    request.set_elementid(elementId);
    request.set_elementaddress(reinterpret_cast<int64_t>(elementAddress));
    request.set_elementkind(static_cast<IRElementKind>(elementKind));
    request.set_label(label);

    ClientInstance->SetCurrentElement(&context, request, &response);
    return response.success();
}


} // extern "C"
//
//int main() {
//    IrxUpdateIR("Testing");
//    return 0;
//}