// Generated by the gRPC C++ plugin.
// If you make any local change, they will be lost.
// source: DebugService.proto

#include "DebugService.pb.h"
#include "DebugService.grpc.pb.h"

#include <functional>
#include <grpcpp/impl/codegen/async_stream.h>
#include <grpcpp/impl/codegen/async_unary_call.h>
#include <grpcpp/impl/codegen/channel_interface.h>
#include <grpcpp/impl/codegen/client_unary_call.h>
#include <grpcpp/impl/codegen/client_callback.h>
#include <grpcpp/impl/codegen/message_allocator.h>
#include <grpcpp/impl/codegen/method_handler.h>
#include <grpcpp/impl/codegen/rpc_service_method.h>
#include <grpcpp/impl/codegen/server_callback.h>
#include <grpcpp/impl/codegen/server_callback_handlers.h>
#include <grpcpp/impl/codegen/server_context.h>
#include <grpcpp/impl/codegen/service_type.h>
#include <grpcpp/impl/codegen/sync_stream.h>

static const char* DebugService_method_names[] = {
  "/DebugService/StartSession",
  "/DebugService/EndSession",
  "/DebugService/UpdateIR",
  "/DebugService/MarkElement",
  "/DebugService/SetCurrentElement",
  "/DebugService/ExecuteCommand",
  "/DebugService/HasActiveBreakpoint",
};

std::unique_ptr< DebugService::Stub> DebugService::NewStub(const std::shared_ptr< ::grpc::ChannelInterface>& channel, const ::grpc::StubOptions& options) {
  (void)options;
  std::unique_ptr< DebugService::Stub> stub(new DebugService::Stub(channel));
  return stub;
}

DebugService::Stub::Stub(const std::shared_ptr< ::grpc::ChannelInterface>& channel)
  : channel_(channel), rpcmethod_StartSession_(DebugService_method_names[0], ::grpc::internal::RpcMethod::NORMAL_RPC, channel)
  , rpcmethod_EndSession_(DebugService_method_names[1], ::grpc::internal::RpcMethod::NORMAL_RPC, channel)
  , rpcmethod_UpdateIR_(DebugService_method_names[2], ::grpc::internal::RpcMethod::NORMAL_RPC, channel)
  , rpcmethod_MarkElement_(DebugService_method_names[3], ::grpc::internal::RpcMethod::NORMAL_RPC, channel)
  , rpcmethod_SetCurrentElement_(DebugService_method_names[4], ::grpc::internal::RpcMethod::NORMAL_RPC, channel)
  , rpcmethod_ExecuteCommand_(DebugService_method_names[5], ::grpc::internal::RpcMethod::NORMAL_RPC, channel)
  , rpcmethod_HasActiveBreakpoint_(DebugService_method_names[6], ::grpc::internal::RpcMethod::NORMAL_RPC, channel)
  {}

::grpc::Status DebugService::Stub::StartSession(::grpc::ClientContext* context, const ::StartSessionRequest& request, ::StartSessionResult* response) {
  return ::grpc::internal::BlockingUnaryCall(channel_.get(), rpcmethod_StartSession_, context, request, response);
}

void DebugService::Stub::experimental_async::StartSession(::grpc::ClientContext* context, const ::StartSessionRequest* request, ::StartSessionResult* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_StartSession_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::StartSession(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::StartSessionResult* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_StartSession_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::StartSession(::grpc::ClientContext* context, const ::StartSessionRequest* request, ::StartSessionResult* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_StartSession_, context, request, response, reactor);
}

void DebugService::Stub::experimental_async::StartSession(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::StartSessionResult* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_StartSession_, context, request, response, reactor);
}

::grpc::ClientAsyncResponseReader< ::StartSessionResult>* DebugService::Stub::AsyncStartSessionRaw(::grpc::ClientContext* context, const ::StartSessionRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::StartSessionResult>::Create(channel_.get(), cq, rpcmethod_StartSession_, context, request, true);
}

::grpc::ClientAsyncResponseReader< ::StartSessionResult>* DebugService::Stub::PrepareAsyncStartSessionRaw(::grpc::ClientContext* context, const ::StartSessionRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::StartSessionResult>::Create(channel_.get(), cq, rpcmethod_StartSession_, context, request, false);
}

::grpc::Status DebugService::Stub::EndSession(::grpc::ClientContext* context, const ::EndSessionRequest& request, ::Result* response) {
  return ::grpc::internal::BlockingUnaryCall(channel_.get(), rpcmethod_EndSession_, context, request, response);
}

void DebugService::Stub::experimental_async::EndSession(::grpc::ClientContext* context, const ::EndSessionRequest* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_EndSession_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::EndSession(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_EndSession_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::EndSession(::grpc::ClientContext* context, const ::EndSessionRequest* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_EndSession_, context, request, response, reactor);
}

void DebugService::Stub::experimental_async::EndSession(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_EndSession_, context, request, response, reactor);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::AsyncEndSessionRaw(::grpc::ClientContext* context, const ::EndSessionRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_EndSession_, context, request, true);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::PrepareAsyncEndSessionRaw(::grpc::ClientContext* context, const ::EndSessionRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_EndSession_, context, request, false);
}

::grpc::Status DebugService::Stub::UpdateIR(::grpc::ClientContext* context, const ::UpdateIRRequest& request, ::Result* response) {
  return ::grpc::internal::BlockingUnaryCall(channel_.get(), rpcmethod_UpdateIR_, context, request, response);
}

void DebugService::Stub::experimental_async::UpdateIR(::grpc::ClientContext* context, const ::UpdateIRRequest* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_UpdateIR_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::UpdateIR(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_UpdateIR_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::UpdateIR(::grpc::ClientContext* context, const ::UpdateIRRequest* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_UpdateIR_, context, request, response, reactor);
}

void DebugService::Stub::experimental_async::UpdateIR(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_UpdateIR_, context, request, response, reactor);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::AsyncUpdateIRRaw(::grpc::ClientContext* context, const ::UpdateIRRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_UpdateIR_, context, request, true);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::PrepareAsyncUpdateIRRaw(::grpc::ClientContext* context, const ::UpdateIRRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_UpdateIR_, context, request, false);
}

::grpc::Status DebugService::Stub::MarkElement(::grpc::ClientContext* context, const ::MarkElementRequest& request, ::Result* response) {
  return ::grpc::internal::BlockingUnaryCall(channel_.get(), rpcmethod_MarkElement_, context, request, response);
}

void DebugService::Stub::experimental_async::MarkElement(::grpc::ClientContext* context, const ::MarkElementRequest* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_MarkElement_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::MarkElement(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_MarkElement_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::MarkElement(::grpc::ClientContext* context, const ::MarkElementRequest* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_MarkElement_, context, request, response, reactor);
}

void DebugService::Stub::experimental_async::MarkElement(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_MarkElement_, context, request, response, reactor);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::AsyncMarkElementRaw(::grpc::ClientContext* context, const ::MarkElementRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_MarkElement_, context, request, true);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::PrepareAsyncMarkElementRaw(::grpc::ClientContext* context, const ::MarkElementRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_MarkElement_, context, request, false);
}

::grpc::Status DebugService::Stub::SetCurrentElement(::grpc::ClientContext* context, const ::SetCurrentElementRequest& request, ::Result* response) {
  return ::grpc::internal::BlockingUnaryCall(channel_.get(), rpcmethod_SetCurrentElement_, context, request, response);
}

void DebugService::Stub::experimental_async::SetCurrentElement(::grpc::ClientContext* context, const ::SetCurrentElementRequest* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_SetCurrentElement_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::SetCurrentElement(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_SetCurrentElement_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::SetCurrentElement(::grpc::ClientContext* context, const ::SetCurrentElementRequest* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_SetCurrentElement_, context, request, response, reactor);
}

void DebugService::Stub::experimental_async::SetCurrentElement(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_SetCurrentElement_, context, request, response, reactor);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::AsyncSetCurrentElementRaw(::grpc::ClientContext* context, const ::SetCurrentElementRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_SetCurrentElement_, context, request, true);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::PrepareAsyncSetCurrentElementRaw(::grpc::ClientContext* context, const ::SetCurrentElementRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_SetCurrentElement_, context, request, false);
}

::grpc::Status DebugService::Stub::ExecuteCommand(::grpc::ClientContext* context, const ::ElementCommandRequest& request, ::Result* response) {
  return ::grpc::internal::BlockingUnaryCall(channel_.get(), rpcmethod_ExecuteCommand_, context, request, response);
}

void DebugService::Stub::experimental_async::ExecuteCommand(::grpc::ClientContext* context, const ::ElementCommandRequest* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_ExecuteCommand_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::ExecuteCommand(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_ExecuteCommand_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::ExecuteCommand(::grpc::ClientContext* context, const ::ElementCommandRequest* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_ExecuteCommand_, context, request, response, reactor);
}

void DebugService::Stub::experimental_async::ExecuteCommand(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::Result* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_ExecuteCommand_, context, request, response, reactor);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::AsyncExecuteCommandRaw(::grpc::ClientContext* context, const ::ElementCommandRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_ExecuteCommand_, context, request, true);
}

::grpc::ClientAsyncResponseReader< ::Result>* DebugService::Stub::PrepareAsyncExecuteCommandRaw(::grpc::ClientContext* context, const ::ElementCommandRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::Result>::Create(channel_.get(), cq, rpcmethod_ExecuteCommand_, context, request, false);
}

::grpc::Status DebugService::Stub::HasActiveBreakpoint(::grpc::ClientContext* context, const ::ActiveBreakpointRequest& request, ::ActiveBreakpointResult* response) {
  return ::grpc::internal::BlockingUnaryCall(channel_.get(), rpcmethod_HasActiveBreakpoint_, context, request, response);
}

void DebugService::Stub::experimental_async::HasActiveBreakpoint(::grpc::ClientContext* context, const ::ActiveBreakpointRequest* request, ::ActiveBreakpointResult* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_HasActiveBreakpoint_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::HasActiveBreakpoint(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::ActiveBreakpointResult* response, std::function<void(::grpc::Status)> f) {
  ::grpc_impl::internal::CallbackUnaryCall(stub_->channel_.get(), stub_->rpcmethod_HasActiveBreakpoint_, context, request, response, std::move(f));
}

void DebugService::Stub::experimental_async::HasActiveBreakpoint(::grpc::ClientContext* context, const ::ActiveBreakpointRequest* request, ::ActiveBreakpointResult* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_HasActiveBreakpoint_, context, request, response, reactor);
}

void DebugService::Stub::experimental_async::HasActiveBreakpoint(::grpc::ClientContext* context, const ::grpc::ByteBuffer* request, ::ActiveBreakpointResult* response, ::grpc::experimental::ClientUnaryReactor* reactor) {
  ::grpc_impl::internal::ClientCallbackUnaryFactory::Create(stub_->channel_.get(), stub_->rpcmethod_HasActiveBreakpoint_, context, request, response, reactor);
}

::grpc::ClientAsyncResponseReader< ::ActiveBreakpointResult>* DebugService::Stub::AsyncHasActiveBreakpointRaw(::grpc::ClientContext* context, const ::ActiveBreakpointRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::ActiveBreakpointResult>::Create(channel_.get(), cq, rpcmethod_HasActiveBreakpoint_, context, request, true);
}

::grpc::ClientAsyncResponseReader< ::ActiveBreakpointResult>* DebugService::Stub::PrepareAsyncHasActiveBreakpointRaw(::grpc::ClientContext* context, const ::ActiveBreakpointRequest& request, ::grpc::CompletionQueue* cq) {
  return ::grpc_impl::internal::ClientAsyncResponseReaderFactory< ::ActiveBreakpointResult>::Create(channel_.get(), cq, rpcmethod_HasActiveBreakpoint_, context, request, false);
}

DebugService::Service::Service() {
  AddMethod(new ::grpc::internal::RpcServiceMethod(
      DebugService_method_names[0],
      ::grpc::internal::RpcMethod::NORMAL_RPC,
      new ::grpc::internal::RpcMethodHandler< DebugService::Service, ::StartSessionRequest, ::StartSessionResult>(
          std::mem_fn(&DebugService::Service::StartSession), this)));
  AddMethod(new ::grpc::internal::RpcServiceMethod(
      DebugService_method_names[1],
      ::grpc::internal::RpcMethod::NORMAL_RPC,
      new ::grpc::internal::RpcMethodHandler< DebugService::Service, ::EndSessionRequest, ::Result>(
          std::mem_fn(&DebugService::Service::EndSession), this)));
  AddMethod(new ::grpc::internal::RpcServiceMethod(
      DebugService_method_names[2],
      ::grpc::internal::RpcMethod::NORMAL_RPC,
      new ::grpc::internal::RpcMethodHandler< DebugService::Service, ::UpdateIRRequest, ::Result>(
          std::mem_fn(&DebugService::Service::UpdateIR), this)));
  AddMethod(new ::grpc::internal::RpcServiceMethod(
      DebugService_method_names[3],
      ::grpc::internal::RpcMethod::NORMAL_RPC,
      new ::grpc::internal::RpcMethodHandler< DebugService::Service, ::MarkElementRequest, ::Result>(
          std::mem_fn(&DebugService::Service::MarkElement), this)));
  AddMethod(new ::grpc::internal::RpcServiceMethod(
      DebugService_method_names[4],
      ::grpc::internal::RpcMethod::NORMAL_RPC,
      new ::grpc::internal::RpcMethodHandler< DebugService::Service, ::SetCurrentElementRequest, ::Result>(
          std::mem_fn(&DebugService::Service::SetCurrentElement), this)));
  AddMethod(new ::grpc::internal::RpcServiceMethod(
      DebugService_method_names[5],
      ::grpc::internal::RpcMethod::NORMAL_RPC,
      new ::grpc::internal::RpcMethodHandler< DebugService::Service, ::ElementCommandRequest, ::Result>(
          std::mem_fn(&DebugService::Service::ExecuteCommand), this)));
  AddMethod(new ::grpc::internal::RpcServiceMethod(
      DebugService_method_names[6],
      ::grpc::internal::RpcMethod::NORMAL_RPC,
      new ::grpc::internal::RpcMethodHandler< DebugService::Service, ::ActiveBreakpointRequest, ::ActiveBreakpointResult>(
          std::mem_fn(&DebugService::Service::HasActiveBreakpoint), this)));
}

DebugService::Service::~Service() {
}

::grpc::Status DebugService::Service::StartSession(::grpc::ServerContext* context, const ::StartSessionRequest* request, ::StartSessionResult* response) {
  (void) context;
  (void) request;
  (void) response;
  return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}

::grpc::Status DebugService::Service::EndSession(::grpc::ServerContext* context, const ::EndSessionRequest* request, ::Result* response) {
  (void) context;
  (void) request;
  (void) response;
  return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}

::grpc::Status DebugService::Service::UpdateIR(::grpc::ServerContext* context, const ::UpdateIRRequest* request, ::Result* response) {
  (void) context;
  (void) request;
  (void) response;
  return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}

::grpc::Status DebugService::Service::MarkElement(::grpc::ServerContext* context, const ::MarkElementRequest* request, ::Result* response) {
  (void) context;
  (void) request;
  (void) response;
  return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}

::grpc::Status DebugService::Service::SetCurrentElement(::grpc::ServerContext* context, const ::SetCurrentElementRequest* request, ::Result* response) {
  (void) context;
  (void) request;
  (void) response;
  return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}

::grpc::Status DebugService::Service::ExecuteCommand(::grpc::ServerContext* context, const ::ElementCommandRequest* request, ::Result* response) {
  (void) context;
  (void) request;
  (void) response;
  return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}

::grpc::Status DebugService::Service::HasActiveBreakpoint(::grpc::ServerContext* context, const ::ActiveBreakpointRequest* request, ::ActiveBreakpointResult* response) {
  (void) context;
  (void) request;
  (void) response;
  return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}


