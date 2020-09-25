#define _WIN32_WINNT 0x600
#define _SILENCE_ALL_CXX20_DEPRECATION_WARNINGS 1
#define _SILENCE_ALL_CXX17_DEPRECATION_WARNINGS 1

#pragma warning(disable: 4251 4508 4988)
#include "DebugService.grpc.pb.h"
#include "DebugService.pb.h"
#include <grpc/grpc.h>
#include <grpcpp/channel.h>
#include <grpcpp/client_context.h>
#include <grpcpp/create_channel.h>
#include <grpcpp/security/credentials.h>
#include <windows.h>
#include <chrono>
using namespace std::chrono;

static bool ClientConnected;
static bool SessionStarted;
static std::shared_ptr<grpc::Channel> ChannelInstance;
static std::shared_ptr<DebugService::Stub> ClientInstance;
static int64_t SessionId;
static const int RequestTimeout = 500;

static bool ConnectClient() {
    if (ClientConnected) {
        return true;
    }

    ChannelInstance = grpc::CreateChannel("localhost:50051", grpc::InsecureChannelCredentials());
    ClientInstance = DebugService::NewStub(ChannelInstance);
    ClientConnected = true;

    grpc::ClientContext context;
    context.set_deadline(std::chrono::system_clock::now() + std::chrono::milliseconds(RequestTimeout));

    StartSessionResult startResponse;
    StartSessionRequest startRequest;
    startRequest.set_kind(ClientKind::runtime);
    startRequest.set_processid(::GetCurrentProcessId());

    ClientInstance->StartSession(&context, startRequest, &startResponse);
    SessionId = startResponse.sessionid();
    //printf("   Session: %d, status %d\n", SessionId, startResponse.errorcode()); fflush(stdout);

    SessionStarted = startResponse.errorcode() == 0 && startResponse.sessionid() != 0;
    return SessionStarted;
}

static void WaitForAsyncResponse(grpc::CompletionQueue& cq) {
    void* tag;
    bool ok;
    cq.AsyncNext(&tag, &ok, std::chrono::system_clock::now() + std::chrono::milliseconds(RequestTimeout));
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
            //printf("Failed to connect client\n"); fflush(stdout);
            return false;
        }

        grpc::Status status;
        grpc::CompletionQueue cq;
        grpc::ClientContext context;
        //context.set_compression_algorithm(GRPC_COMPRESS_STREAM_GZIP);

        UpdateIRRequest request;
        Result response;
        request.set_sessionid(SessionId);
        request.set_text(text);

        std::unique_ptr <grpc::ClientAsyncResponseReader <Result>> rpc(ClientInstance->PrepareAsyncUpdateIR(&context, request, &cq));
        rpc->StartCall();
        rpc->Finish(&response, &status, (void*)1);

        WaitForAsyncResponse(cq);
        return response.success();
    }

    int __cdecl IrxMarkElement(void* elementAddress, const char* label = nullptr) {
        if (!IsDebuggerAttached()) {
            return true;
        }

        if (!SessionStarted) {
            return false;
        }

        grpc::ClientContext context;
        grpc::Status status;
        grpc::CompletionQueue cq;

        MarkElementRequest request;
        Result response;
        //request.set_sessionid(SessionId);
        request.set_elementaddress(reinterpret_cast<int64_t>(elementAddress));
        request.set_label(label);

        std::unique_ptr <grpc::ClientAsyncResponseReader <Result>> rpc(ClientInstance->PrepareAsyncMarkElement(&context, request, &cq));
        rpc->StartCall();
        rpc->Finish(&response, &status, (void*)1);

        WaitForAsyncResponse(cq);
        return response.success();
    }

    int __cdecl IrxHasActiveBreakpoint(void* elementAddress, bool* result) {
        if (!IsDebuggerAttached()) {
            return true;
        }

        if (!SessionStarted) {
            return false;
        }

        grpc::ClientContext context;
        grpc::Status status;
        grpc::CompletionQueue cq;

        ActiveBreakpointRequest request;
        ActiveBreakpointResult response;
        //request.set_sessionid(SessionId);
        request.set_elementaddress(reinterpret_cast<int64_t>(elementAddress));

        std::unique_ptr <grpc::ClientAsyncResponseReader <ActiveBreakpointResult>> rpc(ClientInstance->PrepareAsyncHasActiveBreakpoint(&context, request, &cq));
        rpc->StartCall();
        rpc->Finish(&response, &status, (void*)1);

        WaitForAsyncResponse(cq);
        *result = response.hasbreakpoint();
        return response.success();
    }

    int __cdecl IrxSetCurrentElement(int elementId, void* elementAddress, int elementKind, const char* label = nullptr) {
        if (!IsDebuggerAttached()) {
            return true;
        }

        if (!SessionStarted) {
            return false;
        }

        grpc::ClientContext context;
        grpc::Status status;
        grpc::CompletionQueue cq;

        SetCurrentElementRequest request;
        Result response;
        //request.set_sessionid(SessionId);
        request.set_elementid(elementId);
        request.set_elementaddress(reinterpret_cast<int64_t>(elementAddress));
        request.set_elementkind(static_cast<IRElementKind>(elementKind));
        request.set_label(label);

        std::unique_ptr <grpc::ClientAsyncResponseReader <Result>> rpc(ClientInstance->PrepareAsyncSetCurrentElement(&context, request, &cq));
        rpc->StartCall();
        rpc->Finish(&response, &status, (void*)1);

        WaitForAsyncResponse(cq);
        return response.success();
    }


} // extern "C"

//int main() {
//    IrxUpdateIR("Testing");
//    return 0;
//}