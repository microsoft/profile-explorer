using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;

namespace Client.DebugServer
{
    public class RequestResponsePair<T1, T2>
    {
        public T1 Request;
        public T2 Response;

        public RequestResponsePair(T1 request, T2 response = default(T2))
        {
            Request = request;
            Response = response;
        }
    }

    public class DebugService : global::DebugService.DebugServiceBase
    {
        public event EventHandler<StartSessionRequest> OnStartSession;
        public event EventHandler<UpdateIRRequest> OnUpdateIR;
        public event EventHandler<MarkElementRequest> OnMarkElement;
        public event EventHandler<SetCurrentElementRequest> OnSetCurrentElement;
        public event EventHandler<ElementCommandRequest> OnExecuteCommand;
        public event EventHandler<RequestResponsePair<ActiveBreakpointRequest, bool>> OnHasActiveBreakpoint;
        public event EventHandler<ClearHighlightingRequest> OnClearTemporaryHighlighting;
        public event EventHandler<CurrentStackFrameRequest> OnUpdateCurrentStackFrame;

        private bool handleSetCurrentElement_;

        const int Port = 50051;
        static Server serverInstance_;

        public DebugService()
        {
            handleSetCurrentElement_ = true;
        }

        static public bool StartServer(DebugService instance)
        {
            var options = new[] {
                new ChannelOption("grpc.max_receive_message_length", -1),
                new ChannelOption("grpc.max_send_message_length", -1), 
            };

            serverInstance_ = new Server(options)
            {       
                Services = { global::DebugService.BindService(instance) },
                Ports = { new ServerPort("localhost", Port, ServerCredentials.Insecure) }
            };

            serverInstance_.Start();
            return true;
        }

        public void StopServer()
        {
            serverInstance_.ShutdownAsync().Wait();
        }

        StartSessionResult HandleNewSession(StartSessionRequest request)
        {
            OnStartSession?.Invoke(this, request);
            return new StartSessionResult(){ SessionId = 1};
        }

        Result GetSuccessResult()
        {
            return new Result() { Success = true };
        }

        public override Task<StartSessionResult> StartSession(StartSessionRequest request, ServerCallContext context)
        {
            Trace.TraceInformation("Grpc: new session {0}, process {1}, args {2}",
                                   request.Kind, request.ProcessId, request.ProcessId);
            
            var result = HandleNewSession(request);
            return Task.FromResult(result);
        }

        public override Task<Result> EndSession(EndSessionRequest request, ServerCallContext context)
        {
            Trace.TraceInformation($"Grpc: end session {0}", request.SessionId);
            return Task.FromResult(GetSuccessResult());
        }

        public override Task<Result> UpdateIR(UpdateIRRequest request, ServerCallContext context)
        {
            Trace.TraceInformation("Grpc: update IR {0}, length {1}", 
            request.SessionId, request.Text.Length);
            OnUpdateIR?.Invoke(this, request);
            return Task.FromResult(GetSuccessResult());
        }

        public override Task<Result> MarkElement(MarkElementRequest request, ServerCallContext context)
        {
            Trace.TraceInformation("Grpc: mark element {0}, label {1}, kind {2}", 
                request.ElementAddress, request.Label, request.Highlighting);
            OnMarkElement?.Invoke(this, request);
            return Task.FromResult(GetSuccessResult());
        }

        public override Task<Result> SetCurrentElement(SetCurrentElementRequest request, ServerCallContext context)
        {
            Trace.TraceInformation("Grpc: set current element {0}, {1}, {2}, lable {3}", 
                request.ElementId, request.ElementKind, request.ElementAddress, request.Label);

            if (handleSetCurrentElement_)
            {
                OnSetCurrentElement?.Invoke(this, request);
            }

            return Task.FromResult(GetSuccessResult());
        }

        public override Task<Result> ExecuteCommand(ElementCommandRequest request, ServerCallContext context)
        {
            Trace.TraceInformation("Grpc: execute command {0}, element {1}", 
                                    request.Command, request.ElementAddress);
            OnExecuteCommand?.Invoke(this, request);
            return Task.FromResult(GetSuccessResult());
        }

        public override Task<ActiveBreakpointResult> HasActiveBreakpoint(ActiveBreakpointRequest request, ServerCallContext context)
        {
            Trace.TraceInformation("Grpc: has active breakpoint element {0}", request.ElementAddress);

            var query = new RequestResponsePair<ActiveBreakpointRequest, bool>(request);
            OnHasActiveBreakpoint?.Invoke(this, query);
            return Task.FromResult(new ActiveBreakpointResult()
            {
                HasBreakpoint = query.Response,
                Success = true
            });
        }

        public override Task<Result> ClearTemporaryHighlighting(ClearHighlightingRequest request, ServerCallContext context)
        {
            Trace.TraceInformation("Grpc: clear highlighting {0}", request.Highlighting);
            OnClearTemporaryHighlighting?.Invoke(this, request);
            return Task.FromResult(GetSuccessResult());
        }

        public override Task<Result> SetSessionState(SessionStateRequest request, ServerCallContext context)
        {
            Trace.TraceInformation("Grpc: set session state {0}", request.State);
            handleSetCurrentElement_ = request.State == global::SessionState.Listening;
            return Task.FromResult(GetSuccessResult());
        }

        public override Task<Result> UpdateCurrentStackFrame(CurrentStackFrameRequest request, ServerCallContext context)
        {
            Trace.TraceInformation("Grpc: update stack frame{0}", request.CurrentFrame);
            OnUpdateCurrentStackFrame?.Invoke(this, request);
            return Task.FromResult(GetSuccessResult());
        }
    }
}
