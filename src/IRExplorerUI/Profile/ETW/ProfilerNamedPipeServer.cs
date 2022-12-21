using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;

namespace IRExplorerUI.Profile;

public class ProfilerNamedPipeServer : IDisposable {
    enum MessageKind {
        StartSession,
        EndSession,
        FunctionCode,
        FunctionCallTarget,
        RequestFunctionCode,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct FunctionCodeMessage {
        public long FunctionId; // 0
        public long Address; // 8
        public int ReJITId; // 16
        public int ProcessId; // 20
        public int CodeSize; // 24
        // Code bytes start here at offset 28.
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct FunctionCallTargetMessage {
        public long FunctionId; // 0
        public long Address; // 8
        public int ReJITId; // 16
        public int ProcessId; // 20
        public int NameLength; // 20
        // UTF-8 name bytes start here at offset 28.
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct RequestFunctionCodeMessage {
        public long FunctionId; // 0
        public long Address; // 8
        public int ReJITId; // 16
        public int ProcessId; // 20
    }

    public delegate void FunctionCodeReceivedDelegate(long functionId, int rejitId, int processId, long address,
        int codeSize, byte[] codeBytes);

    public delegate void FunctionCallTargetsReceivedDelegate(long functionId, int rejitId, int processId, long address, string name);

    public const string PipeName = "IRXProfilerPipe";

    private NamedPipeServer instance_;

    public ProfilerNamedPipeServer() {
        instance_ = new NamedPipeServer(PipeName);
    }

    public event FunctionCodeReceivedDelegate FunctionCodeReceived;
    public event FunctionCallTargetsReceivedDelegate FunctionCallTargetsReceived;

    public bool StartReceiving(CancellationToken cancellationToken) {
        try {
            Trace.WriteLine("Start pipe reading thread");

            instance_.ReceiveMessages((header, body) => {
                if (cancellationToken.IsCancellationRequested) {
                    Trace.WriteLine($"Canceled {Environment.CurrentManagedThreadId}");
                    return;
                }

                switch ((MessageKind)header.Kind) {
                    case MessageKind.FunctionCode: {
                        var message = MemoryMarshal.Cast<byte, FunctionCodeMessage>(body)[0];
                        var code = body.AsSpan().Slice(28, message.CodeSize).ToArray();
                        FunctionCodeReceived?.Invoke(message.FunctionId, message.ReJITId, message.ProcessId,
                            message.Address, message.CodeSize, code);
                        break;
                    }
                    case MessageKind.FunctionCallTarget: {
                        var message = MemoryMarshal.Cast<byte, FunctionCallTargetMessage>(body)[0];
                        var nameBytes = body.AsSpan().Slice(28, message.NameLength);

                        // Don't include the null terminator.
                        if (nameBytes[^1] == 0) {
                            nameBytes = nameBytes.Slice(0, nameBytes.Length - 1);
                        }

                        var name = Encoding.UTF8.GetString(nameBytes);
                        FunctionCallTargetsReceived?.Invoke(message.FunctionId, message.ReJITId, message.ProcessId,
                            message.Address, name);
                        break;
                    }
                }
            }, cancellationToken);
            return true;
        }
        catch (Exception ex) {
            Trace.WriteLine($"Failed to receive messages: {ex}");
            return false;
        }
    }

    public bool EndSession() {
        try {
            instance_.SendMessage((int)MessageKind.EndSession);
            return true;
        }
        catch (Exception ex) {
            Trace.WriteLine($"Failed to send message: {ex}");
            return false;
        }
    }

    public bool RequestFunctionCode(long address, long functionId, int rejitId, int processId) {
        try {
            var message = new RequestFunctionCodeMessage() {
                FunctionId = functionId,
                ReJITId = rejitId,
                Address = address,
                ProcessId = processId
            };

            instance_.SendMessage((int)MessageKind.RequestFunctionCode, message);
            return true;
        }
        catch (Exception ex) {
            Trace.WriteLine($"Failed to send message: {ex}");
            return false;
        }
    }
    
    public void Stop() {
        instance_.Dispose();
        instance_ = null;
    }

    public void Dispose() {
        if (instance_ != null) {
            Stop();
        }
    }
}